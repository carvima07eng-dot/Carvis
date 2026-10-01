namespace Carvis.Core.Vision;

// Phase 5: screenshot with a hotkey and analysis with a vision model (qwen2.5vl).

public interface IScreenCaptureService
{
    Task<byte[]> CapturePngAsync(CancellationToken cancellationToken = default);
}

public interface IVisionService
{
    Task<string> DescribeAsync(byte[] png, string question, CancellationToken cancellationToken = default);
}
