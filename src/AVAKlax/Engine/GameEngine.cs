namespace AVAKlax.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>A new game: choose wave 1, or warp to 6 or 11 for a bonus.</summary>
    Select,
    /// <summary>The wave card: which wave, and what it asks of you.</summary>
    Start,
    Play,
    /// <summary>The drop meter full: a moment, then the wave again, if there's a try left.</summary>
    Lost,
    /// <summary>The goal met: the bonus for the empty squares counted up, then the next wave.</summary>
    Cleared,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Flip, Catch, Thunk, Toss, Drop, Blocked, Klax, Combo, SayKlax, Danger, Lost, WaveClear, Bonus, Warp, Select,
    SayStart,
    Music, MusicStop, TitleTune, GameOver,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 60;
    public const double HighScoreSeconds = 9;
    public const double SelectSeconds = 12, StartSeconds = 3, LostSeconds = 3, ClearedSeconds = 4, OverSeconds = 5;
    /// <summary>Tries: a full drop meter costs one, and the wave starts again.</summary>
    public const int Lives0 = 3;

    private double _accumulator, _idle, _flipTick, _bonusTick;
    private bool _startPrev, _tunePending = true, _music, _flipPrev;
    private int _movePrev;

    public GameEngine(HighScoreTable? table = null)
    {
        HighScores = table ?? HighScoreTable.Load();
        NewGame(select: false);
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    public int Lives { get; private set; }
    public int Score => World.Score;
    public int Wave => World.Wave;
    /// <summary>Which warp is picked on the wave select (an index into <see cref="Sim.Warps"/>).</summary>
    public int Choice { get; private set; }
    /// <summary>The last wave's bonus for its empty squares.</summary>
    public int Bonus { get; private set; }
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = null; }
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this), false);
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Music(false); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c, startPressed);
                break;
            case GameState.GameOver:
                if (StateTime > OverSeconds)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Wave);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    /// <summary>A fresh game: the wave select first, or (for the demonstration) straight into wave 1.</summary>
    private void NewGame(bool select)
    {
        World = new World();
        Lives = Lives0;
        Choice = 0;
        Bonus = 0;
        Sim.StartWave(World, 1);
        if (select) SetPhase(Phase.Select);
        else BeginWave(1);
    }

    private void BeginWave(int wave)
    {
        Sim.StartWave(World, wave);
        SetPhase(Phase.Start);
        Pilot.Reset();
    }

    public void StartGame()
    {
        NewGame(select: true);
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
        Play(Sfx.SayStart);
    }

    private void StartAttract()
    {
        NewGame(select: false);
        State = GameState.Attract;
        StateTime = 0;
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Music(false);
        Message = null;
        NewGame(select: false);
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Play) Music(true);
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void Simulate(double dt, Controls c, bool startPressed)
    {
        PhaseTime += dt;
        var w = World;
        bool flipPressed = c.Flip && !_flipPrev;
        int movePressed = c.Move != _movePrev ? Math.Sign(c.Move) : 0;
        _flipPrev = c.Flip;
        _movePrev = c.Move;
        switch (Phase)
        {
            case Phase.Select:
                if (movePressed != 0)
                {
                    int to = Math.Clamp(Choice + movePressed, 0, Sim.Warps.Length - 1);
                    if (to != Choice) { Choice = to; Play(Sfx.Select); }
                }
                if (flipPressed || startPressed || PhaseTime > SelectSeconds) Choose(Choice);
                break;
            case Phase.Start:
                if (PhaseTime > StartSeconds) { SetPhase(Phase.Play); Music(true); }
                break;
            case Phase.Play:
                Sim.Step(w, c);
                _flipTick -= dt;
                if (w.Done)
                {
                    SetPhase(Phase.Cleared);
                    Music(false);
                    Bonus = Sim.EmptyBonus(w);
                    w.Score += Bonus;
                }
                else if (w.Lost) { SetPhase(Phase.Lost); Music(false); }
                break;
            case Phase.Lost:
                if (PhaseTime > LostSeconds)
                {
                    Lives--;
                    if (Lives <= 0) { End(); return; }
                    BeginWave(w.Wave);
                }
                break;
            case Phase.Cleared:
                // The empty squares counted off, one by one.
                if (PhaseTime > 0.8 && PhaseTime < 0.8 + 0.06 * w.Empties && (_bonusTick -= dt) <= 0) { _bonusTick = 0.06; Play(Sfx.Bonus); }
                if (PhaseTime > ClearedSeconds) { BeginWave(w.Wave + 1); return; }
                break;
        }
        foreach (var s in w.Heard)
        {
            // The belt's tiles flip all the time: a knock now and then is enough.
            if (s == Sfx.Flip)
            {
                if (_flipTick > 0) continue;
                _flipTick = 0.16;
            }
            Play(s);
        }
        w.Heard.Clear();
    }

    /// <summary>Takes a warp from the wave select: its wave, and its bonus.</summary>
    public void Choose(int choice)
    {
        var (wave, bonus) = Sim.Warps[Math.Clamp(choice, 0, Sim.Warps.Length - 1)];
        Choice = choice;
        BeginWave(wave);
        World.Score += bonus;
        if (bonus > 0)
        {
            Play(Sfx.Warp);
            Message = $"WARP TO WAVE {wave}  +{bonus:N0}";
            MessageTime = StartSeconds;
        }
    }

    private void End()
    {
        Play(Sfx.GameOver);
        Music(false);
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into a wave, past the wave select and the wave card.</summary>
    public void Go(int wave = 1)
    {
        World = new World();
        Lives = Lives0;
        BeginWave(wave);
        State = GameState.Playing;
        SetPhase(Phase.Play);
        Message = null;
    }

    public void SetLives(int n) => Lives = n;
}
