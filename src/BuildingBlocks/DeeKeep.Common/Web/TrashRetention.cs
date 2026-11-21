using System.Globalization;

namespace DeeKeep.Common.Web;

/// <summary>
/// Durée de conservation de la corbeille (jours), réglable dans les Paramètres et partagée par
/// la bibliothèque et la synchronisation du disque USB. 0 = jamais d'effacement automatique.
/// Réglage propre à la version web : tenu dans <c>deekeep_web.db</c>, pas dans <c>app_settings</c>.
/// </summary>
public static class TrashRetention
{
    public const string Key = "trash_retention_days";

    public const int Default = 30;

    public const int Max = 3650;

    public static int Get(WebDb db)
    {
        try
        {
            return int.TryParse(db.GetValue(Key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) && days >= 0
                ? Math.Min(days, Max)
                : Default;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Base web illisible : la valeur par défaut, plutôt que d'effacer trop tôt ou de planter.
            return Default;
        }
    }

    public static int Set(WebDb db, int days)
    {
        if (days is < 0 or > Max) throw new ArgumentException($"Durée de conservation entre 0 (jamais effacée) et {Max} jours.");
        db.SetValue(Key, days.ToString(CultureInfo.InvariantCulture));
        return days;
    }
}
