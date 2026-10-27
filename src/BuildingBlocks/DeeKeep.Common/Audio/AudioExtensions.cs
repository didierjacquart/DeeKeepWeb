namespace DeeKeep.Common.Audio;

/// <summary>
/// Extensions audio reconnues et parcours d'un dossier — portage de
/// <c>reencode_scan.AUDIO_EXTENSIONS</c> et <c>iter_audio_files</c>.
/// </summary>
public static class AudioExtensions
{
    /// <summary>AUDIO_EXTENSIONS, dans l'ordre de l'original.</summary>
    public static readonly IReadOnlyList<string> All = [".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus", ".wma"];

    /// <summary>Types de contenu servis en HTTP (<c>CONTENT_TYPES</c> de library_mirror).</summary>
    public static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg", [".flac"] = "audio/flac", [".m4a"] = "audio/mp4", [".aac"] = "audio/aac",
        [".ogg"] = "audio/ogg", [".opus"] = "audio/ogg", [".wav"] = "audio/wav", [".wma"] = "audio/x-ms-wma",
        [".aiff"] = "audio/aiff", [".aif"] = "audio/aiff", [".alac"] = "audio/mp4",
    };

    /// <summary>Type de contenu d'un fichier, « application/octet-stream » à défaut.</summary>
    public static string ContentType(string path) =>
        ContentTypes.GetValueOrDefault(Path.GetExtension(path), "application/octet-stream");

    /// <summary>Vrai si le nom se termine par une extension audio (casse ignorée).</summary>
    public static bool IsAudioFile(string path)
    {
        var lower = path.ToLowerInvariant();
        return All.Any(ext => lower.EndsWith(ext, StringComparison.Ordinal));
    }

    /// <summary>
    /// Dossier à ne pas parcourir : corbeilles et dossiers système (« $RECYCLE.BIN »,
    /// « System Volume Information », « .Trash-1000 ») et dossiers cachés « .xxx ».
    /// L'original de l'écran Organiser les parcourait : la base contient des titres
    /// « d:\$recycle.bin\$rcqdpmn.mp3 ».
    /// </summary>
    public static bool IsExcludedFolder(string name) =>
        name.StartsWith('$')
        || name.StartsWith('.')
        || name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <c>iter_audio_files</c> : fichiers audio sous <paramref name="root"/>, récursivement,
    /// dans l'ordre de l'original — les fichiers d'un dossier (triés sans casse) puis ses
    /// sous-dossiers un à un (triés sans casse), en profondeur d'abord.
    /// Les dossiers illisibles sont ignorés en silence. Écarts voulus : dossiers système et
    /// cachés exclus (<see cref="IsExcludedFolder"/>), liens symboliques et jonctions de
    /// dossiers non suivis (une boucle de liens ne peut plus bloquer l'analyse).
    /// </summary>
    public static IEnumerable<string> IterAudioFiles(string root, CancellationToken ct = default)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = 0,          // fichiers cachés compris, comme os.scandir
            ReturnSpecialDirectories = false,
        };
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = stack.Pop();
            var dirs = new List<string>();
            var files = new List<string>();
            try
            {
                foreach (var entry in new DirectoryInfo(current).EnumerateFileSystemInfos("*", options))
                {
                    if (entry is DirectoryInfo dir)
                    {
                        if (!dir.Attributes.HasFlag(FileAttributes.ReparsePoint) && !IsExcludedFolder(dir.Name)) dirs.Add(Path.Join(current, dir.Name));
                    }
                    else if (IsAudioFile(entry.Name))
                        files.Add(Path.Join(current, entry.Name));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                continue;
            }
            // sorted(key=str.lower) puis sorted(key=str.lower, reverse=True) : tris stables,
            // comme OrderBy/OrderByDescending.
            foreach (var file in files.OrderBy(f => f.ToLowerInvariant(), StringComparer.Ordinal)) yield return file;
            foreach (var dir in dirs.OrderByDescending(d => d.ToLowerInvariant(), StringComparer.Ordinal)) stack.Push(dir);
        }
    }
}
