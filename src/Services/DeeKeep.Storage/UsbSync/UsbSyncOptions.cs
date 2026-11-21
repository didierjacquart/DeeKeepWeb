namespace DeeKeep.Storage.UsbSync;

/// <summary>
/// Section <c>Sync</c> de la configuration : disque USB à tenir synchronisé avec la bibliothèque.
/// </summary>
public sealed class UsbSyncOptions
{
    /// <summary>Point de montage du disque USB (ex. <c>/mnt/deekeep-usb</c>) ; vide = synchronisation désactivée.</summary>
    public string? UsbRoot { get; set; }

    /// <summary>Racine de la bibliothèque (ex. <c>/mnt/deekeep-music</c>) ; vide = dossier de dépôt des Paramètres.</summary>
    public string? LibraryRoot { get; set; }

    /// <summary>Extensions synchronisées : la musique et les jaquettes des dossiers.</summary>
    public List<string> Extensions { get; set; } =
        [".mp3", ".flac", ".m4a", ".aac", ".opus", ".ogg", ".oga", ".wav", ".wma", ".aif", ".aiff", ".alac", ".ape", ".wv", ".jpg", ".jpeg", ".png"];

    /// <summary>Dossiers de premier niveau ignorés, en plus des dossiers cachés et système.</summary>
    public List<string> Exclude { get; set; } = ["000_Logiciel"];

    /// <summary>Heure du passage quotidien (heure locale, « HH:mm ») ; vide = pas de passage quotidien.</summary>
    public string? NightlyAt { get; set; } = "03:30";

    /// <summary>Lancer une synchronisation dès que le disque est branché.</summary>
    public bool OnPlug { get; set; } = true;

    /// <summary>Intervalle de détection du disque (secondes).</summary>
    public int PollSeconds { get; set; } = 30;
}
