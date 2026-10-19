using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAKlax.Engine;

namespace AVAKlax.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAKlax/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    /// <summary>The tiles' colours, in <see cref="Colour"/>'s order.</summary>
    public static readonly uint[] TileColours = [0xFFE8343C, 0xFFFFD23A, 0xFF34C85A, 0xFF3A7CFF, 0xFFFF5CC8, 0xFFFF8A2A, 0xFFE4ECF8];

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private static uint Shade(uint c, double k)
    {
        uint Ch(int shift) => (uint)Math.Clamp(Math.Round(((c >> shift) & 0xFF) * k), 0, 255);
        return (c & 0xFF000000) | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    /// <summary>Part way from one colour to another.</summary>
    private static uint Mix(uint a, uint b, double t)
    {
        uint Ch(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    private static Point Lerp(Point a, Point b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    /// <summary>A tile's colour on screen: the wild one runs through them all.</summary>
    private uint TileColour(int colour, double salt = 0) =>
        colour == Colour.Wild ? TileColours[(int)(_clock * 9 + salt) % TileColours.Length] : TileColours[Math.Clamp(colour, 0, TileColours.Length - 1)];

    // ───────────────────────────── the belt, seen in perspective ─────────────────────────────

    /// <summary>How the belt is seen: a lane is this wide at the near end, the far end is smaller by this much a tile length.</summary>
    private const double LaneW = 120, NearY = 452, HorizonY = -190, Depth = 0.12, CenterX = 512;

    private static double Scale(double u) => 1 / (1 + (Field.Belt - u) * Depth);

    /// <summary>
    /// A point on or over the belt to the screen: x across it in lanes (0 the middle), u down it in tile lengths (0
    /// the far end), h above it in tile lengths.
    /// </summary>
    private static Point B(double x, double u, double h = 0)
    {
        double s = Scale(u);
        return new Point(CenterX + x * LaneW * s, HorizonY + (NearY - HorizonY) * s - h * LaneW * s * 0.85);
    }

    private static double LaneX(double lane) => lane - (Field.Lanes - 1) / 2.0;

    private void DrawScene(DrawingContext dc, World w, bool live)
    {
        DrawBackdrop(dc, w.Wave);
        DrawBelt(dc, w.Run);
        DrawBin(dc, w);
        // The tiles on the belt, far ones first.
        foreach (var t in w.Tiles.OrderBy(t => t.P)) DrawBeltTile(dc, t.Lane, t.P, t.Colour, t.Lane * 3 + t.P);
        DrawPaddle(dc, w);
        foreach (var (lane, colour, t) in w.Falls) DrawFall(dc, lane, colour, t);
        DrawPopups(dc, w);
        DrawPanels(dc, w);
        if (live && w.Clearing > 0) DrawKlaxWord(dc, w);
    }

    private void DrawBackdrop(DrawingContext dc, int wave)
    {
        // Each group of five waves its own colour of night.
        uint[] skies = [0xFF0E1030, 0xFF1A0E2A, 0xFF071E26, 0xFF221208];
        uint sky = skies[(wave - 1) / 5 % skies.Length];
        for (int i = 0; i < 16; i++)
            dc.FillRectangle(Brush(Mix(sky, 0xFF000000, i / 18.0)), new Rect(0, i * 48, Field.Width, 49));
        // A grid of light running away to the horizon behind the belt, the way 1989 imagined the future.
        for (int k = -12; k <= 12; k++)
            dc.DrawLine(Pen(Alpha(0xFF6A5AFF, 0.18), 1.5), new Point(CenterX + k * 30, 0), new Point(CenterX + k * 170, 768));
        for (int k = 0; k < 12; k++)
        {
            double y = 768 - Math.Pow(0.78, k) * 640;
            dc.DrawLine(Pen(Alpha(0xFF6A5AFF, 0.16), 1.5), new Point(0, y), new Point(Field.Width, y));
        }
        for (int i = 0; i < 50; i++)
        {
            double x = Hash(i, 1, 9) % 1024, y = Hash(i, 2, 9) % 130;
            double tw = 0.4 + 0.4 * Math.Sin(_clock * 2 + i);
            dc.FillEllipse(x, y, 1.2, Alpha(0xFFFFFFFF, tw));
        }
    }

    private static readonly double[] Ribs = Enumerable.Range(0, 16).Select(i => i * 0.5).ToArray();

    private void DrawBelt(DrawingContext dc, double run)
    {
        double half = Field.Lanes / 2.0, end = Field.Belt;
        // The legs and the frame under it.
        foreach (double s in new[] { -1.0, 1.0 })
        {
            var top = B(s * (half + 0.18), end * 0.55);
            dc.DrawLine(Pen(0xFF2A2C40, 10), top, new Point(top.X + s * 6, 560));
        }
        // The belt itself.
        Poly(dc, 0xFF1C1F2E, null, 0, B(-half - 0.06, 0), B(half + 0.06, 0), B(half + 0.06, end), B(-half - 0.06, end));
        Poly(dc, 0xFF2C3146, null, 0, B(-half, 0), B(half, 0), B(half, end), B(-half, end));
        // The ribs, running toward you.
        double phase = run % 0.5;
        foreach (double r in Ribs)
        {
            double u = r + phase;
            if (u > end) continue;
            double s = Scale(u);
            dc.DrawLine(Pen(Alpha(0xFF4A5170, 0.5 + 0.5 * s), 2 * s), B(-half, u), B(half, u));
        }
        // The lanes.
        for (int k = 1; k < Field.Lanes; k++)
        {
            double x = -half + k;
            Poly(dc, 0xFF181A26, null, 0, B(x - 0.03, 0), B(x + 0.03, 0), B(x + 0.03, end), B(x - 0.03, end));
        }
        // The rails down each side, lit along their tops.
        foreach (double s in new[] { -1.0, 1.0 })
        {
            double a = s * half, b = s * (half + 0.22);
            Poly(dc, 0xFF5A6488, null, 0, B(a, 0, 0.12), B(b, 0, 0.12), B(b, end, 0.12), B(a, end, 0.12));
            Poly(dc, 0xFF353B56, null, 0, B(b, 0, 0.12), B(b, end, 0.12), B(b, end, -0.05), B(b, 0, -0.05));
            dc.DrawLine(Pen(0xFFB8C4F0, 2), B(a, 0, 0.12), B(a, end, 0.12));
            for (int k = 0; k <= 8; k++)
            {
                // Rivets, and a lamp at each end.
                var p = B((a + b) / 2, k * end / 8, 0.12);
                dc.FillEllipse(p.X, p.Y, 2.4 * Scale(k), 0xFF22263A);
            }
        }
        // The hatch at the top the tiles come out of.
        Poly(dc, 0xFF0A0B12, 0xFF5A6488, 2, B(-half - 0.25, -0.15, 0.55), B(half + 0.25, -0.15, 0.55), B(half + 0.25, 0.05, 0), B(-half - 0.25, 0.05, 0));
        for (int k = 0; k < Field.Lanes; k++)
        {
            var p = B(LaneX(k), -0.05, 0.42);
            dc.FillEllipse(p.X, p.Y, 3, Alpha(0xFFFFB040, 0.6 + 0.4 * Math.Sin(_clock * 4 + k)));
        }
        // The lip at the near end.
        Poly(dc, 0xFF5A6488, null, 0, B(-half - 0.22, end), B(half + 0.22, end), B(half + 0.22, end + 0.06, -0.12), B(-half - 0.22, end + 0.06, -0.12));
    }

    /// <summary>
    /// A tile on the belt, tumbling end over end: it turns about its near edge, its far edge coming up and over, and
    /// lands flat a tile length nearer; then the next.
    /// </summary>
    private void DrawBeltTile(DrawingContext dc, int lane, double p, int colour, double salt)
    {
        double step = Math.Floor(p), frac = p - step, theta = frac * Math.PI;
        double hinge = step + 0.96, len = 0.9, lift = 0.07;
        double x0 = LaneX(lane) - 0.43, x1 = LaneX(lane) + 0.43;
        double fu = hinge - len * Math.Cos(theta), fh = lift + len * Math.Sin(theta);
        // The face's normal, in (u, h): up when flat, toward you standing up, down when it lands.
        double nu = Math.Sin(theta), nh = Math.Cos(theta), thick = 0.09;
        uint c = TileColour(colour, salt);
        // The slab's thickness: the face again, pushed back along the normal.
        var back = new[] { B(x0, hinge - nu * thick, lift - nh * thick), B(x1, hinge - nu * thick, lift - nh * thick), B(x1, fu - nu * thick, fh - nh * thick), B(x0, fu - nu * thick, fh - nh * thick) };
        Poly(dc, Shade(c, 0.45), null, 0, back);
        var face = new[] { B(x0, hinge, lift), B(x1, hinge, lift), B(x1, fu, fh), B(x0, fu, fh) };
        Poly(dc, Shade(c, 0.45), null, 0, back[0], back[1], face[1], face[0]);
        // Lit from above and in front.
        double light = 0.62 + 0.38 * Math.Abs(nu * 0.55 + nh * 0.83);
        Face(dc, face, c, light, colour == Colour.Wild);
    }

    /// <summary>A tile's face: its colour, a bevel, and a glint; the wild one with a white rim that flashes.</summary>
    private void Face(DrawingContext dc, Point[] q, uint c, double light, bool wild)
    {
        var mid = new Point(q.Average(p => p.X), q.Average(p => p.Y));
        Poly(dc, Shade(c, light * 0.78), null, 0, q);
        var inner = q.Select(p => Lerp(p, mid, 0.2)).ToArray();
        Poly(dc, Shade(c, Math.Min(1.25, light * 1.04)), null, 0, inner);
        // The glint along the inner far edge.
        dc.DrawLine(Pen(Alpha(0xFFFFFFFF, 0.55 * light), 2), Lerp(inner[3], inner[2], 0.15), Lerp(inner[3], inner[2], 0.6));
        if (wild)
        {
            bool on = (int)(_clock * 8) % 2 == 0;
            Poly(dc, 0, on ? 0xFFFFFFFF : 0xFF202020, 3, q);
            var star = Lerp(inner[0], inner[2], 0.5);
            double r = Math.Abs(inner[2].Y - inner[0].Y) * 0.25 + 3;
            Poly(dc, 0xFFFFFFFF, null, 0, new Point(star.X, star.Y - r), new Point(star.X + r * 0.3, star.Y - r * 0.3), new Point(star.X + r, star.Y),
                new Point(star.X + r * 0.3, star.Y + r * 0.3), new Point(star.X, star.Y + r), new Point(star.X - r * 0.3, star.Y + r * 0.3),
                new Point(star.X - r, star.Y), new Point(star.X - r * 0.3, star.Y - r * 0.3));
        }
        else Poly(dc, 0, Shade(c, 0.4), 1.5, q);
    }

    // ───────────────────────────── the paddle ─────────────────────────────

    private const double PadNear = Field.Belt + 0.9, PadFar = Field.Belt + 0.08;

    private void DrawPaddle(DrawingContext dc, World w)
    {
        double x = LaneX(w.PadX), x0 = x - 0.52, x1 = x + 0.52;
        // The tray: its top, and its front.
        Poly(dc, 0xFF8890B8, null, 0, B(x0, PadFar), B(x1, PadFar), B(x1, PadNear), B(x0, PadNear));
        Poly(dc, 0xFF3A4062, null, 0, B(x0, PadNear), B(x1, PadNear), B(x1, PadNear, -0.2), B(x0, PadNear, -0.2));
        Poly(dc, 0, 0xFFD8E0FF, 2, B(x0, PadFar), B(x1, PadFar), B(x1, PadNear), B(x0, PadNear));
        // Lamps along the front: one lit for each tile it holds.
        for (int k = 0; k < Field.Stack; k++)
        {
            var p = B(x0 + 0.17 + k * 0.175, PadNear, -0.1);
            dc.FillEllipse(p.X, p.Y, 4, k < w.Stack.Count ? 0xFF7CFF8A : 0xFF20283A);
        }
        // The stack, bottom first.
        double t0 = 0.12, t1 = PadNear - 0.08, th = 0.16;
        for (int i = 0; i < w.Stack.Count; i++)
        {
            uint c = TileColour(w.Stack[i], i * 2);
            double h = 0.02 + i * th;
            double a0 = x0 + 0.06, a1 = x1 - 0.06;
            Poly(dc, Shade(c, 0.55), null, 0, B(a0, t1, h), B(a1, t1, h), B(a1, t1, h + th * 0.82), B(a0, t1, h + th * 0.82));
            Face(dc, [B(a0, t1, h + th * 0.82), B(a1, t1, h + th * 0.82), B(a1, PadFar + t0, h + th * 0.82), B(a0, PadFar + t0, h + th * 0.82)], c, 1, w.Stack[i] == Colour.Wild);
        }
    }

    /// <summary>A tile missed: off the end of the belt and down past the paddle.</summary>
    private void DrawFall(DrawingContext dc, int lane, int colour, double t)
    {
        double x = LaneX(lane) + (lane - 2) * 0.3 * t, h = -4.5 * t * t, a = 1 - t;
        double tilt = t * 5;
        var c = Alpha(TileColour(colour), a);
        Poly(dc, c, Alpha(0xFF000000, a), 2,
            B(x - 0.4 * Math.Cos(tilt), Field.Belt + 0.3, h + 0.3 * Math.Sin(tilt)), B(x + 0.4 * Math.Cos(tilt), Field.Belt + 0.3, h - 0.3 * Math.Sin(tilt)),
            B(x + 0.4 * Math.Cos(tilt), Field.Belt + 0.8, h - 0.3 * Math.Sin(tilt)), B(x - 0.4 * Math.Cos(tilt), Field.Belt + 0.8, h + 0.3 * Math.Sin(tilt)));
        if (t < 0.7) Text(dc, "DROP!", B(LaneX(lane), Field.Belt - 0.6, 0.6 + t), 36, Alpha(Red, 1 - t / 0.7), center: true);
    }

    // ───────────────────────────── the bin ─────────────────────────────

    private const double BinTop = 548, BinBottom = 748, BinTopW = 560, BinBottomW = 640;

    /// <summary>A point in the bin: column 0 to 5 across, row 0 (the bottom) to 5 (the top) up.</summary>
    private static Point Cell(double col, double row)
    {
        double y = BinBottom - (BinBottom - BinTop) * row / Field.Rows;
        double wd = BinBottomW + (BinTopW - BinBottomW) * (BinBottom - y) / (BinBottom - BinTop);
        return new Point(CenterX + (col / Field.Lanes - 0.5) * wd, y);
    }

    private static Point CellCentre(double col, double row) => Cell(col + 0.5, row + 0.5);

    private void DrawBin(DrawingContext dc, World w)
    {
        // The well: a rim, the floor, the grid.
        Poly(dc, 0xFF3A4062, 0xFF8890B8, 3, Cell(0, 5.15) - new Point(14, 0), Cell(5, 5.15) + new Point(14, 0), Cell(5, 0) + new Point(16, 8), Cell(0, 0) + new Point(-16, 8));
        Poly(dc, 0xFF0C0E18, null, 0, Cell(0, 5), Cell(5, 5), Cell(5, 0), Cell(0, 0));
        for (int c = 0; c < Field.Lanes; c++)
        for (int r = 0; r < Field.Rows; r++)
        {
            var q = new[] { Cell(c, r + 1), Cell(c + 1, r + 1), Cell(c + 1, r), Cell(c, r) };
            Poly(dc, (c + r) % 2 == 0 ? 0xFF141828 : 0xFF181D30, 0xFF262C44, 1, q);
        }
        // The column under the paddle, lit.
        int lane = w.Lane;
        Poly(dc, Alpha(0xFF7CFF8A, 0.07), null, 0, Cell(lane, 5), Cell(lane + 1, 5), Cell(lane + 1, 0), Cell(lane, 0));
        for (int c = 0; c < Field.Lanes; c++)
        for (int r = 0; r < Field.Rows; r++)
        {
            int k = w.Bin[c, r];
            if (k == Colour.Empty) continue;
            double drop = 0;
            foreach (var (lc, lr, t) in w.Landings)
                if (lc == c && lr == r) drop = (Field.Rows - r) * (1 - t / 0.3) * (1 - t / 0.3);
            var q = new[] { Cell(c + 0.06, r + 0.94 + drop), Cell(c + 0.94, r + 0.94 + drop), Cell(c + 0.94, r + 0.06 + drop), Cell(c + 0.06, r + 0.06 + drop) };
            uint col = TileColour(k, c + r * 5);
            bool doomed = w.Doomed[c, r];
            if (doomed && (int)(_clock * 14) % 2 == 0) col = 0xFFFFFFFF;
            // Its side, then its face.
            Poly(dc, Shade(col, 0.5), null, 0, q[3], q[2], q[2] + new Point(0, 6), q[3] + new Point(0, 6));
            Face(dc, q, col, 1, k == Colour.Wild);
            if (doomed) Poly(dc, 0, 0xFFFFFFFF, 3, q);
        }
        // A line through each Klax as it flashes.
        if (w.Clearing > 0)
            foreach (var k in w.Made)
            {
                var a = CellCentre(k.Cells[0].Col, k.Cells[0].Row);
                var b = CellCentre(k.Cells[^1].Col, k.Cells[^1].Row);
                dc.DrawLine(Pen(Alpha(0xFFFFFFFF, 0.5), 16), a, b);
                dc.DrawLine(Pen(0xFFFFFFFF, 5), a, b);
            }
    }

    private static void DrawPopups(DrawingContext dc, World w)
    {
        foreach (var (text, x, y, t) in w.Popups)
        {
            var at = CellCentre(x, y) - new Point(0, t * 50);
            Text(dc, text, at, 34, Alpha(t < 0.4 ? White : Yellow, 1.6 - t), center: true);
        }
    }

    private void DrawKlaxWord(DrawingContext dc, World w)
    {
        double t = 1 - w.Clearing / Sim.ClearSeconds;
        double size = 70 + 70 * Math.Min(1, t * 3);
        string word = w.Chain > 1 ? $"KLAX x{w.Chain}!" : w.Made.Count > 1 ? "KLAX KLAX!" : "KLAX!";
        uint c = TileColours[(int)(_clock * 12) % TileColours.Length];
        Text(dc, word, new Point(CenterX, 280), size, c, center: true);
    }

    // ───────────────────────────── the score, the drop meter and the goal ─────────────────────────────

    private void DrawPanels(DrawingContext dc, World w)
    {
        var g = Engine;
        Text(dc, $"SCORE {w.Score:N0}", new Point(24, 34), 36, White);
        Text(dc, $"HIGH {Math.Max(g.HighScores.TopScore, w.Score):N0}", new Point(CenterX, 34), 28, Dim, center: true);
        Text(dc, $"WAVE {w.Wave}", new Point(1000, 34), 36, Yellow, right: true);

        // Left: the drop meter, and the tries left.
        int meter = Sim.DropMeter(w.Wave);
        Panel(dc, new Rect(16, 150, 176, 330));
        Text(dc, "DROP METER", new Point(104, 176), 26, Orange, center: true);
        for (int k = 0; k < meter; k++)
        {
            var r = new Rect(44, 208 + k * 46, 120, 34);
            bool lit = k < w.Drops;
            bool warn = !lit && k == w.Drops && w.Drops == meter - 1 && (int)(_clock * 4) % 2 == 0;
            dc.FillRectangle(Brush(lit ? 0xFFFF3A2A : warn ? 0xFF6A2018 : 0xFF20263A), r, 6);
            if (lit) dc.FillRectangle(Brush(0x60FFFFFF), new Rect(r.X + 6, r.Y + 4, r.Width - 12, 8), 4);
            dc.DrawRectangle(null, Pen(0xFF5A6488, 2), r, 6, 6);
        }
        Text(dc, $"{meter - w.Drops} MORE AND", new Point(104, 208 + meter * 46 + 14), 22, Dim, center: true);
        Text(dc, "THE WAVE IS LOST", new Point(104, 208 + meter * 46 + 36), 22, Dim, center: true);
        Panel(dc, new Rect(16, 500, 176, 92));
        Text(dc, "TRIES", new Point(104, 524), 26, Orange, center: true);
        int tries = g.State is GameState.Title ? GameEngine.Lives0 : g.Lives;
        for (int k = 0; k < Math.Min(tries, 6); k++)
        {
            double x = 104 - (Math.Min(tries, 6) - 1) * 14 + k * 28;
            dc.FillRectangle(Brush(0xFF8890B8), new Rect(x - 11, 556, 22, 10), 3);
            dc.FillRectangle(Brush(TileColours[k % 4]), new Rect(x - 9, 548, 18, 7), 2);
        }

        // Right: the goal, and how far it's got.
        var goal = w.Goal;
        Panel(dc, new Rect(832, 150, 176, 330));
        Text(dc, "THE GOAL", new Point(920, 176), 26, Orange, center: true);
        var words = goal.Text.Split(' ');
        int half = (words.Length + 1) / 2;
        Text(dc, string.Join(' ', words[..half]), new Point(920, 214), 24, White, center: true);
        Text(dc, string.Join(' ', words[half..]), new Point(920, 238), 24, White, center: true);
        int have = Math.Min(w.Progress, goal.Count);
        if (goal.Kind == GoalKind.Points)
        {
            Text(dc, $"{have:N0}", new Point(920, 276), 38, Yellow, center: true);
            Text(dc, $"OF {goal.Count:N0}", new Point(920, 306), 22, White, center: true);
        }
        else Text(dc, $"{have} OF {goal.Count}", new Point(920, 290), 44, Yellow, center: true);
        var bar = new Rect(848, 326, 144, 18);
        dc.FillRectangle(Brush(0xFF20263A), bar, 6);
        dc.FillRectangle(Brush(0xFF7CFF8A), new Rect(bar.X, bar.Y, bar.Width * have / Math.Max(1, goal.Count), bar.Height), 6);
        dc.DrawRectangle(null, Pen(0xFF5A6488, 2), bar, 6, 6);
        Text(dc, $"KLAXES {w.Klaxes}", new Point(920, 370), 22, Dim, center: true);
        Text(dc, $"TILES {w.TilesSeen}", new Point(920, 394), 22, Dim, center: true);
        Text(dc, "COLOURS", new Point(920, 428), 22, Dim, center: true);
        int n = Sim.Colours(w.Wave);
        for (int k = 0; k < n; k++)
            dc.FillRectangle(Brush(TileColours[k]), new Rect(920 - n * 10 + k * 20 + 2, 446, 16, 16), 3);
        if (w.Wave >= Sim.WildFrom)
        {
            Panel(dc, new Rect(832, 500, 176, 92));
            Text(dc, "WILD TILE", new Point(920, 524), 26, Orange, center: true);
            Face(dc, [new Point(898, 546), new Point(942, 546), new Point(942, 582), new Point(898, 582)], TileColour(Colour.Wild), 1, true);
        }
    }

    private static void Panel(DrawingContext dc, Rect r)
    {
        dc.FillRectangle(Brush(0xC00A0C18), r, 10);
        dc.DrawRectangle(null, Pen(0xFF3A4062, 2), r, 10, 10);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
