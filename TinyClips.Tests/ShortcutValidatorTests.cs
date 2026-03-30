using TinyClips.Helpers;
using Xunit;

namespace TinyClips.Tests;

/// <summary>
/// Tests for ShortcutValidator — keyboard shortcut conflict detection.
/// </summary>
public class ShortcutValidatorTests
{
    // MARK: - FindConflict

    [Fact]
    public void FindConflict_NoConflicts_ReturnsNull()
    {
        var shortcuts = new[]
        {
            new ShortcutValidator.Shortcut("Screenshot", 0x07, 0x35),
            new ShortcutValidator.Shortcut("Video", 0x07, 0x36),
            new ShortcutValidator.Shortcut("GIF", 0x07, 0x37),
        };

        Assert.Null(ShortcutValidator.FindConflict(shortcuts, 0));
        Assert.Null(ShortcutValidator.FindConflict(shortcuts, 1));
        Assert.Null(ShortcutValidator.FindConflict(shortcuts, 2));
    }

    [Fact]
    public void FindConflict_TwoMatch_ReturnsConflictName()
    {
        var shortcuts = new[]
        {
            new ShortcutValidator.Shortcut("Screenshot", 0x07, 0x35),
            new ShortcutValidator.Shortcut("Video", 0x07, 0x35), // same as Screenshot
            new ShortcutValidator.Shortcut("GIF", 0x07, 0x37),
        };

        Assert.Equal("Video", ShortcutValidator.FindConflict(shortcuts, 0));
        Assert.Equal("Screenshot", ShortcutValidator.FindConflict(shortcuts, 1));
        Assert.Null(ShortcutValidator.FindConflict(shortcuts, 2));
    }

    [Fact]
    public void FindConflict_SameVkDifferentMod_NoConflict()
    {
        var shortcuts = new[]
        {
            new ShortcutValidator.Shortcut("Screenshot", 0x07, 0x35),
            new ShortcutValidator.Shortcut("Video", 0x03, 0x35), // same Vk, different Mod
        };

        Assert.Null(ShortcutValidator.FindConflict(shortcuts, 0));
        Assert.Null(ShortcutValidator.FindConflict(shortcuts, 1));
    }

    [Fact]
    public void FindConflict_SameModDifferentVk_NoConflict()
    {
        var shortcuts = new[]
        {
            new ShortcutValidator.Shortcut("Screenshot", 0x07, 0x35),
            new ShortcutValidator.Shortcut("Video", 0x07, 0x36), // same Mod, different Vk
        };

        Assert.Null(ShortcutValidator.FindConflict(shortcuts, 0));
        Assert.Null(ShortcutValidator.FindConflict(shortcuts, 1));
    }

    [Fact]
    public void FindConflict_ReturnsFirstConflict()
    {
        // When multiple conflicts exist, returns the first one found
        var shortcuts = new[]
        {
            new ShortcutValidator.Shortcut("Screenshot", 0x07, 0x35),
            new ShortcutValidator.Shortcut("Video", 0x07, 0x35),
            new ShortcutValidator.Shortcut("GIF", 0x07, 0x35),
        };

        // Index 0 conflicts with both 1 and 2, should return "Video" (first found)
        Assert.Equal("Video", ShortcutValidator.FindConflict(shortcuts, 0));
    }

    // MARK: - CheckAllConflicts

    [Fact]
    public void CheckAllConflicts_AllUnique_AllNull()
    {
        var shortcuts = new[]
        {
            new ShortcutValidator.Shortcut("Screenshot", 0x07, 0x35),
            new ShortcutValidator.Shortcut("Video", 0x07, 0x36),
            new ShortcutValidator.Shortcut("GIF", 0x07, 0x37),
        };

        var results = ShortcutValidator.CheckAllConflicts(shortcuts);

        Assert.Equal(3, results.Length);
        Assert.All(results, r => Assert.Null(r));
    }

    [Fact]
    public void CheckAllConflicts_TwoConflicting_ReturnsMessages()
    {
        var shortcuts = new[]
        {
            new ShortcutValidator.Shortcut("Screenshot", 0x07, 0x35),
            new ShortcutValidator.Shortcut("Video", 0x07, 0x35),
            new ShortcutValidator.Shortcut("GIF", 0x07, 0x37),
        };

        var results = ShortcutValidator.CheckAllConflicts(shortcuts);

        Assert.Equal("Conflicts with Video", results[0]);
        Assert.Equal("Conflicts with Screenshot", results[1]);
        Assert.Null(results[2]);
    }

    [Fact]
    public void CheckAllConflicts_AllThreeConflicting_AllHaveMessages()
    {
        var shortcuts = new[]
        {
            new ShortcutValidator.Shortcut("Screenshot", 0x07, 0x35),
            new ShortcutValidator.Shortcut("Video", 0x07, 0x35),
            new ShortcutValidator.Shortcut("GIF", 0x07, 0x35),
        };

        var results = ShortcutValidator.CheckAllConflicts(shortcuts);

        Assert.NotNull(results[0]);
        Assert.NotNull(results[1]);
        Assert.NotNull(results[2]);
        Assert.Contains("Conflicts with", results[0]!);
        Assert.Contains("Conflicts with", results[1]!);
        Assert.Contains("Conflicts with", results[2]!);
    }

    [Fact]
    public void CheckAllConflicts_Empty_ReturnsEmptyArray()
    {
        var results = ShortcutValidator.CheckAllConflicts([]);
        Assert.Empty(results);
    }
}
