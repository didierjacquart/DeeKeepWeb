using DeeKeep.Common.Text;
using Xunit;

namespace DeeKeep.Tests.Text;

/// <summary>Vecteurs produits en exécutant library_index.py (voir LibraryTextVectors.cs).</summary>
public class LibraryTextTests
{
    [Fact]
    public void Canonical_matches_python()
    {
        Assert.True(LibraryTextVectors.Canonical.Length >= 15);
        var failures = new List<string>();
        foreach (var v in LibraryTextVectors.Canonical)
            TagTextTests.Check(failures, v.Input, "canonical", v.Canonical, LibraryText.Canonical(v.Input));
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Title_keys_match_python()
    {
        var failures = new List<string>();
        foreach (var v in LibraryTextVectors.Titles)
        {
            TagTextTests.Check(failures, v.Title, "title_key", v.TitleKey, LibraryText.TitleKey(v.Title));
            TagTextTests.Check(failures, v.Title, "base_key", v.BaseKey, LibraryText.BaseKey(v.Title));
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Artist_keys_match_python()
    {
        var failures = new List<string>();
        foreach (var v in LibraryTextVectors.Artists)
        {
            TagTextTests.Check(failures, v.Artist, "artist_tokens", string.Join("|", v.Tokens),
                string.Join("|", LibraryText.ArtistTokens(v.Artist).Order(StringComparer.Ordinal)));
            TagTextTests.Check(failures, v.Artist, "artist_key", v.ArtistKey, LibraryText.ArtistKey(v.Artist));
            TagTextTests.Check(failures, v.Artist, "main_artist_key", v.MainKey, LibraryText.MainArtistKey(v.Artist));
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Path_keys_match_python()
    {
        var failures = new List<string>();
        foreach (var v in LibraryTextVectors.Paths)
        {
            TagTextTests.Check(failures, v.Path, "canonical_path", v.CanonicalPath, LibraryText.CanonicalPath(v.Path));
            TagTextTests.Check(failures, v.Path, "match_key", v.MatchKey, LibraryText.MatchKey(v.Path));
            TagTextTests.Check(failures, v.Path, "stem_key", v.StemKey, LibraryText.StemKey(v.Path));
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Ratio_and_overlap_match_python()
    {
        foreach (var v in LibraryTextVectors.Ratios) Assert.Equal(v.Ratio, LibraryText.Ratio(v.A, v.B));
        foreach (var v in LibraryTextVectors.Overlaps)
            Assert.Equal(v.Overlap, LibraryText.Overlap(v.A.ToHashSet(), v.B.ToHashSet()));
    }

    [Fact]
    public void Decomposed_accents_are_recognised()
    {
        // « Édition » en NFD : sans la normalisation NFC, _VARIANT_RE ne le reconnaîtrait pas.
        Assert.Equal("la boheme", LibraryText.BaseKey("La Bohème (Édition Deluxe)"));
    }
}
