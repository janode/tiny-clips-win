using TinyClips.Models;
using TinyClips.Services;
using Xunit;

namespace TinyClips.Tests;

/// <summary>
/// Tests for SaveService file naming: template tokens, sanitization,
/// duplicate handling, and edge cases.
/// </summary>
public class SaveServiceTests : IDisposable
{
    private readonly string _tempDir;

    public SaveServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"TinyClips_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // MARK: - Template Tokens

    [Fact]
    public void GenerateFileName_DefaultTemplate_FormatsCorrectly()
    {
        var fixedDate = new DateTime(2025, 3, 15, 14, 30, 45);
        var filename = SaveService.Instance.GenerateFileName(
            CaptureType.Screenshot, "png", fixedDate);

        Assert.Equal("TinyClips 2025-03-15 at 14.30.45.png", filename);
    }

    [Fact]
    public void GenerateFileName_TypeToken_SubstitutesCorrectly()
    {
        var settings = CaptureSettings.Instance;
        var origTemplate = settings.FileNameTemplate;
        try
        {
            settings.FileNameTemplate = "{type} capture";
            var fixedDate = new DateTime(2025, 1, 1, 0, 0, 0);

            Assert.Equal("Screenshot capture.png",
                SaveService.Instance.GenerateFileName(CaptureType.Screenshot, "png", fixedDate));
            Assert.Equal("Video capture.mp4",
                SaveService.Instance.GenerateFileName(CaptureType.Video, "mp4", fixedDate));
            Assert.Equal("GIF capture.gif",
                SaveService.Instance.GenerateFileName(CaptureType.Gif, "gif", fixedDate));
        }
        finally { settings.FileNameTemplate = origTemplate; }
    }

    [Fact]
    public void GenerateFileName_DateTimeToken_CombinesDateAndTime()
    {
        var settings = CaptureSettings.Instance;
        var origTemplate = settings.FileNameTemplate;
        try
        {
            settings.FileNameTemplate = "capture_{datetime}";
            var fixedDate = new DateTime(2025, 12, 31, 23, 59, 59);

            var filename = SaveService.Instance.GenerateFileName(
                CaptureType.Screenshot, "png", fixedDate);
            Assert.Equal("capture_2025-12-31_23.59.59.png", filename);
        }
        finally { settings.FileNameTemplate = origTemplate; }
    }

    [Fact]
    public void GenerateFileName_AppToken_SubstitutesAppName()
    {
        var settings = CaptureSettings.Instance;
        var origTemplate = settings.FileNameTemplate;
        try
        {
            settings.FileNameTemplate = "{app}_{type}";
            var fixedDate = new DateTime(2025, 6, 1, 12, 0, 0);

            var filename = SaveService.Instance.GenerateFileName(
                CaptureType.Video, "mp4", fixedDate);
            Assert.Equal("TinyClips_Video.mp4", filename);
        }
        finally { settings.FileNameTemplate = origTemplate; }
    }

    [Fact]
    public void GenerateFileName_EmptyTemplate_UsesFallback()
    {
        var settings = CaptureSettings.Instance;
        var origTemplate = settings.FileNameTemplate;
        try
        {
            settings.FileNameTemplate = "";
            var fixedDate = new DateTime(2025, 1, 15, 9, 5, 30);

            var filename = SaveService.Instance.GenerateFileName(
                CaptureType.Screenshot, "png", fixedDate);
            // Empty template falls back to default: "TinyClips {date} at {time}"
            Assert.Equal("TinyClips 2025-01-15 at 09.05.30.png", filename);
        }
        finally { settings.FileNameTemplate = origTemplate; }
    }

    // MARK: - Filename Sanitization

    [Fact]
    public void GenerateFileName_InvalidChars_AreReplacedWithDash()
    {
        var settings = CaptureSettings.Instance;
        var origTemplate = settings.FileNameTemplate;
        try
        {
            settings.FileNameTemplate = "my:file<name>test";
            var fixedDate = new DateTime(2025, 1, 1, 0, 0, 0);

            var filename = SaveService.Instance.GenerateFileName(
                CaptureType.Screenshot, "png", fixedDate);
            // : < > should become -
            Assert.Equal("my-file-name-test.png", filename);
            Assert.DoesNotContain(":", filename);
            Assert.DoesNotContain("<", filename);
            Assert.DoesNotContain(">", filename);
        }
        finally { settings.FileNameTemplate = origTemplate; }
    }

    [Fact]
    public void GenerateFileName_MultipleSpaces_Collapsed()
    {
        var settings = CaptureSettings.Instance;
        var origTemplate = settings.FileNameTemplate;
        try
        {
            settings.FileNameTemplate = "my   file   name";
            var fixedDate = new DateTime(2025, 1, 1, 0, 0, 0);

            var filename = SaveService.Instance.GenerateFileName(
                CaptureType.Screenshot, "png", fixedDate);
            Assert.Equal("my file name.png", filename);
        }
        finally { settings.FileNameTemplate = origTemplate; }
    }

    // MARK: - Extension Handling

    [Theory]
    [InlineData("png", ".png")]
    [InlineData(".png", ".png")]
    [InlineData("  jpg  ", ".jpg")]
    [InlineData("MP4", ".mp4")]
    public void GenerateFileName_NormalizesExtension(string inputExt, string expectedExt)
    {
        var fixedDate = new DateTime(2025, 1, 1, 0, 0, 0);
        var filename = SaveService.Instance.GenerateFileName(
            CaptureType.Screenshot, inputExt, fixedDate);
        Assert.EndsWith(expectedExt, filename);
    }

    // MARK: - Duplicate File Handling

    [Fact]
    public void GeneratePath_FirstFile_NoSuffix()
    {
        var settings = CaptureSettings.Instance;
        var origDir = settings.SaveDirectory;
        var origTemplate = settings.FileNameTemplate;
        try
        {
            settings.SaveDirectory = _tempDir;
            settings.FileNameTemplate = "test";

            var path = SaveService.Instance.GeneratePath(CaptureType.Screenshot, "png");
            Assert.Equal(Path.Combine(_tempDir, "test.png"), path);
        }
        finally
        {
            settings.SaveDirectory = origDir;
            settings.FileNameTemplate = origTemplate;
        }
    }

    [Fact]
    public void GeneratePath_DuplicateFile_AddsSuffix()
    {
        var settings = CaptureSettings.Instance;
        var origDir = settings.SaveDirectory;
        var origTemplate = settings.FileNameTemplate;
        try
        {
            settings.SaveDirectory = _tempDir;
            settings.FileNameTemplate = "test";

            // Create existing file
            File.WriteAllText(Path.Combine(_tempDir, "test.png"), "existing");

            var path = SaveService.Instance.GeneratePath(CaptureType.Screenshot, "png");
            Assert.Equal(Path.Combine(_tempDir, "test 2.png"), path);
        }
        finally
        {
            settings.SaveDirectory = origDir;
            settings.FileNameTemplate = origTemplate;
        }
    }

    [Fact]
    public void GeneratePath_MultipleDuplicates_IncrementsSuffix()
    {
        var settings = CaptureSettings.Instance;
        var origDir = settings.SaveDirectory;
        var origTemplate = settings.FileNameTemplate;
        try
        {
            settings.SaveDirectory = _tempDir;
            settings.FileNameTemplate = "test";

            File.WriteAllText(Path.Combine(_tempDir, "test.png"), "1");
            File.WriteAllText(Path.Combine(_tempDir, "test 2.png"), "2");
            File.WriteAllText(Path.Combine(_tempDir, "test 3.png"), "3");

            var path = SaveService.Instance.GeneratePath(CaptureType.Screenshot, "png");
            Assert.Equal(Path.Combine(_tempDir, "test 4.png"), path);
        }
        finally
        {
            settings.SaveDirectory = origDir;
            settings.FileNameTemplate = origTemplate;
        }
    }

    // MARK: - NamingPreview

    [Fact]
    public void NamingPreview_ReturnsNonEmptyString()
    {
        var preview = SaveService.Instance.NamingPreview();
        Assert.False(string.IsNullOrEmpty(preview));
        Assert.EndsWith(".png", preview); // Default format is PNG
    }
}
