using Raylib_cs;

namespace KSR.Creative;

public sealed class CreativeApp : IDisposable
{
    private readonly int _width;
    private readonly int _height;
    private readonly DrawContext _draw = new();
    private readonly AppLifecycle _lifecycle = new();
    private bool _closeRequested;
    private bool _closed;
    private bool _disposed;
    private bool _running;

    public CreativeApp(int width, int height, string title)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Window width must be positive.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "Window height must be positive.");

        _width = width;
        _height = height;
        Creative.Use(_draw);
        Raylib.InitWindow(width, height, title);
        Raylib.SetTargetFPS(60);
    }

    public T Init<T>(Func<T> initializer)
    {
        ThrowIfDisposed();
        return _lifecycle.Init(initializer);
    }

    public T Init<T>(Func<object?, T> initializer)
    {
        ArgumentNullException.ThrowIfNull(initializer);
        ThrowIfDisposed();
        return _lifecycle.Init(() => initializer(null));
    }

    public void Init(Action initializer)
    {
        ThrowIfDisposed();
        _lifecycle.Init(initializer);
    }

    public void Init(Action<object?> initializer)
    {
        ArgumentNullException.ThrowIfNull(initializer);
        ThrowIfDisposed();
        _lifecycle.Init(() => initializer(null));
    }

    public void Draw(Action draw)
    {
        ThrowIfDisposed();
        _lifecycle.Draw(draw);
    }

    public void Draw(Action<object?> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        ThrowIfDisposed();
        _lifecycle.Draw(() => draw(null));
    }

    public void Cleanup(Action cleanup)
    {
        ThrowIfDisposed();
        _lifecycle.Cleanup(cleanup);
    }

    public void Cleanup(Action<object?> cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        ThrowIfDisposed();
        _lifecycle.Cleanup(() => cleanup(null));
    }

    public void Run()
    {
        ThrowIfDisposed();
        _lifecycle.EnsureReadyToRun();

        _running = true;
        try
        {
            while (!_closeRequested && !Raylib.WindowShouldClose())
            {
                Raylib.BeginDrawing();
                try
                {
                    _lifecycle.InvokeDraw();
                }
                finally
                {
                    Raylib.EndDrawing();
                }
            }
        }
        finally
        {
            _running = false;
            try
            {
                _lifecycle.InvokeCleanupOnce();
            }
            finally
            {
                Dispose();
            }
        }
    }

    public void Run(Action draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        Draw(draw);
        Run();
    }

    public void Run(Action<object?> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        Draw(draw);
        Run();
    }

    public void Run(Action<DrawContext> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        Draw(() => draw(_draw));
        Run();
    }

    public void Close()
    {
        if (_running)
        {
            _closeRequested = true;
            return;
        }

        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Creative.Release(_draw);
        if (!_closed)
        {
            Raylib.CloseWindow();
            _closed = true;
        }
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CreativeApp), $"CreativeApp({_width}x{_height}) is already closed.");
    }
}
