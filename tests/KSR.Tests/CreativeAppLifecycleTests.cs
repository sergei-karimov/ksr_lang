using KSR.Creative;
using Xunit;

namespace KSR.Tests;

public class CreativeAppLifecycleTests
{
    [Fact]
    public void Init_Action_RunsExactlyOnce()
    {
        var lifecycle = new AppLifecycle();
        var count = 0;

        lifecycle.Init(() => count++);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Init_Func_ReturnsState()
    {
        var lifecycle = new AppLifecycle();

        var state = lifecycle.Init(() => "ready");

        Assert.Equal("ready", state);
    }

    [Fact]
    public void Init_SecondCall_Throws()
    {
        var lifecycle = new AppLifecycle();
        lifecycle.Init(() => { });

        var ex = Assert.Throws<InvalidOperationException>(() => lifecycle.Init(() => { }));

        Assert.Contains("Init can only be executed once", ex.Message);
    }

    [Fact]
    public void Draw_SecondRegistration_Throws()
    {
        var lifecycle = new AppLifecycle();
        lifecycle.Draw(() => { });

        var ex = Assert.Throws<InvalidOperationException>(() => lifecycle.Draw(() => { }));

        Assert.Contains("Draw can only be registered once", ex.Message);
    }

    [Fact]
    public void Cleanup_SecondRegistration_Throws()
    {
        var lifecycle = new AppLifecycle();
        lifecycle.Cleanup(() => { });

        var ex = Assert.Throws<InvalidOperationException>(() => lifecycle.Cleanup(() => { }));

        Assert.Contains("Cleanup can only be registered once", ex.Message);
    }

    [Fact]
    public void Cleanup_InvokesOnlyOnce()
    {
        var lifecycle = new AppLifecycle();
        var count = 0;
        lifecycle.Cleanup(() => count++);

        lifecycle.InvokeCleanupOnce();
        lifecycle.InvokeCleanupOnce();

        Assert.Equal(1, count);
    }

    [Fact]
    public void Cleanup_CanRunAfterDrawException()
    {
        var lifecycle = new AppLifecycle();
        var cleaned = false;
        lifecycle.Draw(() => throw new InvalidOperationException("draw failed"));
        lifecycle.Cleanup(() => cleaned = true);

        try
        {
            Assert.Throws<InvalidOperationException>(() => lifecycle.InvokeDraw());
        }
        finally
        {
            lifecycle.InvokeCleanupOnce();
        }

        Assert.True(cleaned);
    }

    [Fact]
    public void RunGuard_FailedInitPreventsDraw()
    {
        var lifecycle = new AppLifecycle();
        Assert.Throws<InvalidOperationException>(() => lifecycle.Init<object>(() => throw new InvalidOperationException("boom")));
        lifecycle.Draw(() => { });

        var ex = Assert.Throws<InvalidOperationException>(() => lifecycle.InvokeDraw());

        Assert.Contains("Init did not complete successfully", ex.Message);
    }
}
