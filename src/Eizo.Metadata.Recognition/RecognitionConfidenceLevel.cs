namespace Eizo.Metadata.Recognition;

/// <summary>
/// Coarse confidence band derived from the calibrated recognition score.
/// </summary>
public enum RecognitionConfidenceLevel
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}
