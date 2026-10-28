# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A tile-matching puzzle game in the style of Atari Games' 1989 arcade game Klax, in C# on .NET 10 and Avalonia 12,
drawn with Avalonia's `DrawingContext`. One self-contained executable per platform (Windows, macOS and Linux, x64
and ARM). The graphics are drawn in code, and the sounds, voice and music are synthesised by
`tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAKlax.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAKlax.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVAKlax          # play it; --no-audio for silence
KLAX_SHOTS=$PWD/Images dotnet test tests/AVAKlax.Tests --filter ScreenshotTests   # refresh the README's pictures
KLAX_PROBE=1 dotnet test tests/AVAKlax.Tests --filter PilotProbe --logger "console;verbosity=detailed"
                                          # how the computer player gets on; KLAX_FROM and KLAX_WAVES pick the
                                          # waves (1 and 10 by default); KLAX_PROBE=lazy flips everything straight in
python3 tools/make_sounds.py              # rebuild src/AVAKlax/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAKlax/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores), the phases of a wave (from the
    wave select on) and the fixed-step loop.
  - `Sim.cs` steps the world one 60th of a second at a time: the belt, the paddle, the bin, Klaxes and goals.
  - `World.cs` has the screen's measures (`Field`), `Dice` (the copyable random source), `Controls`, the tiles,
    the goals and the world's state.
  - `Pilot.cs` is the computer player, used by the demonstration and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAKlax/Views/`: `GameCanvas` (split over partial files) draws the 1024x768 frame, scaled to the window,
  and runs the 60 Hz timer. `GameCanvas.Screens.cs` draws the title and the other screens between waves.
- `src/AVAKlax/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVAKlax.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`. `PilotProbe.cs` is a tuning report, not a
  check.
- `tools/`: `make_sounds.py` writes the sounds; `speech.py` is the small formant speech synthesiser it uses.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` turns frame time into fixed 1/60 s steps (`Field.Step`), and `Sim.Step`
  advances the world by one. Each wave seeds its own `Dice`, so tests can play waves deterministically. Draw
  random numbers from the world's `Dice` (`w.Dice`), never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the world and draws it.
- **Input goes through `InputHandler`.** `Controls` has no up/down axis: up (W, stick up, Y) is `Toss`, down is
  `Hurry`, and left and right give `Move`. Keys and stick must agree. Test new controls through `InputHandler`,
  not by setting `Controls` directly.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder
  (`SpecialFolder.ApplicationData`, `AVAKlax`); tests load the table from a fresh temporary folder and run with
  `Silent = true`.
- **Line endings.** The sources are LF; `app.manifest` is CRLF. Keep each file's existing line endings so diffs
  show only real changes.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64, kept as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
A release also asks the portfolio site to rebuild, if the `SITE_REFRESH_TOKEN` secret is set.
