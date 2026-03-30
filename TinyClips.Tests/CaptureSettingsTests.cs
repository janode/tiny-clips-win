using System.Text.Json;
using TinyClips.Models;
using Xunit;

namespace TinyClips.Tests;

/// <summary>
/// Tests for CaptureSettings: defaults, serialization round-trip,
/// per-type helper methods, and reset behavior.
/// </summary>
public class CaptureSettingsTests
{
    /// <summary>
    /// Create a fresh CaptureSettings instance via deserialization of default JSON.
    /// We can't use CaptureSettings.Instance (it's a singleton tied to disk),
    /// so we round-trip through JSON to get clean instances.
    /// </summary>
    private static CaptureSettings CreateFresh()
    {
        var json = JsonSerializer.Serialize(new CaptureSettings());
        return JsonSerializer.Deserialize<CaptureSettings>(json)!;
    }

    // MARK: - Defaults

    [Fact]
    public void Defaults_ScreenshotFormat_IsPng()
    {
        var s = CreateFresh();
        Assert.Equal(ImageFormat.Png, s.ScreenshotFormat);
    }

    [Fact]
    public void Defaults_VideoFrameRate_Is30()
    {
        var s = CreateFresh();
        Assert.Equal(30, s.VideoFrameRate);
    }

    [Fact]
    public void Defaults_GifFrameRate_Is10()
    {
        var s = CreateFresh();
        Assert.Equal(10.0, s.GifFrameRate);
    }

    [Fact]
    public void Defaults_GifMaxWidth_Is640()
    {
        var s = CreateFresh();
        Assert.Equal(640, s.GifMaxWidth);
    }

    [Fact]
    public void Defaults_JpegQuality_Is85()
    {
        var s = CreateFresh();
        Assert.Equal(85, s.JpegQuality);
    }

    [Fact]
    public void Defaults_ShowScreenshotEditor_IsTrue()
    {
        var s = CreateFresh();
        Assert.True(s.ShowScreenshotEditor);
    }

    [Fact]
    public void Defaults_ShowVideoTrimmer_IsTrue()
    {
        var s = CreateFresh();
        Assert.True(s.ShowVideoTrimmer);
    }

    [Fact]
    public void Defaults_ShowGifTrimmer_IsTrue()
    {
        var s = CreateFresh();
        Assert.True(s.ShowGifTrimmer);
    }

    [Fact]
    public void Defaults_CopyScreenshotToClipboard_IsTrue()
    {
        var s = CreateFresh();
        Assert.True(s.CopyScreenshotToClipboard);
    }

    [Fact]
    public void Defaults_CopyVideoToClipboard_IsFalse()
    {
        var s = CreateFresh();
        Assert.False(s.CopyVideoToClipboard);
    }

    [Fact]
    public void Defaults_OpenAfterCapture_IsTrue()
    {
        var s = CreateFresh();
        Assert.True(s.OpenAfterCapture);
    }

    // MARK: - Hotkey Defaults

    [Fact]
    public void Defaults_ScreenshotHotKey_IsCtrlAltShift5()
    {
        var s = CreateFresh();
        Assert.Equal(0x35, s.ScreenshotHotKeyVk); // VK_5
        Assert.Equal(0x0007, s.ScreenshotHotKeyMod); // CTRL|ALT|SHIFT
    }

    [Fact]
    public void Defaults_VideoHotKey_IsCtrlAltShift6()
    {
        var s = CreateFresh();
        Assert.Equal(0x36, s.VideoHotKeyVk);
        Assert.Equal(0x0007, s.VideoHotKeyMod);
    }

    [Fact]
    public void Defaults_GifHotKey_IsCtrlAltShift7()
    {
        var s = CreateFresh();
        Assert.Equal(0x37, s.GifHotKeyVk);
        Assert.Equal(0x0007, s.GifHotKeyMod);
    }

    // MARK: - ShouldCopyToClipboard

    [Fact]
    public void ShouldCopyToClipboard_Screenshot_RespectsFlag()
    {
        var s = CreateFresh();
        s.CopyScreenshotToClipboard = true;
        Assert.True(s.ShouldCopyToClipboard(CaptureType.Screenshot));

        s.CopyScreenshotToClipboard = false;
        Assert.False(s.ShouldCopyToClipboard(CaptureType.Screenshot));
    }

    [Fact]
    public void ShouldCopyToClipboard_Video_RespectsFlag()
    {
        var s = CreateFresh();
        s.CopyVideoToClipboard = true;
        Assert.True(s.ShouldCopyToClipboard(CaptureType.Video));

        s.CopyVideoToClipboard = false;
        Assert.False(s.ShouldCopyToClipboard(CaptureType.Video));
    }

    [Fact]
    public void ShouldCopyToClipboard_Gif_RespectsFlag()
    {
        var s = CreateFresh();
        s.CopyGifToClipboard = true;
        Assert.True(s.ShouldCopyToClipboard(CaptureType.Gif));

        s.CopyGifToClipboard = false;
        Assert.False(s.ShouldCopyToClipboard(CaptureType.Gif));
    }

    // MARK: - IsCountdownEnabled / CountdownDuration

    [Theory]
    [InlineData(CaptureType.Screenshot)]
    [InlineData(CaptureType.Video)]
    [InlineData(CaptureType.Gif)]
    public void CountdownDuration_DefaultIs3(CaptureType type)
    {
        var s = CreateFresh();
        Assert.Equal(3, s.CountdownDuration(type));
    }

    [Fact]
    public void IsCountdownEnabled_Screenshot_DefaultFalse()
    {
        var s = CreateFresh();
        Assert.False(s.IsCountdownEnabled(CaptureType.Screenshot));
    }

    [Fact]
    public void IsCountdownEnabled_Video_DefaultFalse()
    {
        var s = CreateFresh();
        Assert.False(s.IsCountdownEnabled(CaptureType.Video));
    }

    [Fact]
    public void IsCountdownEnabled_Gif_DefaultFalse()
    {
        var s = CreateFresh();
        Assert.False(s.IsCountdownEnabled(CaptureType.Gif));
    }

    // MARK: - JSON Round-Trip

    [Fact]
    public void JsonRoundTrip_PreservesAllSettings()
    {
        var original = CreateFresh();
        original.VideoFrameRate = 60;
        original.GifMaxWidth = 1280;
        original.JpegQuality = 50;
        original.FileNameTemplate = "My {type} {datetime}";
        original.ScreenshotHotKeyVk = 0x42;

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<CaptureSettings>(json)!;

        Assert.Equal(60, restored.VideoFrameRate);
        Assert.Equal(1280, restored.GifMaxWidth);
        Assert.Equal(50, restored.JpegQuality);
        Assert.Equal("My {type} {datetime}", restored.FileNameTemplate);
        Assert.Equal(0x42, restored.ScreenshotHotKeyVk);
    }

    [Fact]
    public void JsonDeserialization_MissingFields_UsesDefaults()
    {
        // Simulate a settings file from an older version with fewer fields
        var json = """{"VideoFrameRate": 24}""";
        var settings = JsonSerializer.Deserialize<CaptureSettings>(json)!;

        Assert.Equal(24, settings.VideoFrameRate);
        // All other fields should have defaults
        Assert.Equal(ImageFormat.Png, settings.ScreenshotFormat);
        Assert.Equal(85, settings.JpegQuality);
        Assert.Equal(640, settings.GifMaxWidth);
    }

    [Fact]
    public void JsonDeserialization_EmptyObject_AllDefaults()
    {
        var settings = JsonSerializer.Deserialize<CaptureSettings>("{}")!;
        Assert.Equal(30, settings.VideoFrameRate);
        Assert.Equal(ImageFormat.Png, settings.ScreenshotFormat);
        Assert.True(settings.ShowScreenshotEditor);
    }

    // MARK: - CaptureType Extensions

    [Fact]
    public void CaptureType_Label_ReturnsCorrectStrings()
    {
        Assert.Equal("Screenshot", CaptureType.Screenshot.Label());
        Assert.Equal("Video", CaptureType.Video.Label());
        Assert.Equal("GIF", CaptureType.Gif.Label());
    }

    [Fact]
    public void CaptureType_FileExtension_Video_IsMp4()
    {
        Assert.Equal("mp4", CaptureType.Video.FileExtension());
    }

    [Fact]
    public void CaptureType_FileExtension_Gif_IsGif()
    {
        Assert.Equal("gif", CaptureType.Gif.FileExtension());
    }
}
