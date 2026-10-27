using DeeKeep.Common.Text;
using Xunit;

namespace DeeKeep.Tests.Text;

/// <summary>Vecteurs produits en exécutant utils.py et library_rules.py (voir FileNamesVectors.cs).</summary>
public class FileNamesTests
{
    [Fact]
    public void Sanitize_and_safe_component_match_python()
    {
        Assert.True(FileNamesVectors.Names.Length >= 15);
        var failures = new List<string>();
        foreach (var v in FileNamesVectors.Names)
        {
            TagTextTests.Check(failures, v.Name, "sanitize_filename", v.Sanitized, FileNames.SanitizeFilename(v.Name));
            TagTextTests.Check(failures, v.Name, "safe_component", v.Safe, FileNames.SafeComponent(v.Name));
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Format_duration_matches_python()
    {
        foreach (var v in FileNamesVectors.DurationsNumber) Assert.Equal(v.Formatted, FileNames.FormatDuration(v.Seconds));
        foreach (var v in FileNamesVectors.DurationsText) Assert.Equal(v.Formatted, FileNames.FormatDuration(v.Text));
        Assert.Equal("02:05", FileNames.FormatDuration(125L));
        Assert.Equal("--:--", FileNames.FormatDuration((double?)null));
    }
}
