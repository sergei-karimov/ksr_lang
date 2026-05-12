using Raylib_cs;

namespace KSR.Creative;

public static class Creative
{
    private static DrawContext _draw = new();

    public static DrawContext Draw => _draw;

    internal static void Use(DrawContext draw)
    {
        if (ReferenceEquals(_draw, draw))
            return;

        _draw.Dispose();
        _draw = draw;
    }

    internal static void Release(DrawContext draw)
    {
        if (!ReferenceEquals(_draw, draw))
            return;

        _draw = new DrawContext();
        draw.Dispose();
    }
}

public static class Color
{
    public static Raylib_cs.Color Black => Raylib_cs.Color.Black;
    public static Raylib_cs.Color White => Raylib_cs.Color.White;
    public static Raylib_cs.Color RayWhite => Raylib_cs.Color.RayWhite;
    public static Raylib_cs.Color Gray => Raylib_cs.Color.Gray;
    public static Raylib_cs.Color DarkGray => Raylib_cs.Color.DarkGray;
}

public static class draw
{
    public static void Clear(Raylib_cs.Color color) =>
        Creative.Draw.Clear(color);

    public static void Image(KSR.Vision.VideoFrame frame, int x, int y) =>
        Creative.Draw.Image(frame, x, y);

    public static void Fps(int x, int y) =>
        Creative.Draw.Fps(x, y);
}
