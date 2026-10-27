using DeeKeep.Common.Data;
using Dapper;
using Xunit;

namespace DeeKeep.Tests;

public class RowHashTests
{
    [Theory]
    [InlineData(0.1, "0.1")]
    [InlineData(1e-05, "1e-05")]
    [InlineData(0.0001, "0.0001")]
    [InlineData(1.5e16, "1.5e+16")]
    [InlineData(1234567.125, "1234567.125")]
    [InlineData(241.512, "241.512")]
    [InlineData(-3.25, "-3.25")]
    public void Repr_matches_python(double value, string expected) => Assert.Equal(expected, PythonFloat.Repr(value));

    [Theory]
    [InlineData(2.675, 2.675)]          // round(2.675, 6)
    [InlineData(1788083105.7059052, 1788083105.705905)]
    [InlineData(0.0000005, 0.0)]        // demi pair : 5e-7 binaire est sous 0,0000005
    public void Round_matches_python(double value, double expected) => Assert.Equal(expected, PythonFloat.Round(value, 6));

    [Fact]
    public void Blake2b_empty_vector()
    {
        // Valeurs de référence : hashlib.blake2b de Python.
        Assert.Equal("786a02f742015903c6c6fd852552d272912f4740e15847618a86e217f71f5419d25e1031afee585313896444934eb04b903a685b1448b755d56f701afe9be2ce", Convert.ToHexStringLower(new Blake2b(64).Final()));
        var b = new Blake2b(8);
        b.Update("abc"u8);
        Assert.Equal("d8bb14d833d59559", Convert.ToHexStringLower(b.Final()));
    }

    /// <summary>Recalcule l'empreinte de toutes les lignes de la vraie base, si elle est présente.</summary>
    [Fact]
    public void Matches_existing_database()
    {
        var path = Environment.GetEnvironmentVariable("DEEKEEP_TEST_DB")
                   ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeeKeep", "deekeep_cache.db");
        if (!File.Exists(path)) Assert.Skip("Base absente");
        using var conn = DeeKeepDb.OpenPath(path, readOnly: true, 5000);
        var failures = new List<string>();
        var total = 0;
        foreach (var spec in Schema.TableOrder)
        {
            foreach (var row in conn.QueryRows($"SELECT * FROM {spec.Name}"))
            {
                total++;
                var expected = Convert.ToInt64(row["h"]);
                var actual = RowHash.Compute(spec, row);
                // Les lignes d'archive ont été hachées quand la table s'appelait encore « opus_mirror ».
                if (spec.Name == "archive" && expected != actual) actual = RowHash.Compute(spec with { Name = "opus_mirror" }, row);
                if (expected != actual && failures.Count < 20) failures.Add($"{spec.Name} {row[spec.Pk]}: {expected} != {actual}");
            }
        }
        Assert.True(total > 0);
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
