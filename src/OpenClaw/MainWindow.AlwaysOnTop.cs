// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenClaw.Helpers;

namespace OpenClaw;

public sealed partial class MainWindow
{
    private bool _isAlwaysOnTop;

    private void InitializeAlwaysOnTop()
    {
        if (App.Configuration.Settings.AlwaysOnTop)
        {
            SetAlwaysOnTop(true);
        }
    }

    private void ToggleAlwaysOnTop()
    {
        SetAlwaysOnTop(!_isAlwaysOnTop);
        App.Configuration.Settings.AlwaysOnTop = _isAlwaysOnTop;
        App.Configuration.Save();
    }

    private void SetAlwaysOnTop(bool value)
    {
        _isAlwaysOnTop = value;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = value;
        }

        if (!WindowFrameHelper.SetTopMost(this, value))
        {
            App.Logger.Warning($"Failed to apply native always-on-top state: {value}");
        }

        UpdatePinButtonVisualState();
    }

    private void UpdatePinButtonVisualState()
    {
        if (PinButton is null)
        {
            return;
        }

        var foregroundResource = _isAlwaysOnTop
            ? "AccentTextFillColorPrimaryBrush"
            : "TextFillColorSecondaryBrush";

        Brush? foreground = null;
        if (Application.Current.Resources.TryGetValue(foregroundResource, out var foregroundBrush))
        {
            foreground = foregroundBrush as Brush;
        }

        PinButton.Foreground = foreground;
        PinButton.Content = new FontIcon
        {
            Glyph = _isAlwaysOnTop ? "" : "",
            FontSize = 14,
            Foreground = foreground
        };
    }
}
