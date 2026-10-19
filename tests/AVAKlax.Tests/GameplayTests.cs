using Avalonia.Input;
using AVAKlax.Engine;

namespace AVAKlax.Tests;

public class GameplayTests
{
    private const int R = 0, Y = 1, G = 2, B = 3, W = Colour.Wild, E = Colour.Empty;

    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaklax-tests-" + Guid.NewGuid().ToString("N"));

    private static GameEngine Engine() => new(HighScoreTable.Load(TempFolder())) { Silent = true };

    /// <summary>A wave with no new tiles coming.</summary>
    private static World Quiet(int wave = 1)
    {
        var w = new World();
        Sim.StartWave(w, wave);
        w.SpawnIn = 1e9;
        return w;
    }

    private static void Run(World w, double seconds, Controls c = default)
    {
        for (int i = 0; i < (int)Math.Round(seconds * 60); i++) Sim.Step(w, c);
    }

    private static Tile Put(World w, int lane, int colour, double p = 0)
    {
        var t = new Tile { Lane = lane, Colour = colour, P = p };
        w.Tiles.Add(t);
        return t;
    }

    /// <summary>
    /// Fills the bin from pictures of its rows, the top row first: R, Y, G, B for the colours, * for the wild tile,
    /// . for empty.
    /// </summary>
    private static void Fill(World w, params string[] rows)
    {
        w.Bin = World.NewBin();
        for (int k = 0; k < rows.Length; k++)
        {
            int r = rows.Length - 1 - k;
            var cells = rows[k].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int c = 0; c < cells.Length; c++)
                w.Bin[c, r] = cells[c] switch { "R" => R, "Y" => Y, "G" => G, "B" => B, "*" => W, _ => E };
        }
        Assert.Empty(Sim.FindKlaxes(w.Bin));
    }

    /// <summary>The paddle over a column, holding these tiles (the last on top), and the top one flipped in.</summary>
    private static void FlipInto(World w, int col, params int[] stack)
    {
        w.PadX = w.PadTo = col;
        w.Stack = [.. stack];
        Sim.Step(w, default);
        Sim.Step(w, new Controls(Flip: true));
        Sim.Step(w, default);
    }

    // ───────────────────────────── the belt and the paddle ─────────────────────────────

    [Fact]
    public void A_tile_tumbles_down_its_lane_one_flip_at_a_time_and_lands_on_the_paddle()
    {
        var w = Quiet();
        w.PadX = w.PadTo = 3;
        var t = Put(w, 3, B);
        double due = (Field.Belt - 1) / Sim.BeltSpeed(1);
        var seen = new HashSet<int>();
        for (int i = 0; i < (int)(due * 60) - 3; i++)
        {
            Sim.Step(w, default);
            seen.Add((int)Math.Floor(t.P));
        }
        Assert.Equal(Enumerable.Range(0, (int)Field.Belt - 1), seen.Order());
        Assert.Empty(w.Stack);
        Assert.Contains(Sfx.Flip, w.Heard);
        Run(w, 0.2);
        Assert.Equal([B], w.Stack);
        Assert.Empty(w.Tiles);
        Assert.Equal(1, w.TilesSeen);
        Assert.Equal(Sim.CatchPoints, w.Score);
        Assert.Contains(Sfx.Catch, w.Heard);
        Assert.Equal(0, w.Drops);
    }

    [Fact]
    public void The_paddle_moves_a_lane_a_tap_keeps_going_held_stops_at_the_side_and_can_turn_back_between()
    {
        var w = Quiet();
        Assert.Equal(2, w.PadX);
        Sim.Step(w, new Controls(1));
        Run(w, 0.5);
        Assert.Equal(3, w.PadX);
        Run(w, 1, new Controls(1));
        Assert.Equal(4, w.PadX);
        Run(w, 2, new Controls(-1));
        Assert.Equal(0, w.PadX);
        Run(w, 2.0 / 60, new Controls(1));
        Assert.True(w.PadX is > 0 and < 1);
        Run(w, 0.5, new Controls(-1));
        Run(w, 0.5);
        Assert.Equal(0, w.PadX);
    }

    [Fact]
    public void A_tile_the_paddle_is_not_under_is_a_drop_and_a_full_drop_meter_loses_the_wave()
    {
        var w = Quiet();
        w.PadX = w.PadTo = 0;
        int meter = Sim.DropMeter(1);
        for (int k = 0; k < meter; k++)
        {
            Assert.False(w.Lost);
            Put(w, 4, R, Field.Belt - 1.2);
            Run(w, 0.3);
            Assert.Equal(k + 1, w.Drops);
            Assert.Contains(Sfx.Drop, w.Heard);
            if (k == meter - 2) Assert.Contains(Sfx.Danger, w.Heard);
            w.Heard.Clear();
        }
        Assert.True(w.Lost);
        Assert.Empty(w.Stack);
        // A lost wave goes no further.
        double clock = w.Clock;
        Run(w, 1);
        Assert.Equal(clock, w.Clock);
    }

    [Fact]
    public void The_paddle_holds_five_tiles_and_a_sixth_falls_past_it()
    {
        var w = Quiet();
        for (int k = 0; k < 6; k++) Put(w, 2, k % 4, Field.Belt - 1.05 - k * 1.0);
        Run(w, 6);
        Assert.Equal(Field.Stack, w.Stack.Count);
        Assert.Equal([R, Y, G, B, R], w.Stack);
        Assert.Equal(1, w.Drops);
        Assert.Equal(6, w.TilesSeen);
    }

    [Fact]
    public void The_up_key_tosses_the_top_tile_back_up_the_belt_and_it_comes_down_again()
    {
        using var input = new InputHandler();
        var w = Quiet();
        w.Stack = [R, Y];
        input.OnKeyDown(Key.Up);
        Assert.True(input.Player.Toss);
        Sim.Step(w, input.Player);
        input.OnKeyUp(Key.Up);
        Assert.Equal([R], w.Stack);
        var t = Assert.Single(w.Tiles);
        Assert.Equal((2, Y), (t.Lane, t.Colour));
        Assert.True(t.Rising);
        Assert.Contains(Sfx.Toss, w.Heard);
        // Up the belt (up the screen) to the top, then down again.
        double p = t.P;
        Run(w, 0.3, input.Player);
        Assert.True(t.P < p);
        Run(w, 1.5, input.Player);
        Assert.False(t.Rising);
        Run(w, (Field.Belt + 1) / Sim.BeltSpeed(1), input.Player);
        Assert.Equal([R, Y], w.Stack);
        Assert.Equal(0, w.Drops);
    }

    [Fact]
    public void A_tossed_tile_turns_round_behind_a_tile_coming_down_and_cannot_be_tossed_under_one()
    {
        var w = Quiet();
        var down = Put(w, 2, G, 2);
        w.Stack = [R];
        Sim.Step(w, new Controls(Toss: true));
        var up = w.Tiles.Single(t => t != down);
        Run(w, 2);
        Assert.False(up.Rising);
        Assert.Equal(1, up.P - down.P, 3);
        // With a tile about to tip onto the paddle, nothing can go up that lane.
        w = Quiet();
        Put(w, 2, G, Field.Belt - 1.5);
        w.Stack = [R];
        Sim.Step(w, new Controls(Toss: true));
        Assert.Single(w.Tiles);
        Assert.Equal([R], w.Stack);
        Assert.Contains(Sfx.Blocked, w.Heard);
    }

    [Fact]
    public void The_down_key_hurries_the_belt()
    {
        using var input = new InputHandler();
        input.OnKeyDown(Key.Down);
        Assert.True(input.Player.Hurry);
        double Time(Controls c)
        {
            var w = Quiet();
            Put(w, 2, R);
            int n = 0;
            while (w.Stack.Count == 0 && n < 6000) { Sim.Step(w, c); n++; }
            return n / 60.0;
        }
        double slow = Time(default), quick = Time(input.Player);
        Assert.Equal(slow / Sim.Hurry, quick, 1);
    }

    // ───────────────────────────── the bin ─────────────────────────────

    [Fact]
    public void A_flip_drops_the_top_tile_to_the_bottom_of_the_column_or_onto_the_tiles_there_and_a_full_column_takes_no_more()
    {
        var w = Quiet();
        FlipInto(w, 1, R, Y);
        Assert.Equal(Y, w.Bin[1, 0]);
        Assert.Equal([R], w.Stack);
        Assert.Contains(Sfx.Thunk, w.Heard);
        // Holding the button flips one tile, not all of them.
        w.Stack = [G, B, R];
        Run(w, 0.5, new Controls(Flip: true));
        Assert.Equal(R, w.Bin[1, 1]);
        Assert.Equal(2, w.Height(1));
        Assert.Equal([G, B], w.Stack);
        Fill(w, "R . . . .", "Y . . . .", "R . . . .", "Y . . . .", "R . . . .");
        w.Heard.Clear();
        FlipInto(w, 0, G);
        Assert.Equal([G], w.Stack);
        Assert.Contains(Sfx.Blocked, w.Heard);
    }

    [Fact]
    public void The_bin_has_twenty_lines_of_three_or_more()
    {
        Assert.Equal(5, Sim.Lines.Count(l => l.Line == Line.Vertical));
        Assert.Equal(5, Sim.Lines.Count(l => l.Line == Line.Horizontal));
        Assert.Equal(10, Sim.Lines.Count(l => l.Line == Line.Diagonal));
        Assert.Equal(2, Sim.Lines.Count(l => l.Cells.Length == 5 && l.Line == Line.Diagonal));
    }

    public static TheoryData<string, int, int, Line, int> Lines() => new()
    {
        // The bin (the top row first), the column flipped into, the colour, which way the Klax goes, and its points.
        { ". . . . .|. . . . .|. . . . .|. . . . .|R R . . .", 2, R, Line.Horizontal, 1_000 },
        { ". . . . .|. . . . .|. . . . .|. . . . .|. . R R .", 1, R, Line.Horizontal, 1_000 },
        { ". . . . .|. . . . .|. . . . .|. . . G .|. . . G .", 3, G, Line.Vertical, 50 },
        { ". . . . .|. . . . .|. . . . .|. R Y . .|R Y G . .", 2, R, Line.Diagonal, 5_000 },
        { ". . . . .|. . . . .|B . . . .|Y B . . .|G Y . . .", 2, B, Line.Diagonal, 5_000 },
        { ". . . . .|. . . . .|. . . . .|. . . . .|Y Y . Y .", 2, Y, Line.Horizontal, 5_000 },
        { ". . . . .|. . . . .|. . . . .|. . . . .|B B . B B", 2, B, Line.Horizontal, 10_000 },
        { ". . . . G|. . . G R|. . . B B|. G Y R Y|G Y R B R", 2, G, Line.Diagonal, 20_000 },
    };

    [Theory]
    [MemberData(nameof(Lines))]
    public void Three_or_more_of_a_colour_in_a_row_a_column_or_a_diagonal_is_a_Klax(string bin, int col, int colour, Line line, int points)
    {
        var w = Quiet();
        Fill(w, bin.Split('|'));
        FlipInto(w, col, colour);
        var k = Assert.Single(w.Made);
        Assert.Equal(line, k.Line);
        Assert.Equal(colour, k.Colour);
        Assert.Equal(points, w.Score);
        Assert.Equal(1, w.Klaxes);
        Assert.Equal(line == Line.Diagonal ? 1 : 0, w.Diagonals);
        Assert.Equal(line == Line.Horizontal ? 1 : 0, w.Horizontals);
        Assert.Equal(k.Length >= 4 ? 1 : 0, w.Bigs);
        Assert.Contains(Sfx.Klax, w.Heard);
        Assert.Contains(Sfx.SayKlax, w.Heard);
        Assert.NotEmpty(w.Popups);
        // It flashes, then it's gone.
        foreach (var (c, r) in k.Cells) Assert.True(w.Doomed[c, r]);
        Run(w, Sim.ClearSeconds + 0.05);
        foreach (var (c, r) in k.Cells) Assert.NotEqual(colour, w.Bin[c, r]);
        Assert.Empty(Sim.FindKlaxes(w.Bin));
    }

    [Fact]
    public void Across_beats_up_and_down_a_diagonal_beats_both_and_longer_lines_beat_shorter()
    {
        for (int n = 3; n <= 5; n++)
        {
            Assert.True(Sim.KlaxPoints(Line.Horizontal, n) > Sim.KlaxPoints(Line.Vertical, n));
            Assert.True(Sim.KlaxPoints(Line.Diagonal, n) > Sim.KlaxPoints(Line.Horizontal, n));
            if (n > 3)
                foreach (var line in Enum.GetValues<Line>())
                    Assert.True(Sim.KlaxPoints(line, n) > Sim.KlaxPoints(line, n - 1));
        }
    }

    [Fact]
    public void The_wild_tile_matches_any_colour()
    {
        var w = Quiet();
        Fill(w, "R * . . .");
        FlipInto(w, 2, R);
        Assert.Equal(Line.Horizontal, Assert.Single(w.Made).Line);
        // Between two colours, it finishes both: two Klaxes at once.
        w = Quiet();
        Fill(w, "R R . G G");
        FlipInto(w, 2, W);
        Assert.Equal(2, w.Made.Count);
        Assert.Equal([R, G], w.Made.Select(k => k.Colour).Order());
        Assert.Equal(2 * (1_000 * 2), w.Score);
        Assert.Contains(Sfx.Combo, w.Heard);
        Run(w, Sim.ClearSeconds + 0.05);
        Assert.Equal(Field.Rows * Field.Lanes, w.Empties);
    }

    [Fact]
    public void Klaxes_made_at_once_are_a_combination_each_worth_as_many_times_as_there_are_of_them()
    {
        var w = Quiet();
        // The red lands at the top of the third column: three up, and three across.
        Fill(w, "R R . . .", "G G R . .", "Y Y R . .");
        FlipInto(w, 2, R);
        Assert.Equal([Line.Vertical, Line.Horizontal], w.Made.Select(k => k.Line).Order());
        Assert.Equal((50 + 1_000) * 2, w.Score);
        Assert.Equal(2, w.Klaxes);
        Assert.Contains(w.Popups, p => p.Text.Contains("COMBINATION"));
    }

    [Fact]
    public void Tiles_above_a_Klax_fall_into_the_gap_and_a_Klax_that_makes_is_a_chain_worth_twice()
    {
        var w = Quiet();
        // Greens across the second row; above the first green, two blues that fall onto the blue below.
        Fill(w, "B . . . .", "B . . . .", "G G . . .", "B Y Y . .");
        FlipInto(w, 2, G);
        Assert.Equal(Line.Horizontal, Assert.Single(w.Made).Line);
        Assert.Equal(1, w.Chain);
        int score = w.Score;
        Run(w, Sim.ClearSeconds + 0.02);
        Assert.Equal([B, B, B], [w.Bin[0, 0], w.Bin[0, 1], w.Bin[0, 2]]);
        Assert.Equal(2, w.Chain);
        Assert.Equal(Line.Vertical, Assert.Single(w.Made).Line);
        Assert.Equal(score + 50 * 2, w.Score);
        Assert.Contains(w.Popups, p => p.Text.Contains("CHAIN"));
        Assert.Contains(Sfx.Combo, w.Heard);
        Run(w, Sim.ClearSeconds + 0.02);
        Assert.Equal(0, w.Chain);
        Assert.Equal(E, w.Bin[0, 0]);
        Assert.Equal(2, w.Klaxes);
    }

    [Fact]
    public void While_a_Klax_flashes_nothing_more_goes_in()
    {
        var w = Quiet();
        Fill(w, "R R . . .");
        FlipInto(w, 2, Y, R);
        Assert.True(w.Clearing > 0);
        FlipInto(w, 4, Y);
        Assert.Equal(E, w.Bin[4, 0]);
        Run(w, Sim.ClearSeconds);
        FlipInto(w, 4, Y);
        Assert.Equal(Y, w.Bin[4, 0]);
    }

    // ───────────────────────────── the waves ─────────────────────────────

    [Fact]
    public void Each_wave_has_a_goal_and_meeting_it_ends_the_wave_once_the_Klax_has_gone()
    {
        Assert.Equal(new Goal(GoalKind.Klaxes, 3), Sim.GoalOf(1));
        Assert.Equal(new Goal(GoalKind.Diagonals, 2), Sim.GoalOf(3));
        Assert.Equal(new Goal(GoalKind.Points, 10_000), Sim.GoalOf(4));
        Assert.Equal(new Goal(GoalKind.Tiles, 30), Sim.GoalOf(2));
        Assert.Equal("MAKE 2 DIAGONALS", Sim.GoalOf(3).Text);
        Assert.Equal("SURVIVE 40 TILES", new Goal(GoalKind.Tiles, 40).Text);
        Assert.True(Sim.GoalOf(18).Count > Sim.GoalOf(3).Count);
        Assert.Equal(Sim.GoalOf(3).Kind, Sim.GoalOf(18).Kind);
        // Wave 3: two diagonals. Across doesn't count.
        var w = Quiet(3);
        Fill(w, "R R . . .");
        FlipInto(w, 2, R);
        Run(w, 1);
        Assert.Equal(0, w.Progress);
        Fill(w, ". R Y . .", "R Y G . .");
        FlipInto(w, 2, R);
        Run(w, 1);
        Assert.Equal(1, w.Progress);
        Assert.False(w.Done);
        Fill(w, ". R Y . .", "R Y G . .");
        FlipInto(w, 2, R);
        Assert.False(w.Done); // not while it flashes
        Run(w, Sim.ClearSeconds);
        Assert.True(w.Done);
        Assert.Contains(Sfx.WaveClear, w.Heard);
        // Wave 2: survive thirty tiles.
        w = Quiet(2);
        w.TilesSeen = 29;
        Put(w, 2, R, Field.Belt - 1.1);
        Run(w, 0.2);
        Assert.True(w.Done);
        // Wave 4: points made in the wave, not before it.
        w = Quiet(4);
        w.Score = 50_000;
        Fill(w, ". R Y . .", "R Y G . .");
        FlipInto(w, 2, R);
        Run(w, 1);
        Assert.Equal(5_000, w.Progress);
        Assert.False(w.Done);
        // Wave 10: a Klax of four or more.
        w = Quiet(10);
        Fill(w, "B B . . .");
        FlipInto(w, 2, B);
        Run(w, 1);
        Assert.False(w.Done);
        Fill(w, "B B . B .");
        FlipInto(w, 2, B);
        Run(w, 1);
        Assert.True(w.Done);
    }

    [Fact]
    public void Later_waves_are_quicker_with_more_colours_the_wild_tile_and_a_shorter_drop_meter()
    {
        Assert.True(Sim.BeltSpeed(11) > Sim.BeltSpeed(6) && Sim.BeltSpeed(6) > Sim.BeltSpeed(1));
        Assert.True(Sim.SpawnEvery(11) < Sim.SpawnEvery(1));
        Assert.Equal([4, 5, 6, 7], new[] { 1, 6, 11, 16 }.Select(Sim.Colours));
        Assert.Equal([5, 4, 3], new[] { 5, 6, 11 }.Select(Sim.DropMeter));
        HashSet<int> Colours(int wave)
        {
            var w = new World();
            Sim.StartWave(w, wave);
            var seen = new HashSet<int>();
            for (int i = 0; i < 60 * 600; i++)
            {
                Sim.Step(w, default);
                foreach (var t in w.Tiles) seen.Add(t.Colour);
                w.Tiles.Clear();
            }
            return seen;
        }
        Assert.Equal([R, Y, G, B], Colours(1).Order());
        Assert.Equal([0, 1, 2, 3, 4, 5, W], Colours(11).Order());
    }

    [Fact]
    public void The_paddle_standing_still_loses_the_wave()
    {
        var w = new World();
        Sim.StartWave(w, 1);
        Run(w, 120);
        Assert.True(w.Lost);
        Assert.Equal(Sim.DropMeter(1), w.Drops);
    }

    [Fact]
    public void A_copied_world_goes_on_just_as_the_original()
    {
        var a = new World();
        Sim.StartWave(a, 7);
        var p = new Pilot();
        for (int i = 0; i < 1200; i++) Sim.Step(a, p.Decide(a));
        var b = a.Copy();
        var pa = new Pilot();
        var pb = new Pilot();
        for (int i = 0; i < 1200; i++) { Sim.Step(a, pa.Decide(a)); Sim.Step(b, pb.Decide(b)); }
        Assert.Equal(a.Score, b.Score);
        Assert.Equal(a.Stack, b.Stack);
        Assert.Equal(a.Bin, b.Bin);
        Assert.Equal(a.Tiles.Select(t => (t.Lane, t.P, t.Colour)), b.Tiles.Select(t => (t.Lane, t.P, t.Colour)));
    }

    // ───────────────────────────── the computer player ─────────────────────────────

    private static bool PilotPlays(int wave, out World w)
    {
        w = new World();
        Sim.StartWave(w, wave);
        var p = new Pilot();
        for (int t = 0; t < 60 * 60 * 4 && !w.Done && !w.Lost; t++) Sim.Step(w, p.Decide(w));
        return w.Done;
    }

    [Fact]
    public void The_computer_player_meets_the_goals_of_the_first_ten_waves_without_a_drop()
    {
        for (int wave = 1; wave <= 10; wave++)
        {
            Assert.True(PilotPlays(wave, out var w), $"wave {wave} ({w.Goal.Text}): {w.Progress} with {w.Drops} drops");
            Assert.Equal(0, w.Drops);
        }
    }

    [Fact]
    public void The_computer_player_meets_the_goals_from_the_warp_to_wave_eleven()
    {
        for (int wave = 11; wave <= 15; wave++)
            Assert.True(PilotPlays(wave, out var w), $"wave {wave} ({w.Goal.Text}): {w.Progress} with {w.Drops} drops");
    }

    // ───────────────────────────── the game ─────────────────────────────

    [Fact]
    public void A_new_game_starts_with_the_wave_select_and_a_warp_brings_its_bonus()
    {
        var g = Engine();
        g.StartGame();
        Assert.Equal(Phase.Select, g.Phase);
        g.Tick(new Controls(1));
        g.Tick(default);
        Assert.Equal(1, g.Choice);
        g.Tick(new Controls(Flip: true));
        Assert.Equal(Phase.Start, g.Phase);
        Assert.Equal(6, g.Wave);
        Assert.Equal(50_000, g.Score);
        Assert.Contains("WARP", g.Message);
        g.Run((int)(60 * GameEngine.StartSeconds) + 5);
        Assert.Equal(Phase.Play, g.Phase);
        // Left alone, the select starts at wave 1.
        g = Engine();
        g.StartGame();
        g.Idle((int)(60 * GameEngine.SelectSeconds) + 5);
        Assert.Equal((1, 0, Phase.Start), (g.Wave, g.Score, g.Phase));
    }

    [Fact]
    public void A_wave_done_pays_for_its_empty_squares_and_the_next_one_starts_with_an_empty_bin()
    {
        var g = Engine();
        g.Go(1);
        Fill(g.World, "R Y G B R");
        g.World.Klaxes = 3;
        g.Tick(default);
        Assert.Equal(Phase.Cleared, g.Phase);
        Assert.Equal(20 * Sim.EmptyPoints, g.Bonus);
        Assert.Equal(20 * Sim.EmptyPoints, g.Score);
        g.Run((int)(60 * GameEngine.ClearedSeconds) + 5);
        Assert.Equal((2, Phase.Start), (g.Wave, g.Phase));
        Assert.Equal(25, g.World.Empties);
        Assert.Equal(20 * Sim.EmptyPoints, g.Score);
    }

    [Fact]
    public void A_full_drop_meter_costs_a_try_and_the_wave_starts_again_then_the_end_and_a_high_score()
    {
        var g = Engine();
        g.Go(4);
        g.World.Score = 123_456;
        Fill(g.World, "R Y G B R");
        g.World.Drops = Sim.DropMeter(4) - 1;
        g.World.PadX = g.World.PadTo = 0;
        Put(g.World, 4, R, Field.Belt - 1.1);
        g.Run(20);
        Assert.Equal(Phase.Lost, g.Phase);
        g.Run((int)(60 * GameEngine.LostSeconds) + 5);
        Assert.Equal((4, Phase.Start, GameEngine.Lives0 - 1), (g.Wave, g.Phase, g.Lives));
        Assert.Equal(25, g.World.Empties);
        Assert.Equal(0, g.World.Drops);
        Assert.Equal(123_456, g.Score);
        g.SetLives(1);
        g.Run((int)(60 * GameEngine.StartSeconds) + 5);
        g.World.Drops = Sim.DropMeter(4) - 1;
        g.World.PadX = g.World.PadTo = 0;
        Put(g.World, 4, R, Field.Belt - 1.1);
        g.Run(20);
        g.Run((int)(60 * GameEngine.LostSeconds) + 5);
        Assert.Equal(GameState.GameOver, g.State);
        g.Run((int)(60 * GameEngine.OverSeconds) + 5);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "KLX") g.Initials!.Type(ch);
        g.Tick(default);
        Assert.Equal(GameState.HighScores, g.State);
        var entry = g.HighScores.Entries[g.NewEntryIndex];
        Assert.Equal(("KLX", 123_456, 4), (entry.Initials, entry.Score, entry.Stage));
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_then_the_title()
    {
        var g = Engine();
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        Assert.Equal(Phase.Start, g.Phase);
        g.Idle((int)(60 * GameEngine.AttractSeconds) - 60);
        Assert.Equal(GameState.Attract, g.State);
        Assert.True(g.World.TilesSeen > 10, "the computer player is catching tiles");
        Assert.Equal(0, g.World.Drops);
        g.Idle(90);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
    }

    [Fact]
    public void Every_sound_the_game_asks_for_has_its_file()
    {
        foreach (var s in Enum.GetValues<Sfx>().Where(s => s != Sfx.MusicStop))
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Sounds", s + ".wav")), $"no {s}.wav");
    }

    [Fact]
    public void The_keys_the_last_of_left_and_right_counts_space_flips_up_tosses_and_down_hurries()
    {
        using var input = new InputHandler();
        input.OnKeyDown(Key.Right);
        Assert.Equal(1, input.Player.Move);
        input.OnKeyDown(Key.Left);
        Assert.Equal(-1, input.Player.Move);
        input.OnKeyUp(Key.Left);
        Assert.Equal(1, input.Player.Move);
        input.OnKeyUp(Key.Right);
        Assert.Equal(new Controls(), input.Player);
        input.OnKeyDown(Key.Space);
        Assert.Equal(new Controls(Flip: true), input.Player);
        input.OnKeyUp(Key.Space);
        input.OnKeyDown(Key.W);
        Assert.Equal(new Controls(Toss: true), input.Player);
        input.OnKeyUp(Key.W);
        input.OnKeyDown(Key.S);
        Assert.Equal(new Controls(Hurry: true), input.Player);
        // Through the game: right moves the paddle right.
        input.OnKeyUp(Key.S);
        var w = Quiet();
        input.OnKeyDown(Key.D);
        Sim.Step(w, input.Player);
        input.OnKeyUp(Key.D);
        Run(w, 0.3, input.Player);
        Assert.Equal(3, w.PadX);
    }
}
