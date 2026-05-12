using KSR.Vision;
using Raylib_cs;

namespace KSR.Creative;

public sealed unsafe class DrawContext : IDisposable
{
    private Texture2D? _texture;
    private int _textureWidth;
    private int _textureHeight;
    private bool _disposed;

    public void Clear(Raylib_cs.Color color)
    {
        ThrowIfDisposed();
        Raylib.ClearBackground(color);
    }

    public void Image(VideoFrame frame, int x, int y)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(frame);

        EnsureTexture(frame.Width, frame.Height);

        var rgba = frame.ToRgbaBytes();
        fixed (byte* ptr = rgba)
        {
            Raylib.UpdateTexture(_texture!.Value, ptr);
        }

        Raylib.DrawTexture(_texture.Value, x, y, Raylib_cs.Color.White);
    }

    public void Fps(int x, int y)
    {
        ThrowIfDisposed();
        Raylib.DrawFPS(x, y);
    }

    public void Dispose()
    {
        if (_disposed) return;
        UnloadTexture();
        _disposed = true;
    }

    private void EnsureTexture(int width, int height)
    {
        if (_texture.HasValue && _textureWidth == width && _textureHeight == height)
            return;

        UnloadTexture();

        var image = Raylib.GenImageColor(width, height, Raylib_cs.Color.Blank);
        _texture = Raylib.LoadTextureFromImage(image);
        Raylib.UnloadImage(image);
        _textureWidth = width;
        _textureHeight = height;
    }

    private void UnloadTexture()
    {
        if (!_texture.HasValue)
            return;

        Raylib.UnloadTexture(_texture.Value);
        _texture = null;
        _textureWidth = 0;
        _textureHeight = 0;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DrawContext));
    }
}
