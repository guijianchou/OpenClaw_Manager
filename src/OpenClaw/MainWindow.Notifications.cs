// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace OpenClaw;

public sealed partial class MainWindow
{
    private void InitializeNotifications()
    {
        ViewModel.TaskCompletedNotification += OnTaskCompletedNotification;
    }

    private void DisposeNotifications()
    {
        ViewModel.TaskCompletedNotification -= OnTaskCompletedNotification;
    }

    private void OnTaskCompletedNotification(string modelName)
    {
        if (_isWindowActive && !_isWindowHidden)
        {
            // Window is visible and active — no need to notify
            return;
        }

        try
        {
            var builder = new AppNotificationBuilder()
                .AddText("OpenClaw — Task Complete")
                .AddText(string.IsNullOrWhiteSpace(modelName) ? "Your task has finished." : $"Model: {modelName}");

            var notification = builder.BuildNotification();
            AppNotificationManager.Default.Show(notification);
            App.Logger.Info("toast.shown", new { model = modelName });
        }
        catch (Exception ex)
        {
            App.Logger.Warning($"toast.failed: {ex.Message}");
        }
    }
}
