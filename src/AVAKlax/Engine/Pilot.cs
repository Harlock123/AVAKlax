namespace AVAKlax.Engine;

/// <summary>
/// The computer player, for the demonstration and the tests. It looks at the tile on top of its paddle and tries it in
/// every column of a copy of the bin: a column that makes a Klax is best (and a Klax the wave's goal wants, better
/// still); else one that lines it up with tiles of its colour, in lines that could still be finished, low down. It
/// takes the tile there and flips it in if that's worth doing (or the paddle is getting full) and there's time
/// before the next tile tips off the belt; otherwise it goes to catch that tile. With a full paddle and nowhere good
/// for the top tile, it tosses it back up the belt.
/// </summary>
public sealed class Pilot
{
    /// <summary>How good a column must be to flip a tile in before the paddle has three.</summary>
    public const double Keen = 2.5;
    private bool _pressed;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _pressed = false;
        Doing = "";
    }

    public Controls Decide(GameEngine g) => g.Phase != Phase.Play ? default : Decide(g.World);

    public Controls Decide(World w)
    {
        if (w.Lost || w.Done) return default;
        // The next tile to tip off the end, and how long until it does.
        double speed = Sim.BeltSpeed(w.Wave);
        Tile? next = null;
        double due = double.PositiveInfinity;
        foreach (var t in w.Tiles)
        {
            double d = t.Rising ? (2 * t.P + Field.Belt - 1) / speed : (Field.Belt - 1 - t.P) / speed;
            if (d < due) { due = d; next = t; }
        }
        int target = next?.Lane ?? w.PadTo;
        bool flip = false, toss = false;
        Doing = next == null ? "waiting" : "catching";
        if (w.Stack.Count > 0)
        {
            var (col, value) = BestColumn(w, w.Stack[^1]);
            bool full = w.Stack.Count >= Field.Stack;
            double travel = Math.Abs(w.PadX - col) / Sim.PadSpeed + 4 * Field.Step + (next == null ? 0 : Math.Abs(col - next.Lane) / Sim.PadSpeed + 0.08);
            bool want = value >= Keen || w.Stack.Count >= 3 || full;
            if (col >= 0 && want && (travel < due || full))
            {
                target = col;
                Doing = $"flipping into {col}";
                flip = Math.Abs(w.PadX - col) < 1e-9 && w.Clearing <= 0;
            }
            else if (full && (col < 0 || value < -5))
            {
                // Nowhere for it: back up the belt.
                Doing = "tossing";
                toss = Math.Abs(w.PadX - w.PadTo) < 1e-9;
            }
        }
        int move = Math.Abs(target - w.PadX) < 1e-9 ? 0 : Math.Sign(target - w.PadX);
        // A press needs a release between.
        if (_pressed) { _pressed = false; return new Controls(move); }
        _pressed = flip || toss;
        return new Controls(move, flip, toss);
    }

    /// <summary>The best column for a tile, and how good it is; -1 if every column is full.</summary>
    public static (int Col, double Value) BestColumn(World w, int colour)
    {
        int bestCol = -1;
        double best = double.NegativeInfinity;
        for (int c = 0; c < Field.Lanes; c++)
        {
            int r = w.Height(c);
            if (r >= Field.Rows) continue;
            double v = Value(w, c, r, colour) - 0.01 * Math.Abs(c - w.PadX);
            if (v > best) { best = v; bestCol = c; }
        }
        return (bestCol, best);
    }

    /// <summary>How good a tile of a colour would be at a cell (the top of its column).</summary>
    public static double Value(World w, int col, int row, int colour)
    {
        var bin = (int[,])w.Bin.Clone();
        bin[col, row] = colour;
        var goal = w.Goal.Kind;
        double Weight(Line line) => (goal, line) switch
        {
            (GoalKind.Diagonals, Line.Diagonal) => 4,
            (GoalKind.Diagonals, Line.Vertical) => 0.4,
            (GoalKind.Horizontals, Line.Horizontal) => 3,
            (GoalKind.Horizontals, Line.Vertical) => 0.5,
            (GoalKind.Points, Line.Horizontal) => 2,
            (GoalKind.Points, Line.Diagonal) => 2.5,
            (GoalKind.Big, Line.Vertical) => 0.5,
            (GoalKind.Tiles, Line.Vertical) => 1.5,
            _ => 1,
        };
        bool big = goal == GoalKind.Big;
        var made = Sim.FindKlaxes(bin);
        if (made.Count > 0)
        {
            // Wanting four or five in a line, a three only clears a little room.
            if (big && made.All(k => k.Length < 4)) return 1 + 0.2 * (Field.Rows - row);
            return 20 + made.Sum(k => Weight(k.Line) * (k.Length >= 4 ? big ? 40 : 3 : 1) * 10) + 0.5 * (Field.Rows - row);
        }
        // Lines it could still be part of: each three in a row through it with nothing in the way (four, across or
        // diagonally, when the wave wants big ones), worth more the more of the colour there already, and the sooner
        // the gaps could be filled.
        double v = 0;
        foreach (var (line, cells) in Sim.Lines)
        {
            int at = Array.IndexOf(cells, (col, row));
            if (at < 0) continue;
            int n = big && line != Line.Vertical ? 4 : 3;
            for (int s = Math.Max(0, at - n + 1); s <= at && s + n <= cells.Length; s++)
            {
                double same = 0, gaps = 1;
                bool open = true;
                // The wild tile takes the colour of whatever it lines up with.
                int want = colour;
                for (int k = s; k < s + n && open; k++)
                {
                    if (k == at) continue;
                    var (c, r) = cells[k];
                    int x = bin[c, r];
                    if (x == Colour.Empty)
                    {
                        int h = c == col ? row + 1 : w.Height(c);
                        gaps *= r == h ? 1 : 0.4 / (r - h);
                    }
                    else if (x == Colour.Wild) same++;
                    else if (want == Colour.Wild) { want = x; same++; }
                    else if (x == want) same++;
                    else open = false;
                }
                if (open && same > 0) v += Weight(line) * same * same * gaps;
            }
        }
        // Low down is better, and a column left full is worse.
        v -= 0.35 * row + (row == Field.Rows - 1 ? 1.5 : 0);
        // Burying a tile of another colour that could have gone on to make a line costs a little.
        if (row > 0 && w.Bin[col, row - 1] != colour && w.Bin[col, row - 1] != Colour.Wild) v -= 0.3;
        return v;
    }
}
