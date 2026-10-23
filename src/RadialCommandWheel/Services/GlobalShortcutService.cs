using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using RadialCommandWheel.Core;

namespace RadialCommandWheel.Services;

public sealed class GlobalShortcutService : IDisposable
{
    private const int HotkeyId = 0x5243;
    private const int EscapeHotkeyId = 0x5244;
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardCallback;
    private readonly DispatcherTimer _releaseWatchdog;
    private IntPtr _windowHandle;
    private IntPtr _keyboardHook;
    private HwndSource? _source;
    private ShortcutDefinition _shortcut = new();
    private bool _registered;
    private bool _wheelActive;
    private bool _escapeRegistered;
    private bool _activationConsumed;

    public GlobalShortcutService()
    {
        _keyboardCallback = KeyboardHookCallback;
        _releaseWatchdog = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(45)
        };
        _releaseWatchdog.Tick += (_, _) =>
        {
            if (_activationConsumed && !AreRequiredChordKeysDown())
            {
                _activationConsumed = false;
                if (!_wheelActive)
                {
                    return;
                }
            }

            if (!_wheelActive)
            {
                return;
            }

            if (!AreRequiredChordKeysDown())
            {
                CancelActiveWheel();
            }
        };
    }

    public event Action? Activated;
    public event Action? Cancelled;
    public event Action<string>? RegistrationStatusChanged;

    public bool IsRegistered => _registered;
    public bool IsWheelActive => _wheelActive;
    public bool IsActivationLatched => _activationConsumed;
    public string LastError { get; private set; } = "";

    public void Initialize(Window owner)
    {
        _windowHandle = new WindowInteropHelper(owner).Handle;
        _source = HwndSource.FromHwnd(_windowHandle)
                  ?? throw new InvalidOperationException("The management window handle is unavailable.");
        _source.AddHook(WindowProc);

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var module = NativeMethods.GetModuleHandle(process.MainModule?.ModuleName);
        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _keyboardCallback, module, 0);
        if (_keyboardHook == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Windows could not install the shortcut release monitor.");
        }
    }

    public bool Register(ShortcutDefinition shortcut)
    {
        Unregister();
        _shortcut = shortcut.Clone();
        var modifiers = (uint)shortcut.Modifiers | NativeMethods.ModNoRepeat;
        if (!NativeMethods.RegisterHotKey(_windowHandle, HotkeyId, modifiers, (uint)shortcut.VirtualKey))
        {
            var error = Marshal.GetLastWin32Error();
            LastError = FormatRegistrationError(error);
            RegistrationStatusChanged?.Invoke(LastError);
            return false;
        }

        _registered = true;
        LastError = "";
        if (!_releaseWatchdog.IsEnabled)
        {
            _releaseWatchdog.Start();
        }
        RegistrationStatusChanged?.Invoke($"{shortcut.DisplayText} is active.");
        return true;
    }

    public bool AreRequiredChordKeysDown()
    {
        if (!IsDown(_shortcut.VirtualKey))
        {
            return false;
        }

        return (!_shortcut.Modifiers.HasFlag(ShortcutModifiers.Control) || IsDown(0x11)) &&
               (!_shortcut.Modifiers.HasFlag(ShortcutModifiers.Alt) || IsDown(0x12)) &&
               (!_shortcut.Modifiers.HasFlag(ShortcutModifiers.Shift) || IsDown(0x10)) &&
               (!_shortcut.Modifiers.HasFlag(ShortcutModifiers.Windows) || IsDown(0x5B) || IsDown(0x5C));
    }

    public void SetWheelActive(bool active)
    {
        _wheelActive = active;
        if (active)
        {
            RegisterEscapeHotkey();
            if (!_releaseWatchdog.IsEnabled)
            {
                _releaseWatchdog.Start();
            }
        }
        else
        {
            UnregisterEscapeHotkey();
        }
    }

    public void CommitSelection()
    {
        _activationConsumed = true;
        _wheelActive = false;
        UnregisterEscapeHotkey();
        if (AreRequiredChordKeysDown())
        {
            // The registered-hotkey watchdog releases this latch after the physical chord is released.
        }
        else
        {
            _activationConsumed = false;
        }
    }

    public void Unregister()
    {
        _activationConsumed = false;
        SetWheelActive(false);
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_windowHandle, HotkeyId);
            _registered = false;
        }
        _releaseWatchdog.Stop();
    }

    public void Dispose()
    {
        Unregister();
        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_source is not null)
        {
            _source.RemoveHook(WindowProc);
            _source = null;
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            if (!_wheelActive && AreRequiredChordKeysDown())
            {
                if (_activationConsumed)
                {
                    handled = true;
                    return IntPtr.Zero;
                }

                _activationConsumed = true;
                _wheelActive = true;
                _releaseWatchdog.Start();
                RegisterEscapeHotkey();
                Activated?.Invoke();
            }

            handled = true;
        }
        else if (message == NativeMethods.WmHotkey && wParam.ToInt32() == EscapeHotkeyId)
        {
            CancelActiveWheel();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private IntPtr KeyboardHookCallback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && _wheelActive)
        {
            var keyData = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(data);
            var isKeyDown = message == (IntPtr)NativeMethods.WmKeydown ||
                            message == (IntPtr)NativeMethods.WmSyskeydown;
            var isKeyUp = message == (IntPtr)NativeMethods.WmKeyup ||
                          message == (IntPtr)NativeMethods.WmSyskeyup;

            if (isKeyDown && keyData.VirtualKeyCode == 0x1B)
            {
                CancelActiveWheel();
            }
            else if (isKeyUp &&
                     (keyData.VirtualKeyCode == _shortcut.VirtualKey ||
                      keyData.VirtualKeyCode is 0x11 or 0x12 or 0x10 or 0x5B or 0x5C) &&
                     !AreRequiredChordKeysDown())
            {
                CancelActiveWheel();
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, code, message, data);
    }

    private static bool IsDown(int virtualKey) => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private void CancelActiveWheel()
    {
        if (!_wheelActive)
        {
            return;
        }

        _wheelActive = false;
        _activationConsumed = true;
        UnregisterEscapeHotkey();
        Cancelled?.Invoke();
    }

    private void RegisterEscapeHotkey()
    {
        if (_escapeRegistered)
        {
            return;
        }

        _escapeRegistered = NativeMethods.RegisterHotKey(_windowHandle, EscapeHotkeyId,
            NativeMethods.ModNoRepeat, 0x1B);
    }

    private void UnregisterEscapeHotkey()
    {
        if (!_escapeRegistered)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_windowHandle, EscapeHotkeyId);
        _escapeRegistered = false;
    }

    private static string FormatRegistrationError(int error) => error switch
    {
        1409 => "This shortcut is already registered by another application. Choose a different combination.",
        5 => "Windows denied registration of this shortcut.",
        _ => $"Windows could not register this shortcut (error {error}). It has not been replaced automatically."
    };
}

