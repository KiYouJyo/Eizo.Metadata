namespace Eizo.Metadata.Recognition;

/// <summary>
/// One explainable signal that contributed to a recognition result.
/// </summary>
/// <param name="Code">Stable machine-readable evidence code.</param>
/// <param name="Value">Human-readable value extracted from the input, when applicable.</param>
/// <param name="Weight">Relative contribution in the range -1.0 to 1.0.</param>
public sealed record RecognitionEvidence(
    string Code,
    string? Value,
    double Weight);
