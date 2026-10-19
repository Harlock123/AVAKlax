using Avalonia.Input;

namespace AVAKlax.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: left and right along the lanes, flip, toss and hurry.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update()
    {
        _pad.Update();
    }

    /// <summary>
    /// Left and right (or A and D) move the paddle a lane at a time, the last one pressed counting. Up (W) tosses
    /// the top tile back up the belt, the way the belt goes up the screen; down (S) hurries the belt toward you.
    /// Space, Z, X or Ctrl flips the top tile into the bin. On a gamepad: the stick or D-pad the same way, A or B to
    /// flip, and Y to toss.
    /// </summary>
    public Controls Player
    {
        get
        {
            var pad = _pad;
            int move = 0;
            foreach (var k in _held)
            {
                int m = k switch
                {
                    Key.Left or Key.A or Key.NumPad4 => -1,
                    Key.Right or Key.D or Key.NumPad6 => 1,
                    _ => 0,
                };
                if (m != 0) move = m;
            }
            if (move == 0) move = pad.DPadLeft || pad.LeftStickX < -0.5f ? -1 : pad.DPadRight || pad.LeftStickX > 0.5f ? 1 : 0;
            bool flip = Any(Key.Space, Key.Z, Key.X, Key.LeftCtrl, Key.RightCtrl) || pad.ButtonA || pad.ButtonB;
            bool toss = Any(Key.Up, Key.W, Key.NumPad8) || PadUp || pad.ButtonY;
            bool hurry = Any(Key.Down, Key.S, Key.NumPad2) || PadDown;
            return new Controls(move, flip, toss, hurry);
        }
    }

    /// <summary>Enter or 1.</summary>
    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
