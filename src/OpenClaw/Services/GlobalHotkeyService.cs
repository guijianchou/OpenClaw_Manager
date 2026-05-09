// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Runtime.InteropServices;

namespace OpenClaw.Services;

/// <summary>
/// Manages a single global hotkey registration via Win32 RegisterHotKey/UnregisterHotKey.
/// Designed to be created once and disposed on app shutdown.
/// Registration failure is non-fatal — the service logs a warning and remains inactive.
/// </summary>
internal sealed class GlobalHotkeyService : IDisposable
{
    private const string WindowClassPrefix = "OpenClaw.Hotkey.";
    private const int HotkeyId = 1;

    private readonly IAppLogger _logger;
    private readonly WindowProcedure _windowProcedure;
    private readonly string _windowClassName = WindowClassPrefix + Guid.NewGuid().ToString("N");
    private IntPtr _messageWindowHandle;
    private bool _isRegistered;
    private bool _isDisposed;
    private HotkeyBinding? _currentBinding;

    public GlobalHotkeyService(IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _windowProcedure = OnWindowMessage;
    }

    /// <summary>
    /// Raised when the registered global hotkey is pressed.
    /// </summary>
    public event Action? HotkeyPressed;

    /// <summary>
    /// Gets whether the hotkey is currently registered and active.
    /// </summary>
    public bool IsRegistered => _isRegistered;

    /// <summary>
    /// Gets the currently registered binding, or null if not registered.
    /// </summary>
    public HotkeyBinding? CurrentBinding => _isRegistered ? _currentBinding : null;

    /// <summary>
    /// Attempts to register the specified hotkey binding.
    /// Returns true if registration succeeded, false otherwise.
    /// </summary>
    public bool TryRegister(HotkeyBinding? binding)
    {
        Unregister();

        if (binding is null || binding.GetVirtualKeyCode() == 0)
        {
            _logger.Warning("hotkey.register.skipped: no valid binding provided.");
            return false;
        }

        EnsureMessageWindow();
        if (_messageWindowHandle == IntPtr.Zero)
        {
            _logger.Warning("hotkey.register.failed: message window not available.");
            return false;
        }

        var modifiers = binding.GetWin32Modifiers();
        var vk = binding.GetVirtualKeyCode();

        if (!RegisterHotKey(_messageWindowHandle, HotkeyId, modifiers, vk))
        {
            var error = Marshal.GetLastWin32Error();
            _logger.Warning($"hotkey.register.failed: RegisterHotKey returned false, error={error}, binding={binding}");
            return false;
        }

        _isRegistered = true;
        _currentBinding = binding;
        _logger.Info("hotkey.register.ok", new { binding = binding.ToString() });
        return true;
    }

    /// <summary>
    /// Unregisters the current hotkey if one is registered.
    /// </summary>
    public void Unregister()
    {
        if (!_isRegistered || _messageWindowHandle == IntPtr.Zero)
        {
            return;
        }

        UnregisterHotKey(_messageWindowHandle, HotkeyId);
        _isRegistered = false;
        _currentBinding = null;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Unregister();
        DestroyMessageWindow();
    }

    private void EnsureMessageWindow()
    {
        if (_messageWindowHandle != IntPtr.Zero)
        {
            return;
        }

        var wndClass = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = _windowProcedure,
            hInstance = GetModuleHandle(null),
            lpszClassName = _windowClassName,
        };

        if (RegisterClassExW(ref wndClass) == 0)
        {
            _logger.Warning($"hotkey.window.register_class_failed: {Marshal.GetLastWin32Error()}");
            return;
        }

        _messageWindowHandle = CreateWindowExW(
            0,
            _windowClassName,
            "OpenClaw Hotkey",
            0,
            0, 0, 0, 0,
            IntPtr.Zero,
            IntPtr.Zero,
            wndClass.hInstance,
            IntPtr.Zero);

        if (_messageWindowHandle == IntPtr.Zero)
        {
            _logger.Warning($"hotkey.window.create_failed: {Marshal.GetLastWin32Error()}");
        }
    }

    private void DestroyMessageWindow()
    {
        if (_messageWindowHandle != IntPtr.Zero)
        {
            DestroyWindow(_messageWindowHandle);
            _messageWindowHandle = IntPtr.Zero;
        }

        UnregisterClassW(_windowClassName, GetModuleHandle(null));
    }

    private IntPtr OnWindowMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WindowMessages.Hotkey && (int)wParam == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            return IntPtr.Zero;
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    // Win32 interop
    private delegate IntPtr WindowProcedure(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static class WindowMessages
    {
        public const uint Hotkey = 0x0312;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public WindowProcedure lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }
}
