using Avalonia;
using Avalonia.Media;
using AVAKlax.Engine;

namespace AVAKlax.Views;

public sealed partial class GameCanvas
{
    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawScene(dc, g.World, false); DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawScene(dc, g.World, false); DrawInitials(dc); break;
            case GameState.GameOver: DrawScene(dc, g.World, false); DrawGameOver(dc); break;
            default:
                DrawScene(dc, g.World, g.Phase == Phase.Play);
                switch (g.Phase)
                {
                    case Phase.Select: DrawSelect(dc); break;
                    case Phase.Start: DrawWaveCard(dc, g.World); break;
                    case Phase.Lost: DrawLost(dc); break;
                    case Phase.Cleared: DrawCleared(dc); break;
                }
                if (g.Message is { } msg && g.Phase != Phase.Start)
                {
                    dc.FillRectangle(Brush(0xC0000000), new Rect(0, 372, Field.Width, 64));
                    Text(dc, msg, new Point(512, 404), 46, Yellow, center: true);
                }
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0xD0000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, 80), 24, Yellow, center: true);
    }

    private static void Card(DrawingContext dc, Rect r)
    {
        dc.FillRectangle(Brush(0xE80A0C18), r, 14);
        dc.DrawRectangle(null, Pen(0xFF8890B8, 3), r, 14, 14);
        dc.DrawRectangle(null, Pen(0xFF3A4062, 2), r.Deflate(6), 10, 10);
    }

    /// <summary>A word spelt on tiles that each turn over now and then, the way the title does it.</summary>
    private void TileWord(DrawingContext dc, string word, double cx, double y, double size, int salt)
    {
        double step = size * 1.08;
        for (int i = 0; i < word.Length; i++)
        {
            if (word[i] == ' ') continue;
            double x = cx + (i - (word.Length - 1) / 2.0) * step;
            // Every few seconds a ripple runs along the word: each tile in its turn squashed to an edge and back,
            // showing its other colour.
            double run = _clock * 0.9 - i * 0.1 + salt * 0.37 + 10;
            double turn = run % 4;
            double squash = turn < 0.36 ? Math.Abs(Math.Cos(turn / 0.36 * Math.PI)) : 1;
            int colour = (i + salt + (int)(run / 4) + (turn < 0.18 ? 0 : 1)) % 6;
            double hw = size / 2, hh = size / 2 * Math.Max(0.06, squash);
            var q = new[] { new Point(x - hw, y - hh), new Point(x + hw, y - hh), new Point(x + hw, y + hh), new Point(x - hw, y + hh) };
            uint c = TileColours[colour];
            Poly(dc, Shade(c, 0.4), null, 0, q[3], q[2], q[2] + new Point(0, size * 0.1), q[3] + new Point(0, size * 0.1));
            Face(dc, [q[0], q[1], q[2], q[3]], c, 1, false);
            if (squash > 0.5)
                Text(dc, word[i].ToString(), new Point(x, y - size * 0.06), size * 1.05 * squash, 0xFF101018, center: true, shadow: false);
        }
    }

    // ───────────────────────────── the title ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        // The belt running behind the title, tiles tumbling down it.
        DrawBackdrop(dc, 1);
        DrawBelt(dc, _clock * 1.5);
        for (int lane = 0; lane < Field.Lanes; lane++)
        for (int k = 0; k < 2; k++)
        {
            double p = (_clock * 1.5 + Hash(lane, k, 3) % 70 / 10.0 + k * 3.5) % (Field.Belt - 1);
            DrawBeltTile(dc, lane, p, (int)(Hash(lane, k + (int)((_clock * 1.5 + Hash(lane, k, 3) % 70 / 10.0 + k * 3.5) / (Field.Belt - 1)), 5) % 6), lane + k);
        }
        dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
        Card(dc, new Rect(cx - 440, 70, 880, 560));
        Text(dc, "A V A", new Point(cx, 112), 40, Dim, center: true);
        TileWord(dc, "KLAX", cx, 196, 104, 0);
        Text(dc, "AFTER ATARI GAMES' 1989 ARCADE PUZZLE GAME", new Point(cx, 292), 28, White, center: true);
        Text(dc, "IT IS 2026. PUT DOWN THE PHONE, AND PLAY KLAX.", new Point(cx, 330), 30, Cyan, center: true);
        Text(dc, "TILES TUMBLE DOWN THE BELT. CATCH THEM ON THE PADDLE (IT HOLDS FIVE), AND", new Point(cx, 384), 23, White, center: true);
        Text(dc, "FLIP THEM INTO THE BIN. THREE OR MORE OF A COLOUR IN A ROW, A COLUMN OR A", new Point(cx, 410), 23, White, center: true);
        Text(dc, "DIAGONAL IS A KLAX. ACROSS BEATS UP AND DOWN; A DIAGONAL BEATS BOTH. MISS", new Point(cx, 436), 23, White, center: true);
        Text(dc, "TOO MANY AND THE DROP METER FILLS. MEET EACH WAVE'S GOAL TO GO ON.", new Point(cx, 462), 23, White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER TO START", new Point(cx, 530), 46, Yellow, center: true);
        Text(dc, "WAVES 6 AND 11 CAN BE WARPED TO FOR A BONUS", new Point(cx, 590), 22, Dim, center: true);
        dc.FillRectangle(Brush(0xD0000000), new Rect(0, 700, Field.Width, 68));
        Text(dc, "LEFT/RIGHT: MOVE   SPACE, Z OR X: FLIP INTO THE BIN   UP: TOSS BACK   DOWN: HURRY   P: PAUSE", new Point(cx, 718), 19, 0xFFE0D0B0, center: true);
        Text(dc, "MUSIC: RONDO ALLA TURCA (MOZART, 1783) AND A TUNE OF ITS OWN", new Point(cx, 746), 18, Dim, center: true);
    }

    // ───────────────────────────── the waves ─────────────────────────────

    private void DrawSelect(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xA0000000), new Rect(0, 0, Field.Width, Field.Height));
        Card(dc, new Rect(cx - 400, 130, 800, 440));
        Text(dc, "CHOOSE YOUR WAVE", new Point(cx, 180), 58, Yellow, center: true);
        for (int i = 0; i < Sim.Warps.Length; i++)
        {
            var (wave, bonus) = Sim.Warps[i];
            var r = new Rect(cx - 360 + i * 245, 240, 230, 220);
            bool on = i == g.Choice;
            dc.FillRectangle(Brush(on ? 0xFF2A3050 : 0xFF141828), r, 12);
            dc.DrawRectangle(null, Pen(on && (int)(_clock * 4) % 2 == 0 ? 0xFFFFE14A : on ? 0xFFFFFFFF : 0xFF3A4062, on ? 4 : 2), r, 12, 12);
            Text(dc, "WAVE", new Point(r.Center.X, r.Y + 34), 34, Dim, center: true);
            TileWord(dc, wave.ToString(), r.Center.X, r.Y + 98, 64, i * 2 + 1);
            Text(dc, bonus > 0 ? $"+{bonus:N0}" : "FROM THE START", new Point(r.Center.X, r.Y + 166), bonus > 0 ? 38 : 28, bonus > 0 ? Yellow : White, center: true);
            Text(dc, $"{Sim.Colours(wave)} COLOURS", new Point(r.Center.X, r.Y + 198), 22, Dim, center: true);
        }
        Text(dc, "LEFT/RIGHT TO CHOOSE, SPACE OR ENTER TO GO", new Point(cx, 500), 28, White, center: true);
        Text(dc, $"{Math.Max(0, GameEngine.SelectSeconds - g.PhaseTime):0}", new Point(cx, 540), 30, Orange, center: true);
    }

    private void DrawWaveCard(DrawingContext dc, World w)
    {
        double cx = Field.Width / 2;
        var r = new Rect(cx - 300, 140, 600, Engine.Message != null ? 390 : 340);
        Card(dc, r);
        TileWord(dc, $"WAVE {w.Wave}", cx, 222, 62, w.Wave);
        Text(dc, w.Goal.Text, new Point(cx, 310), 44, Yellow, center: true);
        Text(dc, $"DROP METER {Sim.DropMeter(w.Wave)}   TRIES {Engine.Lives}", new Point(cx, 362), 28, White, center: true);
        int n = Sim.Colours(w.Wave);
        for (int k = 0; k < n; k++)
            Face(dc, [new Point(cx - n * 22 + k * 44 + 4, 392), new Point(cx - n * 22 + k * 44 + 40, 392), new Point(cx - n * 22 + k * 44 + 40, 424), new Point(cx - n * 22 + k * 44 + 4, 424)], TileColours[k], 1, false);
        Text(dc, w.Wave >= Sim.WildFrom ? "AND THE WILD TILE, WHICH MATCHES ANY COLOUR" : $"{n} COLOURS", new Point(cx, 452), 24, Dim, center: true);
        if (Engine.Message is { } msg) Text(dc, msg, new Point(cx, 496), 38, Cyan, center: true);
    }

    private void DrawLost(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        Card(dc, new Rect(cx - 320, 190, 640, 200));
        Text(dc, "THE DROP METER IS FULL", new Point(cx, 250), 52, (int)(_clock * 4) % 2 == 0 ? Red : White, center: true);
        int left = Engine.Lives - 1;
        Text(dc, left > 0 ? $"THE WAVE AGAIN: {left} {(left == 1 ? "TRY" : "TRIES")} LEFT" : "NO TRIES LEFT", new Point(cx, 320), 34, White, center: true);
    }

    private void DrawCleared(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        double cx = Field.Width / 2;
        Card(dc, new Rect(cx - 320, 160, 640, 280));
        Text(dc, $"WAVE {w.Wave} DONE!", new Point(cx, 216), 60, Yellow, center: true);
        int empties = w.Empties;
        int shown = Math.Clamp((int)((g.PhaseTime - 0.8) / 0.06), 0, empties);
        Text(dc, $"EMPTY SQUARES {shown} x {Sim.EmptyPoints}", new Point(cx, 290), 36, White, center: true);
        Text(dc, $"BONUS {shown * Sim.EmptyPoints:N0}", new Point(cx, 340), 44, Cyan, center: true);
        Text(dc, $"NEXT: {Sim.GoalOf(w.Wave + 1).Text}", new Point(cx, 400), 26, Dim, center: true);
    }

    // ───────────────────────────── game over and scores ─────────────────────────────

    private void DrawGameOver(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
        Card(dc, new Rect(cx - 360, 200, 720, 260));
        Text(dc, "GAME OVER", new Point(cx, 270), 84, (int)(_clock * 4) % 2 == 0 ? Yellow : White, center: true);
        Text(dc, $"SCORE {g.Score:N0}", new Point(cx, 350), 46, White, center: true);
        Text(dc, $"WAVE {g.Wave}: {g.World.Progress:N0} OF {g.World.Goal.Count:N0} {g.World.Goal.Short}", new Point(cx, 404), 30, Cyan, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
        Card(dc, new Rect(cx - 330, 70, 660, 640));
        TileWord(dc, "HIGH SCORES", cx, 128, 44, 3);
        Text(dc, "RANK NAME     SCORE   WAVE", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,9:N0}   {e.Stage,4}", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
        Card(dc, new Rect(cx - 330, 140, 660, 460));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString("N0"), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
