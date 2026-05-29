// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenClaw.Helpers;

namespace OpenClaw.Views;

/// <summary>
/// Dialog for viewing application log files.
/// </summary>
public sealed partial class LogViewerDialog : ContentDialog
{
    private readonly string _logDirectory;
    private CancellationTokenSource? _loadCts;

    public LogViewerDialog()
    {
        this.InitializeComponent();
        _logDirectory = App.Logger.LogFolderPath;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await LoadTodayLogAsync();
    }

    private async Task LoadTodayLogAsync()
    {
        CancelPendingLoad();
        var loadCts = new CancellationTokenSource();
        _loadCts = loadCts;
        var token = loadCts.Token;

        try
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var logFile = Path.Combine(_logDirectory, $"openclaw-{today}.log");
            LogFileLabel.Text = string.Format(StringResources.LogFileLabelFormat, $"openclaw-{today}.log");

            if (File.Exists(logFile))
            {
                var tail = await Task.Run(
                    () => LogFileUtilities.ReadLastLines(logFile, LogFileUtilities.DefaultTailLineCount, token),
                    token);
                if (!IsActiveLoad(loadCts))
                {
                    return;
                }

                var content = string.Join(Environment.NewLine, tail.Lines);
                if (tail.WasTruncated)
                {
                    LogContent.Text = string.Format(StringResources.LogShowingLastLinesFormat, tail.TotalLineCount) + Environment.NewLine + Environment.NewLine
                        + content;
                }
                else
                {
                    LogContent.Text = content;
                }
            }
            else
            {
                if (!IsActiveLoad(loadCts))
                {
                    return;
                }

                LogContent.Text = StringResources.LogNotFoundToday;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!IsActiveLoad(loadCts))
            {
                return;
            }

            LogContent.Text = string.Format(StringResources.LogReadFailedFormat, ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_loadCts, loadCts))
            {
                _loadCts = null;
            }

            loadCts.Dispose();
        }
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        await LoadTodayLogAsync();
    }

    private void OnOpenLogFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Directory.Exists(_logDirectory))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_logDirectory)
                {
                    UseShellExecute = true,
                });
            }
        }
        catch (Exception ex)
        {
            App.Logger.Error($"Failed to open log folder: {ex.Message}");
        }
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        CancelPendingLoad();
    }

    private void CancelPendingLoad()
    {
        _loadCts?.Cancel();
    }

    private bool IsActiveLoad(CancellationTokenSource loadCts)
    {
        return ReferenceEquals(_loadCts, loadCts) && !loadCts.IsCancellationRequested;
    }
}
