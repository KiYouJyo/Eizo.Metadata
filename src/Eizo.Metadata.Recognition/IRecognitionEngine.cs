namespace Eizo.Metadata.Recognition;

/// <summary>
/// Deterministic, synchronous and network-free media recognition contract.
/// </summary>
public interface IRecognitionEngine
{
    RecognitionResult Recognize(RecognitionRequest request);
}
