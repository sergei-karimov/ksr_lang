namespace KSR.Creative;

internal sealed class AppLifecycle
{
    private bool _initCalled;
    private bool _initSucceeded;
    private bool _drawRegistered;
    private bool _cleanupRegistered;
    private bool _cleanupInvoked;
    private Action? _draw;
    private Action? _cleanup;

    public T Init<T>(Func<T> initializer)
    {
        ArgumentNullException.ThrowIfNull(initializer);
        EnsureInitNotCalled();

        _initCalled = true;
        try
        {
            var state = initializer();
            _initSucceeded = true;
            return state;
        }
        catch
        {
            _initSucceeded = false;
            throw;
        }
    }

    public void Init(Action initializer)
    {
        ArgumentNullException.ThrowIfNull(initializer);
        EnsureInitNotCalled();

        _initCalled = true;
        try
        {
            initializer();
            _initSucceeded = true;
        }
        catch
        {
            _initSucceeded = false;
            throw;
        }
    }

    public void Draw(Action draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        if (_drawRegistered)
            throw new InvalidOperationException("CreativeApp.Draw can only be registered once in the MVP.");

        _draw = draw;
        _drawRegistered = true;
    }

    public void Cleanup(Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        if (_cleanupRegistered)
            throw new InvalidOperationException("CreativeApp.Cleanup can only be registered once in the MVP.");

        _cleanup = cleanup;
        _cleanupRegistered = true;
    }

    public void EnsureReadyToRun()
    {
        if (_initCalled && !_initSucceeded)
            throw new InvalidOperationException("CreativeApp cannot run because Init did not complete successfully.");

        if (!_drawRegistered)
            throw new InvalidOperationException("CreativeApp.Draw must be registered before Run().");
    }

    public void InvokeDraw()
    {
        EnsureReadyToRun();
        _draw!.Invoke();
    }

    public void InvokeCleanupOnce()
    {
        if (_cleanupInvoked)
            return;

        _cleanupInvoked = true;
        _cleanup?.Invoke();
    }

    private void EnsureInitNotCalled()
    {
        if (_initCalled)
            throw new InvalidOperationException("CreativeApp.Init can only be executed once.");
    }
}
