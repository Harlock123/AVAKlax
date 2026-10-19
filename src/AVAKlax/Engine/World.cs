namespace AVAKlax.Engine;

/// <summary>The screen, and the sizes of the things on it: five lanes of belt, a paddle, and a five-by-five bin.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    /// <summary>Lanes across the belt, and columns of the bin under them.</summary>
    public const int Lanes = 5;
    /// <summary>How deep the bin is.</summary>
    public const int Rows = 5;
    /// <summary>The belt's length, in tile lengths: a tile flips end over end this many times, less one, to reach the paddle.</summary>
    public const double Belt = 8;
    /// <summary>How many tiles the paddle holds.</summary>
    public const int Stack = 5;
}

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>
/// The controls: left or right along the lanes (-1, 0 or 1), flip the top tile into the bin, toss it back up the belt
/// (up), and hurry the belt (down).
/// </summary>
public readonly record struct Controls(int Move = 0, bool Flip = false, bool Toss = false, bool Hurry = false);

/// <summary>The tiles' colours. <see cref="Wild"/>, the flashing tile, counts as any of them.</summary>
public static class Colour
{
    public const int Empty = -1, Wild = 7, Count = 7;
    public static readonly string[] Names = ["RED", "YELLOW", "GREEN", "BLUE", "PINK", "ORANGE", "WHITE"];

    public static bool Matches(int cell, int colour) => cell == colour || cell == Wild && colour >= 0;
}

/// <summary>A tile on the belt: its lane, its colour, how far down the belt its far edge is, and whether it's going back up.</summary>
public sealed class Tile
{
    public int Lane, Colour;
    /// <summary>0 at the top of the belt; <see cref="Field.Belt"/> - 1 as it tips off the end onto the paddle.</summary>
    public double P;
    public bool Rising;
    public Tile Copy() => (Tile)MemberwiseClone();
}

/// <summary>Which way a line of tiles runs.</summary>
public enum Line { Vertical, Horizontal, Diagonal }

/// <summary>Three or more of a colour in a line: the cells, which way, and what it was worth.</summary>
public sealed record Klax(Line Line, int Colour, (int Col, int Row)[] Cells)
{
    public int Length => Cells.Length;
}

/// <summary>What a wave asks of you.</summary>
public enum GoalKind { Klaxes, Diagonals, Horizontals, Points, Tiles, Big }

public readonly record struct Goal(GoalKind Kind, int Count)
{
    /// <summary>What the wave card says.</summary>
    public string Text => Kind switch
    {
        GoalKind.Klaxes => $"MAKE {Count} KLAX{(Count == 1 ? "" : "ES")}",
        GoalKind.Diagonals => $"MAKE {Count} DIAGONAL{(Count == 1 ? "" : "S")}",
        GoalKind.Horizontals => $"MAKE {Count} HORIZONTAL{(Count == 1 ? "" : "S")}",
        GoalKind.Points => $"SCORE {Count:N0} POINTS",
        GoalKind.Tiles => $"SURVIVE {Count} TILES",
        _ => $"MAKE {Count} BIG KLAX{(Count == 1 ? "" : "ES")} (4 OR 5)",
    };

    /// <summary>The short name for the progress panel.</summary>
    public string Short => Kind switch
    {
        GoalKind.Klaxes => "KLAXES", GoalKind.Diagonals => "DIAGONALS", GoalKind.Horizontals => "HORIZONTALS",
        GoalKind.Points => "POINTS", GoalKind.Tiles => "TILES", _ => "BIG KLAXES",
    };
}

/// <summary>
/// Everything that moves or changes, kept together so it can be copied whole: the belt and its tiles, the paddle and
/// its stack, the bin, the drops, what the wave has done toward its goal, and the score.
/// </summary>
public sealed class World
{
    public int Wave = 1;
    public List<Tile> Tiles = [];
    /// <summary>Where the paddle is (in lanes, 0 to 4, between them while it moves), and where it's going.</summary>
    public double PadX = 2;
    public int PadTo = 2;
    /// <summary>The tiles on the paddle, bottom first.</summary>
    public List<int> Stack = [];
    /// <summary>The bin, by column and row; row 0 is the bottom. Tiles always sit on the bottom or on another tile.</summary>
    public int[,] Bin = NewBin();
    /// <summary>Cells flashing in a Klax, gone when <see cref="Clearing"/> runs out; and how deep in a chain this one is.</summary>
    public bool[,] Doomed = new bool[Field.Lanes, Field.Rows];
    public double Clearing;
    public int Chain;
    public int Drops;
    /// <summary>The wave's tallies, toward its goal.</summary>
    public int Klaxes, Diagonals, Horizontals, Verticals, Bigs, TilesSeen, WaveScore;
    public int Score;
    public double Clock, SpawnIn;
    /// <summary>How far the belt has run, in tile lengths (for its ribs on the screen).</summary>
    public double Run;
    public bool FlipHeld, TossHeld;
    public bool Lost, Done;
    public Dice Dice = new(1);
    public List<Sfx> Heard = [];
    /// <summary>For the screen: tiles just landed in the bin, tiles falling off the end, and words popping up.</summary>
    public List<(int Col, int Row, double T)> Landings = [];
    public List<(int Lane, int Colour, double T)> Falls = [];
    public List<(string Text, double X, double Y, double T)> Popups = [];
    /// <summary>The last Klaxes made, for the screen (the lines drawn through them).</summary>
    public List<Klax> Made = [];

    public static int[,] NewBin()
    {
        var b = new int[Field.Lanes, Field.Rows];
        for (int c = 0; c < Field.Lanes; c++)
        for (int r = 0; r < Field.Rows; r++)
            b[c, r] = Colour.Empty;
        return b;
    }

    public World Copy() => new()
    {
        Wave = Wave, Tiles = Tiles.Select(t => t.Copy()).ToList(), PadX = PadX, PadTo = PadTo, Stack = [.. Stack],
        Bin = (int[,])Bin.Clone(), Doomed = (bool[,])Doomed.Clone(), Clearing = Clearing, Chain = Chain, Drops = Drops,
        Klaxes = Klaxes, Diagonals = Diagonals, Horizontals = Horizontals, Verticals = Verticals, Bigs = Bigs,
        TilesSeen = TilesSeen, WaveScore = WaveScore, Score = Score, Clock = Clock, SpawnIn = SpawnIn, Run = Run,
        FlipHeld = FlipHeld, TossHeld = TossHeld, Lost = Lost, Done = Done, Dice = Dice, Made = [.. Made],
    };

    /// <summary>The lane the paddle is in (the nearest, while it moves).</summary>
    public int Lane => (int)Math.Round(PadX);

    /// <summary>How many tiles are in a column of the bin.</summary>
    public int Height(int col)
    {
        int h = 0;
        while (h < Field.Rows && Bin[col, h] != Colour.Empty) h++;
        return h;
    }

    public int Empties
    {
        get
        {
            int n = 0;
            foreach (int c in Bin) if (c == Colour.Empty) n++;
            return n;
        }
    }

    public Goal Goal => Sim.GoalOf(Wave);

    /// <summary>How far the wave has got toward its goal.</summary>
    public int Progress => Goal.Kind switch
    {
        GoalKind.Klaxes => Klaxes, GoalKind.Diagonals => Diagonals, GoalKind.Horizontals => Horizontals,
        GoalKind.Points => WaveScore, GoalKind.Tiles => TilesSeen, _ => Bigs,
    };
}
