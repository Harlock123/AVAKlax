using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAKlax.Engine;
using AVAKlax.Views;
using Controls = AVAKlax.Engine.Controls;

namespace AVAKlax.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set KLAX_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaklax-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("KLAX_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAKlax-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>A bin part filled, as it would be some way into a wave: tiles at random, but no Klax among them.</summary>
    private static void Clutter(World w, int tiles, int seed)
    {
        var dice = new Dice(seed);
        int placed = 0;
        for (int tries = 0; placed < tiles && tries < 5000; tries++)
        {
            int c = dice.Next(Field.Lanes), h = w.Height(c);
            if (h >= Field.Rows - 1) continue;
            w.Bin[c, h] = dice.Next(Sim.Colours(w.Wave));
            if (Sim.FindKlaxes(w.Bin).Count > 0) w.Bin[c, h] = Colour.Empty;
            else placed++;
        }
    }

    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 600)
    {
        for (int t = 0; t < 60 * maxSeconds && g.State == GameState.Playing; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    private static void Moment(int wave, int clutter, int score, string name, Func<GameEngine, bool> moment)
    {
        var (w, c, g) = Open();
        g.Go(wave);
        g.SetLives(3);
        g.World.Score = score;
        Clutter(g.World, clutter, wave * 7 + clutter);
        Assert.True(PlayUntil(g, c, moment), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("AVA", 211_850, 13);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 600 && g.State != GameState.HighScores; i++) g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        c.Advance(1.5);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void The_wave_card()
    {
        var (w, c, g) = Open();
        g.StartGame();
        g.Tick(new Controls(1));
        g.Tick(default);
        g.Tick(new Controls(Flip: true));
        Assert.Equal((6, Phase.Start), (g.Wave, g.Phase));
        for (int i = 0; i < 40; i++) { g.Tick(default); c.Advance(1 / 60.0); }
        Shot(w, c, "wave-card");
    }

    [AvaloniaFact]
    public void Play_with_a_bin_filling_up() => Moment(4, 13, 21_340, "play", g =>
        g.PhaseTime > 6 && g.World.Clearing <= 0 && g.World.Stack.Count >= 2 && g.World.Tiles.Count >= 3 && g.World.Empties <= 13
        && g.World.Tiles.Any(t => t.P % 1 is > 0.35 and < 0.65));

    [AvaloniaFact]
    public void A_Klax() => Moment(3, 9, 14_785, "klax", g =>
        g.World.Clearing is > 0.25 and < 0.33 && g.World.Made.Any(k => k.Line == Line.Diagonal) && g.World.Tiles.Count >= 2);

    [AvaloniaFact]
    public void A_later_wave() => Moment(12, 11, 236_910, "wave-12", g =>
        g.PhaseTime > 8 && g.World.Clearing <= 0 && g.World.Tiles.Count >= 3 && g.World.Stack.Count >= 2
        && g.World.Tiles.Any(t => t.Colour == Colour.Wild && t.P > 2) && g.World.Tiles.Select(t => t.Colour).Distinct().Count() >= 3);

    [AvaloniaFact]
    public void Game_over()
    {
        var (w, c, g) = Open();
        g.Go(5);
        g.SetLives(1);
        g.World.Score = 48_270;
        Clutter(g.World, 14, 5);
        // The paddle left where it is: the drop meter soon fills.
        for (int i = 0; i < 60 * 120 && g.State == GameState.Playing; i++)
        {
            g.Tick(default);
            if (i % 3 == 0) c.Advance(0.05);
        }
        for (int i = 0; i < 90; i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Assert.Equal(GameState.GameOver, g.State);
        Shot(w, c, "game-over");
    }
}
