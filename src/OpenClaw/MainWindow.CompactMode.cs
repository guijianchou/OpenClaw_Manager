// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Xaml;
using OpenClaw.Helpers;
using Windows.Graphics;

namespace OpenClaw;

public sealed partial class MainWindow
{
    private const int CompactWidth = 480;
    private const int CompactHeight = 120;

    private bool _isCompactMode;
    private SizeInt32 _normalSize;
    private PointInt32 _normalPosition;

    private void RestoreCompactModeIfSaved()
    {
        if (App.Configuration.Settings.CompactMode)
        {
            // Defer to after layout so AppWindow.Size is valid
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (!_isCompactMode)
                {
                    EnterCompactMode();
                }
            });
        }
    }

    internal void ToggleCompactMode()
    {
        if (_isCompactMode)
        {
            ExitCompactMode();
        }
        else
        {
            EnterCompactMode();
        }

        App.Configuration.Settings.CompactMode = _isCompactMode;
        App.Configuration.Save();
    }

    private void EnterCompactMode()
    {
        // Save current normal bounds
        var appWindow = this.AppWindow;
        _normalSize = appWindow.Size;
        _normalPosition = appWindow.Position;
        _isCompactMode = true;

        // Hide WebView and InfoBars (Rows 2-4)
        SetCompactVisibility(Visibility.Collapsed);
        ApplyCompactTopBarState(true);
        UpdateLoadingRingVisibility();

        appWindow.Resize(new SizeInt32(CompactWidth, CompactHeight));
        RestoreCompactWindowPosition(appWindow);
        App.Logger.Info("Entered compact mode.");
    }

    private void ExitCompactMode()
    {
        // Save compact position
        var appWindow = this.AppWindow;
        SaveCompactWindowPosition();

        // Restore normal bounds
        _isCompactMode = false;
        SetCompactVisibility(Visibility.Visible);
        ApplyCompactTopBarState(false);
        UpdateLoadingRingVisibility();
        appWindow.Resize(_normalSize.Width > 0 ? _normalSize : new SizeInt32(1280, 800));
        if (_normalSize.Width > 0)
        {
            appWindow.Move(_normalPosition);
        }

        ResumeDeferredWebViewRecreationIfReady();
        App.Logger.Info("Exited compact mode.");
    }

    private void SaveCompactWindowPosition()
    {
        var compactPos = this.AppWindow.Position;
        App.Configuration.Settings.CompactWindowLeft = compactPos.X;
        App.Configuration.Settings.CompactWindowTop = compactPos.Y;
    }

    private static void RestoreCompactWindowPosition(Microsoft.UI.Windowing.AppWindow appWindow)
    {
        var settings = App.Configuration.Settings;
        if (!WindowBoundsUtilities.HasSavedPosition(settings.CompactWindowLeft, settings.CompactWindowTop))
        {
            return;
        }

        var left = (int)settings.CompactWindowLeft;
        var top = (int)settings.CompactWindowTop;
        if (WindowBoundsUtilities.IsVisibleWithinAnyWorkArea(left, top, CompactWidth, CompactHeight, GetDisplayWorkAreas()))
        {
            appWindow.Move(new PointInt32(left, top));
            return;
        }

        if (TryGetCurrentDisplayWorkArea(appWindow, out var currentWorkArea) &&
            WindowBoundsUtilities.TryCenterInWorkArea(CompactWidth, CompactHeight, currentWorkArea, out var centeredLeft, out var centeredTop))
        {
            appWindow.Move(new PointInt32(centeredLeft, centeredTop));
            App.Logger.Info("Saved compact bounds were outside current displays; moved compact window to the current display.");
        }
    }

    private void SetCompactVisibility(Visibility visibility)
    {
        if (this.Content is not FrameworkElement root)
        {
            return;
        }

        // Hide/show InfoBars and WebView host (Rows 2, 3, 4)
        if (root.FindName("ConnectionInfoBar") is UIElement connectionInfoBar)
        {
            connectionInfoBar.Visibility = visibility;
        }

        if (root.FindName("DiagnosticInfoBar") is UIElement diagnosticInfoBar)
        {
            diagnosticInfoBar.Visibility = visibility;
        }

        if (root.FindName("WebViewHost") is UIElement webViewHost)
        {
            webViewHost.Visibility = visibility;
        }
    }

    private void UpdateLoadingRingVisibility()
    {
        LoadingRing.Visibility = !_isCompactMode && ViewModel.IsLoading
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyCompactTopBarState(bool isCompact)
    {
        VisualStateManager.GoToState(
            RootLayout,
            isCompact ? "CompactMode" : "FullMode",
            useTransitions: false);
    }
}
