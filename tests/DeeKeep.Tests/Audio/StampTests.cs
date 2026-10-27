using DeeKeep.Common.Audio;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DeeKeep.Tests.Audio;

public class StampTests
{
    // (st_mtime_ns, empreinte quality, empreinte archive) relevés par Python (os.utime puis
    // quality_scan._stamp / library_mirror.stamp) sur un fichier de 10 octets.
    public static readonly TheoryData<long, string, string> Vectors = new()
    {
        { 1784576096000000000, "1784576096000000000:10", "1784576096:10" },
        { 1784576095999999900, "1784576095999999900:10", "1784576096:10" },   // l'arrondi flottant de st_mtime fait basculer la seconde
        { 1787319272123456700, "1787319272123456700:10", "1787319272:10" },
        { 1000000000, "1000000000:10", "1:10" },
        { 1784399730500000000, "1784399730500000000:10", "1784399730:10" },
    };

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Stamps_match_python(long mtimeNs, string quality, string archive)
    {
        Assert.Equal(quality, Stamp.Quality(mtimeNs, 10));
        Assert.Equal(archive, Stamp.Archive(mtimeNs, 10));

        var dir = AudioTestSupport.TempDir();
        try
        {
            var file = Path.Combine(dir, "f.bin");
            File.WriteAllBytes(file, "0123456789"u8.ToArray());
            File.SetLastWriteTimeUtc(file, DateTime.UnixEpoch.AddTicks(mtimeNs / 100));
            Assert.Equal(quality, Stamp.Quality(file));
            Assert.Equal(archive, Stamp.Archive(file));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Missing_file()
    {
        Assert.Null(Stamp.Quality(@"Z:\nulle\part.mp3"));
        Assert.Equal("", Stamp.Archive(@"Z:\nulle\part.mp3"));
    }

    /// <summary>
    /// Les empreintes « archive » de la vraie base coïncident pour les fichiers inchangés depuis.
    /// Les mesures « quality » datent d'avant une modification de tous les fichiers (dates
    /// 1784… en base, 1787… sur disque) : seul leur format est vérifié.
    /// </summary>
    [Fact]
    public void Matches_existing_database_rows()
    {
        if (!File.Exists(AudioTestSupport.Database)) Assert.Skip("base absente");
        using var db = new SqliteConnection($"Data Source={AudioTestSupport.Database};Mode=ReadOnly");
        db.Open();
        using (var quality = db.CreateCommand())
        {
            quality.CommandText = "SELECT path, stamp FROM quality WHERE deleted = 0 LIMIT 500";
            using var rows = quality.ExecuteReader();
            while (rows.Read())
            {
                Assert.Matches(@"^\d{19}:\d+$", rows.GetString(1));
                if (File.Exists(rows.GetString(0))) Assert.Matches(@"^\d{19}:\d+$", Stamp.Quality(rows.GetString(0))!);
            }
        }
        foreach (var (table, compute) in new (string, Func<string, string?>)[] { ("archive", Stamp.Archive) })
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"SELECT path, stamp FROM {table} WHERE deleted = 0";
            using var reader = cmd.ExecuteReader();
            int present = 0, same = 0;
            while (reader.Read())
            {
                var path = reader.GetString(0);
                if (!File.Exists(path)) continue;
                present++;
                if (compute(path) == reader.GetString(1)) same++;
            }
            // Les fichiers modifiés depuis la mesure diffèrent légitimement ; aucun format ne doit diverger partout.
            if (present > 0) Assert.True(same > 0, $"{table} : aucune empreinte identique sur {present} fichiers présents");
        }
    }
}
