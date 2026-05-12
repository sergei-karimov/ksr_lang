using OpenCvSharp;

namespace KSR.Vision;

public sealed class Camera : IDisposable
{
    private readonly VideoCapture _capture;
    private bool _disposed;

    private Camera(VideoCapture capture, int index)
    {
        _capture = capture;
        Index = index;
    }

    public int Index { get; }

    public static Camera Open(int index)
    {
        var capture = new VideoCapture(index);
        if (!capture.IsOpened())
        {
            capture.Dispose();
            throw new InvalidOperationException(
                $"Could not open camera index {index}. Check that the camera exists and OS permissions allow access.");
        }

        return new Camera(capture, index);
    }

    public VideoFrame Read()
    {
        ThrowIfDisposed();

        var mat = new Mat();
        try
        {
            if (!_capture.Read(mat) || mat.Empty())
                throw new InvalidOperationException($"Camera index {Index} did not return a frame.");

            return VideoFrame.FromMat(mat);
        }
        catch
        {
            mat.Dispose();
            throw;
        }
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _capture.Release();
        _capture.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Camera), $"Camera index {Index} is already closed.");
    }
}
