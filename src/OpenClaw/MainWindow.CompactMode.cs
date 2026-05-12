// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Xaml;
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

        // Hide WebView and InfoBars (Rows 2-4)
        SetCompactVisibility(Visibility.Collapsed);

        // Restore compact position if previously saved
        var settings = App.Configuration.Settings;
        if (settings.CompactWindowLeft >= 0 && settings.CompactWindowTop >= 0)
        {
            appWindow.Move(new PointInt32((int)settings.CompactWindowLeft, (int)settings.CompactWindowTop));
        }

        appWindow.Resize(new SizeInt32(CompactWidth, CompactHeight));
        _isCompactMode = true;
        App.Logger.Info("Entered compact mode.");
    }

    private void ExitCompactMode()
    {
        // Save compact position
        var appWindow = this.AppWindow;
        var compactPos = appWindow.Position;
        App.Configuration.Settings.CompactWindowLeft = compactPos.X;
        App.Configuration.Settings.CompactWindowTop = compactPos.Y;

        // Restore normal bounds
        SetCompactVisibility(Visibility.Visible);
        appWindow.Resize(_normalSize.Width > 0 ? _normalSize : new SizeInt32(1280, 800));
        if (_normalSize.Width > 0)
        {
            appWindow.Move(_normalPosition);
        }

        _isCompactMode = false;
        App.Logger.Info("Exited compact mode.");
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

        if (root.FindName("LoadingRing") is UIElement loadingRing)
        {
            loadingRing.Visibility = visibility;
        }
    }
}
