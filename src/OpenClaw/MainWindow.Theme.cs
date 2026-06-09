// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using OpenClaw.Helpers;

namespace OpenClaw;

public sealed partial class MainWindow
{
    private void OnThemeSelectionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string selectedTheme)
        {
            return;
        }

        App.Configuration.Settings.AppTheme = selectedTheme;
        App.Configuration.SaveDeferred();
        ApplyTheme(selectedTheme);
        UpdateThemeSelector(selectedTheme);
    }

    private void UpdateThemeSelector(string themeMode)
    {
        foreach (var button in EnumerateThemeButtons())
        {
            UpdateThemeButtonState(button, themeMode);
        }
    }

    private IEnumerable<ToggleButton> EnumerateThemeButtons()
    {
        yield return SystemThemeButton;
        yield return LightThemeButton;
        yield return DarkThemeButton;
    }

    private static void UpdateThemeButtonState(ToggleButton button, string selectedThemeMode)
    {
        var isSelected = string.Equals(button.Tag as string, selectedThemeMode, StringComparison.Ordinal);
        button.IsChecked = isSelected;
        button.Background = isSelected
            ? GetBrushResource("AccentFillColorDefaultBrush")
            : GetBrushResource("SubtleFillColorTransparentBrush");

        button.Foreground = isSelected
            ? GetBrushResource("TextOnAccentFillColorPrimaryBrush")
            : GetBrushResource("TextFillColorSecondaryBrush");
    }

    private static Brush GetBrushResource(string resourceKey)
    {
        if (Application.Current.Resources.TryGetValue(resourceKey, out var resource) &&
            resource is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Colors.Transparent);
    }

    private void ApplyTheme(string themeMode)
    {
        _isDarkThemeActive = WindowFrameHelper.ApplyWindowTheme(
            this,
            themeMode,
            _isDarkThemeActive,
            UpdateTitleBarColors,
            DispatcherQueue,
            RefreshTitleBarVisualState,
            redrawWindow: true,
            useSizeNudgeOnDarkTransition: true,
            includeTrailingRefresh: true);
    }

    private void RefreshTitleBarVisualState()
    {
        UpdateTitleBarInsets();

        if (this.Content is FrameworkElement rootElement)
        {
            UpdateTitleBarColors(rootElement.ActualTheme);
            AppTitleBar.InvalidateMeasure();
            AppTitleBar.InvalidateArrange();
            rootElement.InvalidateMeasure();
            rootElement.InvalidateArrange();
            rootElement.UpdateLayout();
        }
    }

    private void UpdateTitleBarInsets()
    {
        var titleBar = AppWindow.TitleBar;
        LeftInsetColumn.Width = new GridLength(titleBar.LeftInset);
        RightInsetColumn.Width = new GridLength(titleBar.RightInset);
    }

    private void UpdateTitleBarColors(ElementTheme actualTheme)
    {
        var palette = WindowFrameHelper.CreateThemePalette(actualTheme);
        var inactiveBackground = palette.IsDark
            ? Windows.UI.Color.FromArgb(255, 40, 40, 40)
            : Windows.UI.Color.FromArgb(255, 248, 248, 248);
        var currentCaptionColor = _isWindowActive ? palette.BackgroundColor : inactiveBackground;
        var currentForeground = _isWindowActive ? palette.ForegroundColor : palette.InactiveForegroundColor;

        WindowFrameHelper.ApplyTitleBarColors(this, new WindowTitleBarColors
        {
            IsDark = palette.IsDark,
            ForegroundColor = palette.ForegroundColor,
            BackgroundColor = currentCaptionColor,
            InactiveForegroundColor = palette.InactiveForegroundColor,
            InactiveBackgroundColor = inactiveBackground,
            ButtonForegroundColor = currentForeground,
            ButtonBackgroundColor = Colors.Transparent,
            ButtonInactiveForegroundColor = palette.InactiveForegroundColor,
            ButtonInactiveBackgroundColor = Colors.Transparent,
            ButtonHoverForegroundColor = palette.ForegroundColor,
            ButtonHoverBackgroundColor = palette.ButtonHoverBackgroundColor,
            ButtonPressedForegroundColor = palette.ForegroundColor,
            ButtonPressedBackgroundColor = palette.ButtonPressedBackgroundColor,
            NativeBackgroundColor = currentCaptionColor,
            NativeBorderColor = currentCaptionColor,
            NativeTextColor = currentForeground,
        });

        UpdateTitleBarContentState(currentForeground, _isWindowActive);
        AppTitleBar.Background = new SolidColorBrush(currentCaptionColor);
    }

    private void UpdateTitleBarContentState(Windows.UI.Color foregroundColor, bool isWindowActive)
    {
        AppTitleText.Foreground = new SolidColorBrush(foregroundColor);
        AppIcon.Opacity = isWindowActive ? 1.0 : 0.72;
    }
}
