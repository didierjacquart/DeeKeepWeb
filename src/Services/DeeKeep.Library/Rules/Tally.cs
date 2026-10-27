using DeeKeep.Common.Audio;
using DeeKeep.Common.Text;

namespace DeeKeep.Library.Rules;

/// <summary>
/// Ce que la bibliothèque comptera par artiste, lot d'extraction compris — portage de
/// <c>library_rules.Tally</c>. Trois apports se cumulent : les titres du dossier de
/// l'artiste, ceux qui traînent à la racine ou dans « Divers » et que leur nom
/// « Titre - Artiste » lui attribue, et les titres réservés par le lot en cours (sans
/// eux, dix extractions parallèles d'un artiste nouveau se verraient chacune seule et
/// partiraient toutes dans « Divers »).
/// </summary>
/// <remarks>
/// Rien n'est ouvert : seuls les noms de dossiers et de fichiers sont lus, une fois par
/// artiste. Un même objet est partagé par toutes les extractions d'un lot (verrou).
/// </remarks>
public sealed class Tally(string root)
{
    private readonly object _lock = new();
    private Dictionary<string, string>? _folders;   // clé d'artiste -> dossier de premier niveau
    private Dictionary<string, int>? _loose;        // clé d'artiste -> titres hors dossier d'artiste
    private readonly Dictionary<string, int> _counted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _pending = new(StringComparer.Ordinal);

    public string Root { get; } = root ?? "";

    /// <summary>Artistes que la bibliothèque nomme déjà par un dossier (le <c>known</c> de <see cref="LibraryRules.MainArtist"/>).</summary>
    public IReadOnlySet<string> Known()
    {
        lock (_lock) return new HashSet<string>(ScanFolders().Keys, StringComparer.Ordinal);
    }

    /// <summary>Dossier que ce nom d'artiste doit rejoindre : l'écriture du dossier existant s'il y en a un.</summary>
    public string Folder(string? artist)
    {
        lock (_lock)
        {
            var folders = ScanFolders();
            var baseName = LibraryRules.MainArtist(PyText.Strip(artist ?? ""), KeySet(folders));
            return folders.GetValueOrDefault(LibraryRules.ArtistKey(baseName)) ?? LibraryRules.SafeComponent(baseName);
        }
    }

    /// <summary>Annonce <paramref name="count"/> titre(s) de cet artiste dans le lot, avant tout démarrage.</summary>
    public void Reserve(string? artist, int count = 1)
    {
        lock (_lock)
        {
            var key = Key(artist);
            if (key.Length > 0) _pending[key] = _pending.GetValueOrDefault(key) + count;
        }
    }

    /// <summary>Titres que l'artiste comptera une fois le lot écrit ; <paramref name="exclude"/> = fichier remplacé.</summary>
    public int Count(string? artist, string? exclude = null)
    {
        lock (_lock)
        {
            var key = Key(artist);
            if (key.Length == 0) return 0;
            var onDisk = Math.Max(0, OnDisk(key) - Excluded(key, exclude));
            // Le titre en cours n'est pas encore écrit : il compte pour un, et avec lui tout le lot réservé.
            return onDisk + Math.Max(_pending.GetValueOrDefault(key), 1);
        }
    }

    // ------------------------------------------------------------------ lecture du disque

    private static HashSet<string> KeySet(Dictionary<string, string> folders) => new(folders.Keys, StringComparer.Ordinal);

    private Dictionary<string, string> ScanFolders()
    {
        if (_folders is not null) return _folders;
        var folders = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var dir in new DirectoryInfo(Root).EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }))
            {
                if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                var key = LibraryRules.ArtistKey(dir.Name);
                // « Divers » ne nomme personne : c'est le fourre-tout.
                if (key.Length > 0 && key != PyText.CaseFold(LibraryRules.DiversFolder)) folders.TryAdd(key, dir.Name);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Racine absente ou illisible : aucun dossier d'artiste.
        }
        return _folders = folders;
    }

    private Dictionary<string, int> ScanLoose()
    {
        if (_loose is not null) return _loose;
        var loose = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var folder in new[] { Root, Path.Join(Root, LibraryRules.DiversFolder) })
        {
            try
            {
                foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }))
                {
                    if (!AudioExtensions.IsAudioFile(file.Name)) continue;
                    var key = Key(LibraryRules.StemArtist(file.Name));
                    if (key.Length > 0) loose[key] = loose.GetValueOrDefault(key) + 1;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Dossier absent : rien à compter.
            }
        }
        return _loose = loose;
    }

    /// <summary>Clé de regroupement d'un nom d'artiste, invités écartés.</summary>
    private string Key(string? artist) => LibraryRules.ArtistKey(LibraryRules.MainArtist(PyText.Strip(artist ?? ""), KeySet(ScanFolders())));

    private int OnDisk(string key)
    {
        if (_counted.TryGetValue(key, out var known)) return known;
        var total = ScanLoose().GetValueOrDefault(key);
        if (ScanFolders().TryGetValue(key, out var folder))
            total += AudioExtensions.IterAudioFiles(Path.Join(Root, folder)).Count();
        _counted[key] = total;
        return total;
    }

    /// <summary>1 si le fichier remplacé par l'extraction est déjà compté pour <paramref name="key"/>.</summary>
    private int Excluded(string key, string? exclude)
    {
        if (string.IsNullOrEmpty(exclude)) return 0;
        var path = Norm(exclude);
        if (ScanFolders().TryGetValue(key, out var folder))
        {
            var prefix = Norm(Path.Join(Root, folder)) + Path.DirectorySeparatorChar;
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return 1;
        }
        var parent = Path.GetDirectoryName(path) ?? "";
        if (SamePath(parent, Norm(Root)) || SamePath(parent, Norm(Path.Join(Root, LibraryRules.DiversFolder))))
            return Key(LibraryRules.StemArtist(exclude)) == key ? 1 : 0;
        return 0;
    }

    // normcase(abspath(path)) : chemin absolu comparable, casse ignorée comme partout ailleurs.
    private static string Norm(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
