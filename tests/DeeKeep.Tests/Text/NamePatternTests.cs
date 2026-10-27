using DeeKeep.Common.Text;
using Xunit;

namespace DeeKeep.Tests.Text;

/// <summary>Vecteurs produits en exécutant name_pattern.py (voir NamePatternVectors.cs).</summary>
public class NamePatternTests
{
    [Fact]
    public void Format_name_matches_python()
    {
        Assert.True(NamePatternVectors.Formats.Length >= 15);
        var failures = new List<string>();
        foreach (var v in NamePatternVectors.Formats)
            TagTextTests.Check(failures, $"{v.Pattern} #{v.Entry}", "format_name", v.Result,
                NamePattern.FormatName(v.Pattern, NamePatternVectors.Entries[v.Entry]));
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Pattern_helpers_match_python()
    {
        var untagged = NamePatternVectors.Entries[1];
        foreach (var v in NamePatternVectors.Patterns)
        {
            Assert.Equal(v.Modern, NamePattern.Modernize(v.Pattern));
            Assert.Equal(v.Unknown, NamePattern.UnknownTokens(v.Pattern));
            Assert.Equal(v.Missing, NamePattern.MissingRequiredTags(v.Pattern, untagged));
        }
    }

    [Fact]
    public void Tidy_matches_python()
    {
        foreach (var v in NamePatternVectors.Tidy) Assert.Equal(v.Tidy, NamePattern.Tidy(v.Input));
    }

    [Fact]
    public void Tables_match_python()
    {
        Assert.Equal(23, NamePattern.Tokens.Count);
        Assert.Equal(NamePatternVectors.TokenNames, NamePattern.Tokens);
        Assert.Equal(NamePatternVectors.Presets, NamePattern.Presets);
        Assert.Equal(NamePatternVectors.Categories.Length, NamePattern.TagCategories.Count);
        for (var i = 0; i < NamePatternVectors.Categories.Length; i++)
        {
            Assert.Equal(NamePatternVectors.Categories[i].Category, NamePattern.TagCategories[i].Name);
            Assert.Equal(NamePatternVectors.Categories[i].Items.Select(x => x.Label + "=" + x.Token),
                NamePattern.TagCategories[i].Items.Select(x => x.Label + "=" + x.Token));
        }
        Assert.Equal("%titre% - %artiste%", NamePattern.DefaultPattern);
    }
}
