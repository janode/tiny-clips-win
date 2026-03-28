using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using TinyClips.Models;

namespace TinyClips.Services;

/// <summary>
/// Windows toast notifications for save confirmations and errors.
/// </summary>
public sealed class NotificationService
{
    public static NotificationService Instance { get; } = new();

    private NotificationService()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
        }
        catch
        {
            // Notification registration may fail on some configurations
        }
    }

    public void ShowSaveNotification(string filePath, CaptureType type)
    {
        try
        {
            var fileName = Path.GetFileName(filePath);
            var builder = new AppNotificationBuilder()
                .AddText($"{type.Label()} saved")
                .AddText(fileName)
                .AddArgument("action", "openFile")
                .AddArgument("filePath", filePath);

            var notification = builder.BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch
        {
            // Non-fatal — notification display is best-effort
        }
    }

    public void ShowErrorNotification(string message)
    {
        try
        {
            var builder = new AppNotificationBuilder()
                .AddText("TinyClips Error")
                .AddText(message);

            var notification = builder.BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch
        {
            // Non-fatal
        }
    }

    private void OnNotificationInvoked(AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        if (args.Arguments.TryGetValue("action", out var action) &&
            action == "openFile" &&
            args.Arguments.TryGetValue("filePath", out var filePath))
        {
            if (File.Exists(filePath))
            {
                Helpers.NativeMethods.ShowInExplorer(filePath);
            }
        }
    }
}
