namespace Eizo.Metadata.Recognition;

/// <summary>
/// Provider-neutral input for offline recognition.
/// </summary>
/// <param name="Path">A local or remote logical media path. No file-system access is implied.</param>
public sealed record RecognitionRequest(string Path);
