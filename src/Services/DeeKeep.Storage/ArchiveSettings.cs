using System.Text.Json;
using DeeKeep.Common.Web;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Storage;
using Microsoft.Extensions.Options;

namespace DeeKeep.Storage;

/// <summary>Section <c>Storage</c> de la configuration : emplacement par défaut de l'archive.</summary>
public sealed class StorageOptions
{
    /// <summary>Dossier (ou fichier) de l'archive tant que le client n'en a pas enregistré d'autre.</summary>
    public string? ArchiveRoot { get; set; }

    public string Prefix { get; set; } = ArchiveRules.DefaultPrefix;
}

/// <summary>
/// Équivalent d'<c>archive.json</c> : <c>{root, prefix}</c>. La valeur enregistrée
/// depuis le client vit dans <c>deekeep_web.db</c> (clé <c>storage.archive</c>) et
/// l'emporte sur la configuration du service, qui ne donne que la valeur par défaut.
/// </summary>
public sealed class ArchiveSettings(WebDb webDb, IOptions<StorageOptions> options)
{
    private const string Key = "storage.archive";

    private sealed record Stored(string? Root, string? Prefix);

    public ArchiveConfig Get()
    {
        string? root = options.Value.ArchiveRoot;
        var prefix = options.Value.Prefix;
        var text = webDb.GetValue(Key);
        if (!string.IsNullOrEmpty(text))
        {
            try
            {
                var stored = JsonSerializer.Deserialize<Stored>(text, JsonDefaults.Options);
                if (stored is not null)
                {
                    // Une racine vidée depuis le client vaut « non configurée » : elle ne
                    // retombe pas sur la valeur par défaut.
                    root = stored.Root;
                    if (!string.IsNullOrEmpty(stored.Prefix)) prefix = stored.Prefix;
                }
            }
            catch (JsonException)
            {
                // Valeur illisible : la configuration du service s'applique (comme l'original
                // qui ignore un archive.json illisible).
            }
        }
        return Describe(root, prefix);
    }

    /// <summary><c>save_config</c> : racine rendue absolue, préfixe normalisé.</summary>
    public ArchiveConfig Save(string? root, string? prefix)
    {
        var trimmed = (root ?? "").Trim();
        var absolute = trimmed.Length > 0 ? Path.GetFullPath(trimmed) : "";
        var normalized = ArchiveRules.NormalizePrefix(string.IsNullOrWhiteSpace(prefix) ? ArchiveRules.DefaultPrefix : prefix);
        webDb.SetValue(Key, JsonSerializer.Serialize(new Stored(absolute, normalized), JsonDefaults.Options));
        return Get();
    }

    /// <summary>Le magasin configuré ; <see cref="ArchiveNotConfiguredException"/> sans emplacement.</summary>
    public ObjectStore Open() => Open(Get());

    /// <summary>Magasin d'un emplacement saisi mais pas forcément enregistré (Tester, Mesurer).</summary>
    public ObjectStore Open(string? root)
    {
        var current = Get();
        return string.IsNullOrWhiteSpace(root) ? Open(current) : Open(Describe(root, current.Prefix));
    }

    public static ObjectStore Open(ArchiveConfig config)
    {
        var file = config.ArchiveFile ?? throw new ArchiveNotConfiguredException();
        return new ObjectStore(file, ArchiveRules.NormalizePrefix(config.Prefix));
    }

    private static ArchiveConfig Describe(string? root, string? prefix)
    {
        var trimmed = (root ?? "").Trim();
        string? file = null;
        try
        {
            file = ArchiveRules.ResolveArchiveFile(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Chemin invalide : reste « non configuré », le test le dira.
        }
        var normalized = ArchiveRules.NormalizePrefix(string.IsNullOrWhiteSpace(prefix) ? ArchiveRules.DefaultPrefix : prefix);
        return new ArchiveConfig(trimmed.Length > 0 ? trimmed : null, normalized, file, file is not null);
    }
}
