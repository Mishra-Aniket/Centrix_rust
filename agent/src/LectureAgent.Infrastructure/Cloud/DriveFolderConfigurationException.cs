namespace LectureAgent.Infrastructure.Cloud;

/// <summary>
/// Thrown when the upload cannot proceed because of a Drive configuration problem
/// (missing batch folder, ambiguous duplicate folder names, missing credentials).
/// Retrying cannot fix these — the user must fix the folder or the assignment — so
/// the upload worker marks the entry permanently failed instead of burning retries.
/// </summary>
public sealed class DriveFolderConfigurationException : InvalidOperationException
{
    public DriveFolderConfigurationException(string message) : base(message) { }
}
