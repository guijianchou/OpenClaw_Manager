// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace OpenClaw.Helpers;

/// <summary>
/// Provides shared helpers for syncing WinUI theme changes with native window chrome.
/// </summary>
internal static class WindowFrameHelper
{
    private const int DwmWindowAttributeUseImmersiveDarkMode = 20;
    private const int DwmWindowAttributeBorderColor = 34;
    private const int DwmWindowAttributeCaptionColor = 35;
    private const int DwmWindowAttributeTextColor = 36;
    private const uint SetWindowPosNoMove = 0x0002;
    private const uint SetWindowPosNoSize = 0x0001;
    private const uint SetWindowPosNoZOrder = 0x0004;
    private const uint SetWindowPosNoActivate = 0x0010;
    private const uint SetWindowPosFrameChanged = 0x0020;
    private const uint RedrawWindowInvalidate = 0x0001;
    private const uint RedrawWindowUpdatenow = 0x0100;
    private const uint RedrawWindowFrame = 0x0400;
    private const int ShowWindowHide = 0;
    private const int ShowWindowShowNormal = 1;
    private const int ShowWindowRestore = 9;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private static readonly IntPtr WindowTopMost = new(-1);
    private static readonly IntPtr WindowNoTopMost = new(-2);

    public static bool ApplyWindowTheme(
        Window window,
        string themeMode,
        bool isDarkThemeActive,
        Action<ElementTheme> updateTitleBarColors,
        DispatcherQueue dispatcherQueue,
        Action refreshVisualState,
        bool redrawWindow = false,
        bool repeatRefreshOnDarkTransition = false,
        bool useSizeNudgeOnDarkTransition = false,
        bool includeTrailingRefresh = false)
    {
        if (window.Content is not FrameworkElement rootElement)
        {
            return isDarkThemeActive;
        }

        rootElement.RequestedTheme = ThemeModeHelper.ToElementTheme(themeMode);
        var effectiveTheme = ThemeModeHelper.GetEffectiveTheme(rootElement, themeMode);
        return ApplyThemeVisualState(
            window,
            effectiveTheme,
            isDarkThemeActive,
            updateTitleBarColors,
            dispatcherQueue,
            refreshVisualState,
            redrawWindow,
            repeatRefreshOnDarkTransition,
            useSizeNudgeOnDarkTransition,
            includeTrailingRefresh);
    }

    public static bool ApplyActualTheme(
        Window window,
        ElementTheme actualTheme,
        bool isDarkThemeActive,
        Action<ElementTheme> updateTitleBarColors,
        DispatcherQueue dispatcherQueue,
        Action refreshVisualState,
        bool redrawWindow = false,
        bool repeatRefreshOnDarkTransition = false,
        bool useSizeNudgeOnDarkTransition = false,
        bool includeTrailingRefresh = false)
    {
        return ApplyThemeVisualState(
            window,
            actualTheme,
            isDarkThemeActive,
            updateTitleBarColors,
            dispatcherQueue,
            refreshVisualState,
            redrawWindow,
            repeatRefreshOnDarkTransition,
            useSizeNudgeOnDarkTransition,
            includeTrailingRefresh);
    }

    public static WindowThemePalette CreateThemePalette(ElementTheme actualTheme)
    {
        var isDark = actualTheme == ElementTheme.Dark;
        return new WindowThemePalette
        {
            IsDark = isDark,
            BackgroundColor = isDark
                ? Windows.UI.Color.FromArgb(255, 32, 32, 32)
                : Windows.UI.Color.FromArgb(255, 243, 243, 243),
            ForegroundColor = isDark ? Colors.White : Colors.Black,
            InactiveForegroundColor = isDark
                ? Windows.UI.Color.FromArgb(255, 160, 160, 160)
                : Windows.UI.Color.FromArgb(255, 128, 128, 128),
            ButtonHoverBackgroundColor = isDark
                ? Windows.UI.Color.FromArgb(255, 55, 55, 55)
                : Windows.UI.Color.FromArgb(255, 229, 229, 229),
            ButtonPressedBackgroundColor = isDark
                ? Windows.UI.Color.FromArgb(255, 68, 68, 68)
                : Windows.UI.Color.FromArgb(255, 217, 217, 217),
        };
    }

    public static void ApplyTitleBarColors(Window window, WindowTitleBarColors colors)
    {
        var titleBar = window.AppWindow.TitleBar;
        titleBar.ForegroundColor = colors.ForegroundColor;
        titleBar.BackgroundColor = colors.BackgroundColor;
        titleBar.InactiveForegroundColor = colors.InactiveForegroundColor;
        titleBar.InactiveBackgroundColor = colors.InactiveBackgroundColor;

        titleBar.ButtonForegroundColor = colors.ButtonForegroundColor;
        titleBar.ButtonBackgroundColor = colors.ButtonBackgroundColor;
        titleBar.ButtonInactiveForegroundColor = colors.ButtonInactiveForegroundColor;
        titleBar.ButtonInactiveBackgroundColor = colors.ButtonInactiveBackgroundColor;
        titleBar.ButtonHoverForegroundColor = colors.ButtonHoverForegroundColor;
        titleBar.ButtonHoverBackgroundColor = colors.ButtonHoverBackgroundColor;
        titleBar.ButtonPressedForegroundColor = colors.ButtonPressedForegroundColor;
        titleBar.ButtonPressedBackgroundColor = colors.ButtonPressedBackgroundColor;

        ApplyNativeWindowTheme(
            window,
            colors.NativeBackgroundColor,
            colors.NativeBorderColor,
            colors.NativeTextColor,
            colors.IsDark);
    }

    public static void QueueFrameRefresh(
        Window window,
        DispatcherQueue dispatcherQueue,
        Action refreshVisualState,
        bool repeatRefresh = false,
        bool redrawWindow = false)
    {
        dispatcherQueue.TryEnqueue(() =>
        {
            refreshVisualState();
            RefreshNonClientFrame(window, redrawWindow);

            if (!repeatRefresh)
            {
                return;
            }

            dispatcherQueue.TryEnqueue(() =>
            {
                refreshVisualState();
                RefreshNonClientFrame(window, redrawWindow);
            });
        });
    }

    public static void RefreshUsingSizeNudge(
        Window window,
        DispatcherQueue dispatcherQueue,
        Action refreshVisualState)
    {
        var currentSize = window.AppWindow.Size;
        if (currentSize.Width <= 0 || currentSize.Height <= 0)
        {
            return;
        }

        window.AppWindow.Resize(new SizeInt32(currentSize.Width, currentSize.Height + 1));
        dispatcherQueue.TryEnqueue(() =>
        {
            window.AppWindow.Resize(currentSize);
            refreshVisualState();
        });
    }

    public static void ApplyNativeWindowTheme(
        Window window,
        Windows.UI.Color backgroundColor,
        Windows.UI.Color borderColor,
        Windows.UI.Color textColor,
        bool isDark)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var useDarkMode = isDark ? 1 : 0;
        var nativeBorderColor = ToColorRef(borderColor);
        var captionColor = ToColorRef(backgroundColor);
        var nativeTextColor = ToColorRef(textColor);

        DwmSetWindowAttribute(hwnd, DwmWindowAttributeUseImmersiveDarkMode, ref useDarkMode, sizeof(int));
        DwmSetWindowAttribute(hwnd, DwmWindowAttributeBorderColor, ref nativeBorderColor, sizeof(uint));
        DwmSetWindowAttribute(hwnd, DwmWindowAttributeCaptionColor, ref captionColor, sizeof(uint));
        DwmSetWindowAttribute(hwnd, DwmWindowAttributeTextColor, ref nativeTextColor, sizeof(uint));
    }

    public static bool IsWindowMinimized(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        return hwnd != IntPtr.Zero && IsIconic(hwnd);
    }

    public static bool TryGetWindowRect(Window window, out int left, out int top, out int width, out int height)
    {
        left = 0;
        top = 0;
        width = 0;
        height = 0;

        var hwnd = WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        left = rect.Left;
        top = rect.Top;
        width = rect.Right - rect.Left;
        height = rect.Bottom - rect.Top;
        return true;
    }

    public static bool TrySetWindowRect(Window window, int left, int top, int width, int height)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        return SetWindowPos(
            hwnd,
            IntPtr.Zero,
            left,
            top,
            width,
            height,
            SetWindowPosNoZOrder | SetWindowPosNoActivate);
    }

    public static bool IsNativeWindowRectVisibleWithinAnyMonitor(int left, int top, int width, int height) =>
        WindowBoundsUtilities.IsVisibleWithinAnyWorkArea(left, top, width, height, GetNativeMonitorWorkAreas());

    public static bool TryCenterNativeWindowRectInNearestMonitor(
        int left,
        int top,
        int width,
        int height,
        out int centeredLeft,
        out int centeredTop)
    {
        centeredLeft = 0;
        centeredTop = 0;

        var rect = new NativeRect
        {
            Left = left,
            Top = top,
            Right = left + width,
            Bottom = top + height,
        };
        var monitor = MonitorFromRect(ref rect, MonitorDefaultToNearest);
        return monitor != IntPtr.Zero &&
            TryGetMonitorWorkArea(monitor, out var workArea) &&
            WindowBoundsUtilities.TryCenterInWorkArea(width, height, workArea, out centeredLeft, out centeredTop);
    }

    public static void HideWindow(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, ShowWindowRestore);
        }

        ShowWindow(hwnd, ShowWindowHide);
    }

    public static void ShowAndActivateWindow(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero)
        {
            window.Activate();
            return;
        }

        ShowWindow(hwnd, IsIconic(hwnd) ? ShowWindowRestore : ShowWindowShowNormal);
        window.Activate();
        SetForegroundWindow(hwnd);
    }

    public static bool SetTopMost(Window window, bool value)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        return SetWindowPos(
            hwnd,
            value ? WindowTopMost : WindowNoTopMost,
            0,
            0,
            0,
            0,
            SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosNoActivate);
    }

    private static bool ApplyThemeVisualState(
        Window window,
        ElementTheme actualTheme,
        bool isDarkThemeActive,
        Action<ElementTheme> updateTitleBarColors,
        DispatcherQueue dispatcherQueue,
        Action refreshVisualState,
        bool redrawWindow,
        bool repeatRefreshOnDarkTransition,
        bool useSizeNudgeOnDarkTransition,
        bool includeTrailingRefresh)
    {
        var isTransitioningToDark = actualTheme == ElementTheme.Dark && !isDarkThemeActive;
        updateTitleBarColors(actualTheme);
        QueueThemeRefresh(
            window,
            dispatcherQueue,
            refreshVisualState,
            isTransitioningToDark,
            redrawWindow,
            repeatRefreshOnDarkTransition,
            useSizeNudgeOnDarkTransition,
            includeTrailingRefresh);
        return actualTheme == ElementTheme.Dark;
    }

    private static void QueueThemeRefresh(
        Window window,
        DispatcherQueue dispatcherQueue,
        Action refreshVisualState,
        bool isTransitioningToDark,
        bool redrawWindow,
        bool repeatRefreshOnDarkTransition,
        bool useSizeNudgeOnDarkTransition,
        bool includeTrailingRefresh)
    {
        dispatcherQueue.TryEnqueue(() =>
        {
            refreshVisualState();
            RefreshNonClientFrame(window, redrawWindow);

            if (useSizeNudgeOnDarkTransition && isTransitioningToDark)
            {
                RefreshUsingSizeNudge(window, dispatcherQueue, refreshVisualState);
            }

            if (repeatRefreshOnDarkTransition && isTransitioningToDark)
            {
                dispatcherQueue.TryEnqueue(() =>
                {
                    refreshVisualState();
                    RefreshNonClientFrame(window, redrawWindow);
                });
            }

            if (includeTrailingRefresh)
            {
                dispatcherQueue.TryEnqueue(() =>
                {
                    refreshVisualState();
                });
            }
        });
    }

    private static void RefreshNonClientFrame(Window window, bool redrawWindow)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosNoZOrder | SetWindowPosNoActivate | SetWindowPosFrameChanged);

        if (!redrawWindow)
        {
            return;
        }

        RedrawWindow(
            hwnd,
            IntPtr.Zero,
            IntPtr.Zero,
            RedrawWindowInvalidate | RedrawWindowUpdatenow | RedrawWindowFrame);
        DwmFlush();
    }

    private static IReadOnlyList<WindowWorkArea> GetNativeMonitorWorkAreas()
    {
        var workAreas = new List<WindowWorkArea>();
        EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (monitor, _, _, _) =>
            {
                if (TryGetMonitorWorkArea(monitor, out var workArea))
                {
                    workAreas.Add(workArea);
                }

                return true;
            },
            IntPtr.Zero);
        return workAreas;
    }

    private static bool TryGetMonitorWorkArea(IntPtr monitor, out WindowWorkArea workArea)
    {
        workArea = default;
        var monitorInfo = new NativeMonitorInfo
        {
            Size = Marshal.SizeOf<NativeMonitorInfo>(),
        };

        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        var rect = monitorInfo.WorkArea;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        workArea = new WindowWorkArea(rect.Left, rect.Top, width, height);
        return true;
    }

    private static uint ToColorRef(Windows.UI.Color color)
    {
        return (uint)(color.R | (color.G << 8) | (color.B << 16));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref uint pvAttribute,
        int cbAttribute);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(
        IntPtr hWnd,
        IntPtr lprcUpdate,
        IntPtr hrgnUpdate,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumProc lpfnEnum,
        IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect lprc, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref NativeMonitorInfo lpmi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    private delegate bool MonitorEnumProc(
        IntPtr hMonitor,
        IntPtr hdcMonitor,
        IntPtr lprcMonitor,
        IntPtr dwData);

    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }
}

internal readonly record struct WindowThemePalette
{
    public bool IsDark { get; init; }
    public Windows.UI.Color BackgroundColor { get; init; }
    public Windows.UI.Color ForegroundColor { get; init; }
    public Windows.UI.Color InactiveForegroundColor { get; init; }
    public Windows.UI.Color ButtonHoverBackgroundColor { get; init; }
    public Windows.UI.Color ButtonPressedBackgroundColor { get; init; }
}

internal readonly record struct WindowTitleBarColors
{
    public bool IsDark { get; init; }
    public Windows.UI.Color ForegroundColor { get; init; }
    public Windows.UI.Color BackgroundColor { get; init; }
    public Windows.UI.Color InactiveForegroundColor { get; init; }
    public Windows.UI.Color InactiveBackgroundColor { get; init; }
    public Windows.UI.Color ButtonForegroundColor { get; init; }
    public Windows.UI.Color ButtonBackgroundColor { get; init; }
    public Windows.UI.Color ButtonInactiveForegroundColor { get; init; }
    public Windows.UI.Color ButtonInactiveBackgroundColor { get; init; }
    public Windows.UI.Color ButtonHoverForegroundColor { get; init; }
    public Windows.UI.Color ButtonHoverBackgroundColor { get; init; }
    public Windows.UI.Color ButtonPressedForegroundColor { get; init; }
    public Windows.UI.Color ButtonPressedBackgroundColor { get; init; }
    public Windows.UI.Color NativeBackgroundColor { get; init; }
    public Windows.UI.Color NativeBorderColor { get; init; }
    public Windows.UI.Color NativeTextColor { get; init; }
}
