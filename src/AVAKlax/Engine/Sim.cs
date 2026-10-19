namespace AVAKlax.Engine;

/// <summary>The rules, one 60th of a second at a time.</summary>
public static class Sim
{
    /// <summary>Lanes a second the paddle moves; tile lengths a second a tossed tile goes back up; how much quicker the belt goes hurried.</summary>
    public const double PadSpeed = 14, RiseSpeed = 7, Hurry = 3;
    /// <summary>How long a Klax flashes before it's gone.</summary>
    public const double ClearSeconds = 0.45;
    public const int CatchPoints = 5, EmptyPoints = 250;
    /// <summary>The flashing tile, which counts as any colour, comes from this wave on.</summary>
    public const int WildFrom = 6;
    public const double WildChance = 1.0 / 22;

    /// <summary>The warps on offer when a game starts, and their bonuses.</summary>
    public static readonly (int Wave, int Bonus)[] Warps = [(1, 0), (6, 50_000), (11, 150_000)];

    /// <summary>Tile lengths a second down the belt: quicker every wave.</summary>
    public static double BeltSpeed(int wave) => Math.Min(4, 1.2 + 0.12 * (wave - 1));
    /// <summary>Seconds between tiles, about.</summary>
    public static double SpawnEvery(int wave) => Math.Max(0.6, 1.35 - 0.05 * (wave - 1));
    /// <summary>How many colours the tiles come in: four, then more every five waves.</summary>
    public static int Colours(int wave) => Math.Min(Colour.Count, 4 + (wave - 1) / 5);
    /// <summary>How many drops fill the drop meter and lose the wave.</summary>
    public static int DropMeter(int wave) => wave <= 5 ? 5 : wave <= 10 ? 4 : 3;

    private static readonly Goal[] Goals =
    [
        new(GoalKind.Klaxes, 3), new(GoalKind.Tiles, 30), new(GoalKind.Diagonals, 2), new(GoalKind.Points, 10_000), new(GoalKind.Horizontals, 3),
        new(GoalKind.Klaxes, 5), new(GoalKind.Diagonals, 3), new(GoalKind.Tiles, 50), new(GoalKind.Points, 25_000), new(GoalKind.Big, 1),
        new(GoalKind.Klaxes, 8), new(GoalKind.Horizontals, 5), new(GoalKind.Diagonals, 4), new(GoalKind.Points, 50_000), new(GoalKind.Tiles, 75),
    ];

    /// <summary>Fifteen waves of goals, then round again asking half as much more each time.</summary>
    public static Goal GoalOf(int wave)
    {
        var g = Goals[(wave - 1) % Goals.Length];
        int round = (wave - 1) / Goals.Length;
        return g with { Count = (int)Math.Round(g.Count * (1 + 0.5 * round)) };
    }

    /// <summary>What a Klax is worth: across beats up and down, and a diagonal beats both; four or five beat three.</summary>
    public static int KlaxPoints(Line line, int length) => (line, Math.Min(length, 5)) switch
    {
        (Line.Vertical, 3) => 50, (Line.Vertical, 4) => 500, (Line.Vertical, _) => 1_500,
        (Line.Horizontal, 3) => 1_000, (Line.Horizontal, 4) => 5_000, (Line.Horizontal, _) => 10_000,
        (Line.Diagonal, 3) => 5_000, (Line.Diagonal, 4) => 10_000, _ => 20_000,
    };

    public static void StartWave(World w, int wave)
    {
        w.Wave = wave;
        w.Tiles.Clear();
        w.Stack.Clear();
        w.Bin = World.NewBin();
        w.Doomed = new bool[Field.Lanes, Field.Rows];
        w.Clearing = 0;
        w.Chain = 0;
        w.Drops = 0;
        w.Klaxes = w.Diagonals = w.Horizontals = w.Verticals = w.Bigs = w.TilesSeen = w.WaveScore = 0;
        w.PadX = w.PadTo = Field.Lanes / 2;
        w.Lost = w.Done = false;
        w.SpawnIn = 0.5;
        w.Dice = new Dice(wave * 31 + 7);
        w.Landings.Clear();
        w.Falls.Clear();
        w.Popups.Clear();
        w.Made.Clear();
    }

    public static void Step(World w, Controls c)
    {
        double dt = Field.Step;
        if (w.Lost || w.Done) return;
        w.Clock += dt;
        bool flip = c.Flip && !w.FlipHeld, toss = c.Toss && !w.TossHeld;
        w.FlipHeld = c.Flip;
        w.TossHeld = c.Toss;
        MovePaddle(w, c.Move, dt);
        MoveTiles(w, c.Hurry, dt);
        if (w.Lost) return;
        Spawn(w, dt);
        if (flip) Flip(w);
        if (toss) Toss(w);
        if (w.Clearing > 0 && (w.Clearing -= dt) <= 0) Clear(w);
        Age(w, dt);
        if (w.Clearing <= 0 && w.Progress >= w.Goal.Count)
        {
            w.Done = true;
            w.Heard.Add(Sfx.WaveClear);
        }
    }

    // ───────────────────────────── the paddle ─────────────────────────────

    /// <summary>A lane at a time: a tap moves one, held it keeps going; it can turn back between two.</summary>
    private static void MovePaddle(World w, int move, double dt)
    {
        move = Math.Sign(move);
        bool still = Math.Abs(w.PadX - w.PadTo) < 1e-9;
        if (move != 0 && still) w.PadTo = Math.Clamp(w.PadTo + move, 0, Field.Lanes - 1);
        else if (move != 0 && Math.Sign(w.PadTo - w.PadX) == -move) w.PadTo = (int)(move < 0 ? Math.Floor(w.PadX) : Math.Ceiling(w.PadX));
        double d = w.PadTo - w.PadX, go = PadSpeed * dt;
        w.PadX = Math.Abs(d) <= go ? w.PadTo : w.PadX + Math.Sign(d) * go;
    }

    // ───────────────────────────── the belt ─────────────────────────────

    private static void MoveTiles(World w, bool hurry, double dt)
    {
        double speed = BeltSpeed(w.Wave) * (hurry ? Hurry : 1);
        bool flipped = false;
        w.Run += speed * dt;
        foreach (var t in w.Tiles)
        {
            double was = t.P;
            if (t.Rising)
            {
                t.P -= RiseSpeed * dt;
                if (t.P <= 0) { t.P = 0; t.Rising = false; }
            }
            else t.P += speed * dt;
            flipped |= Math.Floor(was) != Math.Floor(t.P);
        }
        // A tile going back up turns round when it comes up behind the next one down the belt.
        foreach (var lane in w.Tiles.GroupBy(t => t.Lane))
        {
            var row = lane.OrderBy(t => t.P).ToList();
            for (int k = 1; k < row.Count; k++)
                if (row[k].P - row[k - 1].P < 1)
                {
                    row[k].Rising = false;
                    row[k].P = row[k - 1].P + 1;
                }
        }
        if (flipped) w.Heard.Add(Sfx.Flip);
        for (int i = 0; i < w.Tiles.Count; i++)
        {
            var t = w.Tiles[i];
            if (t.Rising || t.P < Field.Belt - 1) continue;
            w.Tiles.RemoveAt(i--);
            Arrive(w, t);
            if (w.Lost) return;
        }
    }

    /// <summary>A tile tips off the end of the belt: onto the paddle if it's there with room, else it's a drop.</summary>
    private static void Arrive(World w, Tile t)
    {
        w.TilesSeen++;
        if (w.Lane == t.Lane && w.Stack.Count < Field.Stack)
        {
            w.Stack.Add(t.Colour);
            Points(w, CatchPoints);
            w.Heard.Add(Sfx.Catch);
            return;
        }
        w.Drops++;
        w.Falls.Add((t.Lane, t.Colour, 0));
        w.Heard.Add(Sfx.Drop);
        if (w.Drops >= DropMeter(w.Wave))
        {
            w.Lost = true;
            w.Heard.Add(Sfx.Lost);
        }
        else if (w.Drops == DropMeter(w.Wave) - 1) w.Heard.Add(Sfx.Danger);
    }

    /// <summary>A new tile at the top of a lane with room there, now and then.</summary>
    private static void Spawn(World w, double dt)
    {
        if ((w.SpawnIn -= dt) > 0) return;
        var free = Enumerable.Range(0, Field.Lanes).Where(l => !w.Tiles.Any(t => t.Lane == l && t.P < 1.2)).ToList();
        if (free.Count == 0) { w.SpawnIn = 0.1; return; }
        int lane = free[w.Dice.Next(free.Count)];
        int colour = w.Wave >= WildFrom && w.Dice.Next() < WildChance ? Colour.Wild : w.Dice.Next(Colours(w.Wave));
        w.Tiles.Add(new Tile { Lane = lane, Colour = colour });
        w.SpawnIn = SpawnEvery(w.Wave) * (0.8 + 0.4 * w.Dice.Next());
    }

    /// <summary>The top tile back up the belt, if nothing's about to come down on it.</summary>
    private static void Toss(World w)
    {
        if (w.Stack.Count == 0) return;
        int lane = w.Lane;
        if (w.Tiles.Any(t => t.Lane == lane && t.P > Field.Belt - 2.05)) { w.Heard.Add(Sfx.Blocked); return; }
        w.Tiles.Add(new Tile { Lane = lane, Colour = w.Stack[^1], P = Field.Belt - 1.01, Rising = true });
        w.Stack.RemoveAt(w.Stack.Count - 1);
        w.Heard.Add(Sfx.Toss);
    }

    // ───────────────────────────── the bin ─────────────────────────────

    /// <summary>The top tile off the paddle into the bin below it, to the bottom or onto the tiles there.</summary>
    private static void Flip(World w)
    {
        if (w.Stack.Count == 0) return;
        int col = w.Lane, h = w.Height(col);
        if (w.Clearing > 0 || h >= Field.Rows) { w.Heard.Add(Sfx.Blocked); return; }
        w.Bin[col, h] = w.Stack[^1];
        w.Stack.RemoveAt(w.Stack.Count - 1);
        w.Landings.Add((col, h, 0));
        w.Heard.Add(Sfx.Thunk);
        Check(w, 1);
    }

    /// <summary>
    /// Any Klaxes in the bin: they flash, then go. All found at once count as a combination, each worth as many times
    /// its points as there are of them; and a Klax made by tiles falling into the gap is a chain, worth twice, three
    /// times, and so on.
    /// </summary>
    public static void Check(World w, int chain)
    {
        var found = FindKlaxes(w.Bin);
        if (found.Count == 0) { w.Chain = 0; return; }
        w.Chain = chain;
        w.Clearing = ClearSeconds;
        w.Made = found;
        int points = 0;
        foreach (var k in found)
        {
            foreach (var (c, r) in k.Cells) w.Doomed[c, r] = true;
            int p = KlaxPoints(k.Line, k.Length) * found.Count * chain;
            points += p;
            w.Klaxes++;
            if (k.Line == Line.Diagonal) w.Diagonals++;
            else if (k.Line == Line.Horizontal) w.Horizontals++;
            else w.Verticals++;
            if (k.Length >= 4) w.Bigs++;
            double x = k.Cells.Average(cell => cell.Col), y = k.Cells.Average(cell => cell.Row);
            string name = k.Line switch { Line.Diagonal => "DIAGONAL", Line.Horizontal => "HORIZONTAL", _ => "VERTICAL" };
            w.Popups.Add(($"{(k.Length > 3 ? $"{k.Length} " : "")}{name} {p:N0}", x, y, 0));
        }
        Points(w, points);
        if (found.Count > 1) w.Popups.Add(($"COMBINATION x{found.Count}", 2, 5.4, 0));
        if (chain > 1) w.Popups.Add(($"CHAIN x{chain}", 2, 6.2, 0));
        w.Heard.Add(chain > 1 || found.Count > 1 ? Sfx.Combo : Sfx.Klax);
        if (chain == 1) w.Heard.Add(Sfx.SayKlax);
    }

    /// <summary>The Klax gone, the tiles above falling into the gap, and any Klax that makes.</summary>
    private static void Clear(World w)
    {
        w.Clearing = 0;
        for (int c = 0; c < Field.Lanes; c++)
        {
            var keep = new List<int>();
            for (int r = 0; r < Field.Rows; r++)
                if (w.Bin[c, r] != Colour.Empty && !w.Doomed[c, r]) keep.Add(w.Bin[c, r]);
            for (int r = 0; r < Field.Rows; r++)
            {
                w.Bin[c, r] = r < keep.Count ? keep[r] : Colour.Empty;
                w.Doomed[c, r] = false;
            }
        }
        w.Made = [];
        Check(w, w.Chain + 1);
    }

    /// <summary>Every line in the bin, of three cells or more: the columns, the rows, and the diagonals both ways.</summary>
    public static readonly (Line Line, (int Col, int Row)[] Cells)[] Lines = MakeLines();

    private static (Line, (int, int)[])[] MakeLines()
    {
        var lines = new List<(Line, (int, int)[])>();
        for (int c = 0; c < Field.Lanes; c++) lines.Add((Line.Vertical, Enumerable.Range(0, Field.Rows).Select(r => (c, r)).ToArray()));
        for (int r = 0; r < Field.Rows; r++) lines.Add((Line.Horizontal, Enumerable.Range(0, Field.Lanes).Select(c => (c, r)).ToArray()));
        foreach (int dr in new[] { 1, -1 })
            for (int start = -Field.Rows; start <= Field.Lanes + Field.Rows; start++)
            {
                var cells = new List<(int, int)>();
                for (int c = 0; c < Field.Lanes; c++)
                {
                    int r = dr > 0 ? c - start : start - c;
                    if (r is >= 0 and < Field.Rows) cells.Add((c, r));
                }
                if (cells.Count >= 3) lines.Add((Line.Diagonal, cells.ToArray()));
            }
        return lines.ToArray();
    }

    /// <summary>Every run of three or more of a colour along a line (the wild tile counting as any colour), longest only.</summary>
    public static List<Klax> FindKlaxes(int[,] bin)
    {
        var found = new List<Klax>();
        foreach (var (line, cells) in Lines)
            for (int colour = 0; colour < Colour.Count; colour++)
            {
                int start = 0;
                for (int i = 0; i <= cells.Length; i++)
                {
                    if (i < cells.Length && Colour.Matches(bin[cells[i].Col, cells[i].Row], colour)) continue;
                    if (i - start >= 3 && cells[start..i].Any(cell => bin[cell.Col, cell.Row] == colour))
                        found.Add(new Klax(line, colour, cells[start..i]));
                    start = i + 1;
                }
            }
        return found;
    }

    /// <summary>The end-of-wave bonus: so much for every square of the bin still empty.</summary>
    public static int EmptyBonus(World w) => w.Empties * EmptyPoints;

    private static void Points(World w, int points)
    {
        w.Score += points;
        w.WaveScore += points;
    }

    private static void Age(World w, double dt)
    {
        void Tick<T>(List<T> list, double life, Func<T, double> t, Func<T, double, T> with)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                double next = t(list[i]) + dt;
                if (next > life) list.RemoveAt(i);
                else list[i] = with(list[i], next);
            }
        }
        Tick(w.Landings, 0.3, x => x.T, (x, t) => x with { T = t });
        Tick(w.Falls, 1.0, x => x.T, (x, t) => x with { T = t });
        Tick(w.Popups, 1.6, x => x.T, (x, t) => x with { T = t });
    }
}
