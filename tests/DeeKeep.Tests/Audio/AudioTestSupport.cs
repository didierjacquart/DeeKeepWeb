using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace DeeKeep.Tests.Audio;

/// <summary>Outils partagés des tests audio : ressources, ffmpeg, dossiers temporaires.</summary>
internal static class AudioTestSupport
{
    /// <summary>Dossier des ressources de test (Audio/Assets), retrouvé depuis les sources.</summary>
    public static string Assets([CallerFilePath] string source = "") => Path.Combine(Path.GetDirectoryName(source)!, "Assets");

    /// <summary>Petit MP3 réel de la bibliothèque (jamais modifié : toujours copié avant usage).</summary>
    public static string SampleMp3 => Environment.GetEnvironmentVariable("DEEKEEP_TEST_MP3") ?? @"D:\Queen\Yeah - Queen.mp3";

    /// <summary>Base de l'application desktop, lue seule.</summary>
    public static string Database => Environment.GetEnvironmentVariable("DEEKEEP_TEST_DB")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeeKeep", "deekeep_cache.db");

    /// <summary>ffmpeg/ffprobe : variable DEEKEEP_FFMPEG_DIR, sinon celui livré avec l'application desktop, sinon le PATH.</summary>
    public static string? Tool(string name)
    {
        var exe = OperatingSystem.IsWindows() ? name + ".exe" : name;
        foreach (var dir in new[] { Environment.GetEnvironmentVariable("DEEKEEP_FFMPEG_DIR"), @"D:\000_Logiciel\DeezerMusic\ffmpeg" })
            if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, exe))) return Path.Combine(dir, exe);
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (dir.Length > 0 && File.Exists(Path.Combine(dir, exe))) return Path.Combine(dir, exe);
        return null;
    }

    public static (int Code, string Out, string Err) Run(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var outTask = p.StandardOutput.ReadToEndAsync();
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, outTask.Result, err);
    }

    public static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "deekeep-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
