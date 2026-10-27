using System.Text.Json;
using System.Text.Json.Nodes;
using DeeKeep.Common.Audio;
using DeeKeep.Common.Events;
using DeeKeep.Common.Files;
using DeeKeep.Common.Jobs;
using DeeKeep.Common.Outbox;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Catalog;
using DeeKeep.Contracts.Clients;
using DeeKeep.Contracts.Downloader;
using DeeKeep.Contracts.Library;
using DeeKeep.Downloader.Extraction;
using DeeKeep.Downloader.Settings;

namespace DeeKeep.Downloader.Jobs;

/// <summary>
/// Socle des trois tâches d'extraction : progression par ligne (texte exact de
/// l'original), échec avec la cause exacte, enregistrement du fichier produit
/// auprès de Library par la boîte d'envoi (rejouée jusqu'au succès) et événement.
/// </summary>
public abstract class ExtractionJobBase(
    TrackExtractor extractor,
    DownloaderSettings settings,
    CatalogClient catalog,
    PathMap pathMap,
    OutboxQueue outbox,
    EventLog events,
    ILogger logger)
{
    protected TrackExtractor Extractor { get; } = extractor;
    protected DownloaderSettings Settings { get; } = settings;
    protected CatalogClient Catalog { get; } = catalog;
    protected PathMap PathMap { get; } = pathMap;
    protected ILogger Logger { get; } = logger;

    /// <summary>Progression : le texte change → publié aussitôt ; seul le pourcentage change → au plus 4 fois par seconde.</summary>
    protected static Action<int, string> Reporter(JobContext context)
    {
        string? lastText = null;
        var gate = new object();
        return (pct, text) =>
        {
            lock (gate)
            {
                var force = text != lastText;
                lastText = text;
                context.Report(Math.Clamp(pct, 0, 100), 100, text, force: force);
            }
        };
    }

    /// <summary>Mène l'extraction ; échec → message de statut (« Erreur / Abandonné (n essais) ») et cause exacte dans l'erreur.</summary>
    protected async Task<ExtractionOutcome> ExtractAsync(JobContext context, ExtractionRequest request, CancellationToken ct)
    {
        ExtractionOutcome outcome;
        try
        {
            outcome = await Extractor.RunAsync(request, ct);
        }
        catch (ServiceUnavailableException ex)
        {
            // Library injoignable (emplacement) : la tâche est remise en file, rien n'est perdu.
            throw new TransientJobException($"Service {ex.Service} indisponible : {ex.Message}", ex);
        }
        if (!outcome.Success)
        {
            context.Report(0, 100, Failures.FailedStatus(outcome.Attempts), force: true);
            throw new PermanentJobException(outcome.Message);
        }
        return outcome;
    }

    protected void Register(string localPath, string origin, bool addToHistory, string? replacedLocal, TrackInfo track)
    {
        outbox.Enqueue(ServiceNames.Library, HttpMethod.Post, "/api/files/register",
            new RegisterFileRequest(localPath, origin, addToHistory, replacedLocal, track.DeezerId, track.CoverUrl,
                addToHistory && track.Cover is { Length: > 0 } c ? Convert.ToBase64String(c) : null));
    }

    protected void Publish(string type, FileProducedEvent evt) => events.Publish(type, evt, evt.Path);

    protected JsonNode? Result(ExtractionOutcome outcome, TrackInfo track, string format, string quality)
    {
        var dbPath = PathMap.ToDb(outcome.Path!);
        var result = new DownloadJobResult(outcome.Path, dbPath, outcome.Video?.Id, outcome.Video?.Title, outcome.Attempts, null,
            track.Title, track.Artist, track.Album, format.ToUpperInvariant(), ExportFormats.TargetBitrate(quality), track.CoverUrl, track.DeezerId);
        return JsonSerializer.SerializeToNode(result, JsonDefaults.Options);
    }

    /// <summary>Emplacement selon les règles « Organiser » (service Library : sous-dossiers, motif, « Divers »).</summary>
    protected static Func<CancellationToken, Task<ExtractionTarget>> LibraryTarget(LibraryClient library, TrackInfo track, string format,
        string quality, string batchId) => async ct =>
    {
        var tags = track.ExtraTags;
        var response = await library.ResolveDestinationAsync(new DestinationRequest(
            track.Title, track.Artist, track.Album, tags.GetValueOrDefault("albumartist"), tags.GetValueOrDefault("date"),
            tags.GetValueOrDefault("genre"), Int(tags.GetValueOrDefault("track")), Int(tags.GetValueOrDefault("tracktotal")),
            Int(tags.GetValueOrDefault("disc")), "." + format, batchId, ExportFormats.TargetBitrate(quality)), ct);
        return new ExtractionTarget(Path.GetDirectoryName(response.LocalPath)!, Path.GetFileNameWithoutExtension(response.LocalPath));
    };

    protected static int? Int(string? text) =>
        int.TryParse((text ?? "").Split('/')[0].Trim(), System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
}

/// <summary>
/// Tâche « download » : une piste Deezer de la page Musique. Métadonnées complètes
/// demandées au catalogue, jaquette pleine taille, rangement selon les règles
/// « Organiser », ajout à l'historique.
/// </summary>
public sealed class DownloadJobHandler(
    TrackExtractor extractor, DownloaderSettings settings, CatalogClient catalog, LibraryClient library, PathMap pathMap,
    OutboxQueue outbox, EventLog events, ILogger<DownloadJobHandler> logger)
    : ExtractionJobBase(extractor, settings, catalog, pathMap, outbox, events, logger), IJobHandler
{
    public async Task<JsonNode?> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var payload = context.GetPayload<DownloadJobPayload>();
        var t = payload.Track;
        var export = Settings.Export(payload.Options);
        var format = export.Format.ToLowerInvariant();
        var report = Reporter(context);

        var track = new TrackInfo { Title = t.Title, Artist = t.Artist, Album = t.Album ?? "", DeezerId = t.Id };
        report(3, "Initialisation...");
        // Avant l'emplacement : le motif de nommage peut réclamer l'année ou le rang.
        await TrackMetadata.ApplyCatalogAsync(track, Catalog, t, t.Id, Logger, ct);

        var outcome = await ExtractAsync(context, new ExtractionRequest
        {
            Track = track,
            Format = format,
            Quality = export.Quality,
            ResolveTarget = LibraryTarget(library, track, format, export.Quality, payload.BatchId),
            Progress = report,
        }, ct);

        Register(outcome.Path!, DownloaderJobTypes.Download, addToHistory: true, null, track);
        Publish(EventTypes.FileProduced, new FileProducedEvent(PathMap.ToDb(outcome.Path!), DownloaderJobTypes.Download, null, context.Id));
        return Result(outcome, track, format, export.Quality);
    }
}

/// <summary>
/// Tâche « convert » : un fichier de la page Réencoder, réextrait au format des
/// Paramètres et remplacé sur place (nom « Titre - Artiste », pas de rangement).
/// Titre, artiste et album suivent la correspondance Deezer ; les autres tags du
/// fichier remplacé priment sur ceux du catalogue.
/// </summary>
public sealed class ConvertJobHandler(
    TrackExtractor extractor, DownloaderSettings settings, CatalogClient catalog, PathMap pathMap,
    OutboxQueue outbox, EventLog events, ILogger<ConvertJobHandler> logger)
    : ExtractionJobBase(extractor, settings, catalog, pathMap, outbox, events, logger), IJobHandler
{
    public async Task<JsonNode?> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var payload = context.GetPayload<ConvertJobPayload>();
        var source = payload.Item.LocalPath;
        if (!File.Exists(source))
            throw new PermanentJobException($"Fichier introuvable : {source}");
        var export = Settings.Export(payload.Options);
        var format = export.Format.ToLowerInvariant();
        var report = Reporter(context);

        var fileTags = AudioTags.Read(source, withProperties: false, withCover: true);
        var track = new TrackInfo { Title = fileTags.Title, Artist = fileTags.Artist, Album = fileTags.Album };
        foreach (var (field, value) in TrackMetadata.FileTags(fileTags)) track.ExtraTags[field] = value;

        DeezerTrack? match = null;
        if (payload.Item.DeezerId is null)
        {
            report(3, "Recherche Deezer...");
            match = await TrackMetadata.TryBestMatchAsync(Catalog, track.Artist, track.Title, null, Logger, ct);
        }
        await TrackMetadata.ApplyCatalogAsync(track, Catalog, match, payload.Item.DeezerId, Logger, ct, namesFromDetails: true);
        // Sans jaquette au catalogue, celle du fichier d'origine est conservée (l'original la perdait).
        if (track.Cover is null && fileTags.Cover is { } own) track.Cover = own.Data;

        var directory = Path.GetDirectoryName(Path.GetFullPath(source))!;
        var outcome = await ExtractAsync(context, new ExtractionRequest
        {
            Track = track,
            Format = format,
            Quality = export.Quality,
            OriginalPath = source,
            ResolveTarget = _ => Task.FromResult(new ExtractionTarget(directory, TrackMetadata.TargetStem(track.Tags(format)))),
            Progress = report,
        }, ct);

        Register(outcome.Path!, DownloaderJobTypes.Convert, addToHistory: false, source, track);
        Publish(EventTypes.FileProduced, new FileProducedEvent(PathMap.ToDb(outcome.Path!), DownloaderJobTypes.Convert, PathMap.ToDb(source), context.Id));
        return Result(outcome, track, format, export.Quality);
    }
}

/// <summary>
/// Tâche « redownload » : un titre abîmé de la page Qualité. Correspondance Deezer à
/// partir de ses tags, extraction rangée selon les règles « Organiser » (le titre peut
/// changer de dossier), remplacement du fichier abîmé ; la page Qualité réanalyse le
/// fichier en écoutant <c>file.replaced</c>.
/// </summary>
public sealed class RedownloadJobHandler(
    TrackExtractor extractor, DownloaderSettings settings, CatalogClient catalog, LibraryClient library, PathMap pathMap,
    OutboxQueue outbox, EventLog events, ILogger<RedownloadJobHandler> logger)
    : ExtractionJobBase(extractor, settings, catalog, pathMap, outbox, events, logger), IJobHandler
{
    public async Task<JsonNode?> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var payload = context.GetPayload<RedownloadJobPayload>();
        var item = payload.Item;
        if (string.IsNullOrWhiteSpace(item.Artist) || string.IsNullOrWhiteSpace(item.Title))
            throw new PermanentJobException("Tags insuffisants : ni artiste ni titre pour chercher le morceau.");
        var export = Settings.Export(payload.Options);
        var format = export.Format.ToLowerInvariant();
        var report = Reporter(context);
        var original = PathMap.ToLocal(item.DbPath);

        var track = new TrackInfo { Title = item.Title, Artist = item.Artist, Album = item.Album ?? "" };
        AudioTags? fileTags = null;
        if (File.Exists(original))
        {
            // Le jeu complet des tags du fichier abîmé prime sur le catalogue.
            fileTags = AudioTags.Read(original, withProperties: false, withCover: true);
            foreach (var (field, value) in TrackMetadata.FileTags(fileTags)) track.ExtraTags[field] = value;
        }

        report(3, "Recherche Deezer...");
        var match = await TrackMetadata.TryBestMatchAsync(Catalog, item.Artist, item.Title, item.Duration, Logger, ct);
        await TrackMetadata.ApplyCatalogAsync(track, Catalog, match, null, Logger, ct);
        if (track.Cover is null && fileTags?.Cover is { } own) track.Cover = own.Data;

        var outcome = await ExtractAsync(context, new ExtractionRequest
        {
            Track = track,
            Format = format,
            Quality = export.Quality,
            OriginalPath = original,
            ResolveTarget = LibraryTarget(library, track, format, export.Quality, payload.BatchId),
            Progress = report,
        }, ct);

        Register(outcome.Path!, DownloaderJobTypes.Redownload, addToHistory: false, original, track);
        Publish(EventTypes.FileReplaced, new FileProducedEvent(PathMap.ToDb(outcome.Path!), DownloaderJobTypes.Redownload, item.DbPath, context.Id));
        return Result(outcome, track, format, export.Quality);
    }
}
