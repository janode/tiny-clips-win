using TinyClips.Models;

namespace TinyClips.Services;

/// <summary>
/// Interface for Windows toast notifications.
/// </summary>
public interface INotificationService
{
    void ShowSaveNotification(string filePath, CaptureType type);
    void ShowErrorNotification(string message);
    void ShowUpdateNotification(string version, string url);
}
