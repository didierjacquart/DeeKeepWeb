using DeeKeep.Common.Text;
using Xunit;

namespace DeeKeep.Tests.Text;

/// <summary>Vecteurs produits en exécutant tag_lookup.py (voir TagTextVectors.cs).</summary>
public class TagTextTests
{
    [Fact]
    public void Titles_match_python()
    {
        Assert.True(TagTextVectors.Titles.Length >= 15);
        var failures = new List<string>();
        foreach (var v in TagTextVectors.Titles)
        {
            Check(failures, v.Input, "words", string.Join("|", v.Words), string.Join("|", TagText.Words(v.Input, WordMode.Python)));
            Check(failures, v.Input, "words corrigé", string.Join("|", v.WordsUnicode), string.Join("|", TagText.Words(v.Input)));
            Check(failures, v.Input, "clean_stem", v.CleanStem, TagText.CleanStem(v.Input));
            Check(failures, v.Input, "clean_title", v.CleanTitle, TagText.CleanTitle(v.Input));
            Check(failures, v.Input, "core_title", v.CoreTitle, TagText.CoreTitle(v.Input));
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Artists_match_python()
    {
        var failures = new List<string>();
        foreach (var v in TagTextVectors.Artists)
        {
            Check(failures, v.Input, "main_artist", v.MainArtist, TagText.MainArtist(v.Input));
            Check(failures, v.Input, "words", string.Join("|", v.Words), string.Join("|", TagText.Words(v.Input, WordMode.Python)));
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Contained_matches_python_and_corrected_variant()
    {
        var failures = new List<string>();
        foreach (var v in TagTextVectors.Contained)
        {
            Check(failures, v.Needle + " ⊂ " + v.Haystack, "python", v.Python.ToString("R"), TagText.Contained(v.Needle, v.Haystack, WordMode.Python).ToString("R"));
            Check(failures, v.Needle + " ⊂ " + v.Haystack, "corrigé", v.Unicode.ToString("R"), TagText.Contained(v.Needle, v.Haystack).ToString("R"));
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Query_helpers_match_python()
    {
        foreach (var v in TagTextVectors.Queries)
        {
            Assert.Equal(v.WithoutArtist, TagText.WithoutArtist(v.Stem, v.Artist, WordMode.Python));
            Assert.Equal(v.Lucene, TagText.Lucene(v.Stem));
        }
        foreach (var v in TagTextVectors.Folders) Assert.Equal(v.Generic, TagText.IsGenericFolder(v.Folder, WordMode.Python));
        Assert.Equal(TagTextVectors.UniqueOutput, TagText.Unique(TagTextVectors.UniqueInput));
        foreach (var v in TagTextVectors.Years) Assert.Equal(v.Year, TagText.Year(v.Input));
    }

    [Fact]
    public void Corrected_variants_fix_known_defects()
    {
        // Un titre n'est pas un nom de fichier : « Mr. Brightside » reste entier.
        Assert.Equal("Mr", TagText.CleanStem("Mr. Brightside"));
        Assert.Equal("Mr. Brightside", TagText.CleanTitle("Mr. Brightside"));
        // Un titre cyrillique était introuvable (aucun mot) : il est désormais reconnu.
        Assert.Equal(0.0, TagText.Contained("Мой путь", "Би-2 - Мой путь", WordMode.Python));
        Assert.Equal(1.0, TagText.Contained("Мой путь", "Би-2 - Мой путь"));
    }

    internal static void Check(List<string> failures, string input, string what, string expected, string actual)
    {
        if (expected != actual) failures.Add($"{what}({input}) : attendu « {expected} », obtenu « {actual} »");
    }
}
