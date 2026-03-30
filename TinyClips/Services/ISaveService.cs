using TinyClips.Models;

namespace TinyClips.Services;

/// <summary>
/// Interface for file naming, path generation, and post-save actions.
/// </summary>
public interface ISaveService
{
    string GeneratePath(CaptureType type);
    string GeneratePath(CaptureType type, string fileExtension);
    string GenerateFileName(CaptureType type, string fileExtension, DateTime? date = null);
    string NamingPreview(CaptureType type = CaptureType.Screenshot);
    void HandleSavedFile(string filePath, CaptureType type);
    void ShowError(string message);
}
