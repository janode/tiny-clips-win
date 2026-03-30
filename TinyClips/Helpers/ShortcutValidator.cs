namespace TinyClips.Helpers;

/// <summary>
/// Pure logic for detecting conflicting keyboard shortcuts, extracted from SettingsWindow.
/// </summary>
internal static class ShortcutValidator
{
    /// <summary>
    /// Represents a named shortcut with modifier and virtual key code.
    /// </summary>
    public readonly record struct Shortcut(string Name, int Mod, int Vk);

    /// <summary>
    /// Finds a conflicting shortcut for the given index in the list.
    /// Returns the name of the conflicting shortcut, or null if no conflict.
    /// </summary>
    public static string? FindConflict(Shortcut[] shortcuts, int index)
    {
        for (int j = 0; j < shortcuts.Length; j++)
        {
            if (j != index &&
                shortcuts[index].Mod == shortcuts[j].Mod &&
                shortcuts[index].Vk == shortcuts[j].Vk)
            {
                return shortcuts[j].Name;
            }
        }
        return null;
    }

    /// <summary>
    /// Gets the conflict message for each shortcut in the array.
    /// Returns an array of the same length, with null for no conflict
    /// or a message like "Conflicts with Screenshot" for conflicts.
    /// </summary>
    public static string?[] CheckAllConflicts(Shortcut[] shortcuts)
    {
        var results = new string?[shortcuts.Length];
        for (int i = 0; i < shortcuts.Length; i++)
        {
            var conflictName = FindConflict(shortcuts, i);
            results[i] = conflictName != null ? $"Conflicts with {conflictName}" : null;
        }
        return results;
    }
}
