using DeeKeep.Common.Data;
using Microsoft.Extensions.Options;

namespace DeeKeep.Catalog;

/// <summary>Section <c>Catalog</c> de la configuration.</summary>
public sealed class CatalogOptions
{
    /// <summary>Dossier des caches disque (images). Vide : <c>catalog-cache</c> à côté de la base.</summary>
    public string CacheDir { get; set; } = "";

    /// <summary>Taille maximale du cache d'images, en Mo (les plus anciennement servies partent d'abord).</summary>
    public int ImageCacheMaxMb { get; set; } = 256;

    /// <summary>Intervalle minimal entre deux appels à l'API Deezer (200 ms = 5 requêtes par seconde).</summary>
    public int DeezerIntervalMs { get; set; } = 200;

    /// <summary>Intervalle minimal entre deux appels à MusicBrainz (leur règle : une requête par seconde au plus).</summary>
    public int MusicBrainzIntervalMs { get; set; } = 1100;

    /// <summary>Intervalle minimal entre deux appels à AcoustID (le service tolère trois requêtes par seconde).</summary>
    public int AcoustIdIntervalMs { get; set; } = 340;

    /// <summary>Plafond de pistes lues pour une playlist ou un album (pagination Deezer suivie jusque-là).</summary>
    public int MaxTracks { get; set; } = 2000;
}

/// <summary>Section <c>Import</c> : données de l'application desktop à reprendre.</summary>
public sealed class ImportOptions
{
    /// <summary>Dossier de données de l'application desktop (<c>%APPDATA%\DeeKeep</c>).</summary>
    public string DesktopDataDir { get; set; } = "";

    public string ResolveDesktopDataDir() =>
        string.IsNullOrWhiteSpace(DesktopDataDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeeKeep")
            : DesktopDataDir;
}

/// <summary>Emplacements résolus des caches du service.</summary>
public sealed class CatalogPaths(IOptions<CatalogOptions> options, IOptions<DatabaseOptions> database)
{
    public string CacheDir { get; } = string.IsNullOrWhiteSpace(options.Value.CacheDir)
        ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(database.Value.MainPath)) ?? ".", "catalog-cache")
        : options.Value.CacheDir;

    public string ImageDir => Path.Combine(CacheDir, "img");
}
