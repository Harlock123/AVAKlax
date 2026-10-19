using AVAKlax.Engine;

namespace AVAKlax.Tests;

/// <summary>
/// How the computer player gets on (set KLAX_PROBE to run it; KLAX_FROM and KLAX_WAVES pick the waves). KLAX_PROBE=lazy
/// just catches what comes and flips it straight in.
/// </summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_player_does()
    {
        if (Environment.GetEnvironmentVariable("KLAX_PROBE") is not { } mode) return;
        int from = int.TryParse(Environment.GetEnvironmentVariable("KLAX_FROM"), out int f) ? f : 1;
        int waves = int.TryParse(Environment.GetEnvironmentVariable("KLAX_WAVES"), out int n) ? n : 10;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var p = new Pilot();
        int lost = 0, cleared = 0;
        for (int wave = from; wave < from + waves; wave++)
        {
            var w = new World();
            Sim.StartWave(w, wave);
            p.Reset();
            int t = 0;
            bool held = false;
            for (; t < 60 * 60 * 5 && !w.Done && !w.Lost; t++)
            {
                Controls c;
                if (mode == "lazy")
                {
                    // Under the next tile, and everything straight into the bin.
                    var next = w.Tiles.Where(x => !x.Rising).OrderByDescending(x => x.P).FirstOrDefault();
                    int to = next?.Lane ?? 2;
                    held = !held && w.Stack.Count > 0;
                    c = new Controls(Math.Sign(to - w.PadX), held);
                }
                else c = p.Decide(w);
                Sim.Step(w, c);
            }
            if (w.Done) cleared++;
            else lost++;
            output.WriteLine($"wave {wave,2} {w.Goal.Text,-34} {(w.Done ? "done" : w.Lost ? "LOST" : "timed out"),-9} in {t / 60.0,5:0}s: " +
                             $"{w.Drops} drops, {w.Klaxes} klaxes ({w.Horizontals} across, {w.Verticals} up, {w.Diagonals} diagonal, {w.Bigs} big), " +
                             $"{w.TilesSeen} tiles, {w.WaveScore:N0} points, {w.Empties} empty");
        }
        output.WriteLine($"{cleared} done, {lost} lost; {sw.ElapsedMilliseconds} ms");
    }
}
