using Raylib_cs;

namespace KSR.Creative;

public sealed class DrawContext : IDisposable
{
    private bool _disposed;

    public void Clear(Raylib_cs.Color color)
    {
        ThrowIfDisposed();
        Raylib.ClearBackground(color);
    }

    public void Fps(int x, int y)
    {
        ThrowIfDisposed();
        Raylib.DrawFPS(x, y);
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DrawContext));
    }
}
