using DeeKeep.Common.Audio;
using Xunit;

namespace DeeKeep.Tests.Audio;

public class AudioExtensionsTests
{
    [Fact]
    public void Iter_audio_files_keeps_python_order_and_skips_system_folders()
    {
        var root = AudioTestSupport.TempDir();
        try
        {
            foreach (var rel in new[] { "b.mp3", "A.FLAC", "c.txt", "Zed/z1.ogg", "zed2/a.opus", "alpha/Beta/x.wav", "alpha/a.M4A",
                         "alpha/b.wma", "$RECYCLE.BIN/r.mp3", "System Volume Information/s.mp3", ".Trash-1000/t.mp3", ".hidden/h.mp3",
                         "Mixed/.dot.mp3", "Mixed/song.aac", "Mixed/song.mp3.part" })
            {
                var path = Path.Combine(root, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, []);
            }
            var found = AudioExtensions.IterAudioFiles(root).Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')).ToList();

            // Ordre produit par reencode_scan.iter_audio_files sur la même arborescence, dont on
            // retire les quatre dossiers système/cachés que l'original parcourait.
            Assert.Equal(["A.FLAC", "b.mp3", "alpha/a.M4A", "alpha/b.wma", "alpha/Beta/x.wav", "Mixed/.dot.mp3",
                "Mixed/song.aac", "Zed/z1.ogg", "zed2/a.opus"], found);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Extensions_and_content_types()
    {
        Assert.Equal([".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus", ".wma"], AudioExtensions.All);
        Assert.True(AudioExtensions.IsAudioFile(@"D:\x\Song.MP3"));
        Assert.False(AudioExtensions.IsAudioFile("song.mp3.part"));
        Assert.Equal("audio/flac", AudioExtensions.ContentType("a.FLAC"));
        Assert.Equal("application/octet-stream", AudioExtensions.ContentType("a.xyz"));
        Assert.True(AudioExtensions.IsExcludedFolder("$Recycle.Bin"));
        Assert.True(AudioExtensions.IsExcludedFolder("system volume information"));
        Assert.False(AudioExtensions.IsExcludedFolder("Daft Punk"));
    }
}
