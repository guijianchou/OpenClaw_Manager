// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Windowing;

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

        UpdatePinButtonVisualState();
    }

    private void UpdatePinButtonVisualState()
    {
        if (PinButton is null)
        {
            return;
        }

        PinButton.Content = _isAlwaysOnTop
            ? new Microsoft.UI.Xaml.Controls.FontIcon { Glyph = "", FontSize = 14 }  // Pinned
            : new Microsoft.UI.Xaml.Controls.FontIcon { Glyph = "", FontSize = 14 }; // Unpin
    }
}
