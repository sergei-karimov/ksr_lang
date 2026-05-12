using OpenCvSharp;
using System.Runtime.InteropServices;

namespace KSR.Vision;

public sealed class VideoFrame : IDisposable
{
    private readonly Mat _mat;
    private byte[]? _rgbaCache;
    private bool _disposed;

    internal VideoFrame(Mat mat)
    {
        if (mat.Empty())
            throw new ArgumentException("Video frame cannot wrap an empty OpenCV Mat.", nameof(mat));

        _mat = mat;
        Width = mat.Width;
        Height = mat.Height;
    }

    public int Width { get; }
    public int Height { get; }

    internal Mat Mat
    {
        get
        {
            ThrowIfDisposed();
            return _mat;
        }
    }

    internal static VideoFrame FromMat(Mat mat)
    {
        if (mat.Empty())
            throw new InvalidOperationException("OpenCV returned an empty video frame.");

        return new VideoFrame(mat);
    }

    public VideoFrame Grayscale()
    {
        ThrowIfDisposed();

        if (_mat.Channels() == 1)
            return new VideoFrame(_mat.Clone());

        var gray = new Mat();
        Cv2.CvtColor(_mat, gray, ToGrayConversion(_mat.Channels()));
        return new VideoFrame(gray);
    }

    public VideoFrame Blur(int radius)
    {
        ThrowIfDisposed();
        if (radius < 1)
            return new VideoFrame(_mat.Clone());

        var kernel = radius % 2 == 0 ? radius + 1 : radius;
        var blurred = new Mat();
        Cv2.GaussianBlur(_mat, blurred, new Size(kernel, kernel), 0);
        return new VideoFrame(blurred);
    }

    public VideoFrame Edges()
    {
        ThrowIfDisposed();

        using var gray = new Mat();
        if (_mat.Channels() == 1)
            _mat.CopyTo(gray);
        else
            Cv2.CvtColor(_mat, gray, ToGrayConversion(_mat.Channels()));

        var edges = new Mat();
        Cv2.Canny(gray, edges, 100, 200);
        return new VideoFrame(edges);
    }

    public byte[] ToRgbaBytes()
    {
        ThrowIfDisposed();

        var expected = Width * Height * 4;
        if (_rgbaCache is { Length: var length } && length == expected)
            return _rgbaCache;

        using var rgba = new Mat();
        Cv2.CvtColor(_mat, rgba, ToRgbaConversion(_mat.Channels()));

        _rgbaCache = new byte[expected];
        using var continuous = rgba.IsContinuous() ? null : rgba.Clone();
        var source = continuous ?? rgba;
        var byteCount = source.Total() * source.ElemSize();
        if (byteCount != expected)
            throw new InvalidOperationException($"OpenCV produced an unexpected RGBA buffer size: {byteCount}, expected {expected}.");

        Marshal.Copy(source.Data, _rgbaCache, 0, expected);
        return _rgbaCache;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _mat.Dispose();
        _disposed = true;
    }

    private static ColorConversionCodes ToGrayConversion(int channels) => channels switch
    {
        3 => ColorConversionCodes.BGR2GRAY,
        4 => ColorConversionCodes.BGRA2GRAY,
        _ => throw new InvalidOperationException($"Unsupported frame channel count: {channels}."),
    };

    private static ColorConversionCodes ToRgbaConversion(int channels) => channels switch
    {
        1 => ColorConversionCodes.GRAY2RGBA,
        3 => ColorConversionCodes.BGR2RGBA,
        4 => ColorConversionCodes.BGRA2RGBA,
        _ => throw new InvalidOperationException($"Unsupported frame channel count: {channels}."),
    };

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VideoFrame));
    }
}
