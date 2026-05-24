// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OpenClaw.Helpers;
using OpenClaw.Models;
using OpenClaw.Services;

namespace OpenClaw.ViewModels;

public partial class MainViewModel
{
    private const int HeartbeatIndicatorCount = 12;
    private const int RunIndicatorCount = 12;
    private const int LatencyHistoryCapacity = 60;
    private const string DefaultHeartbeatSummary = "HB --";
    private const string DefaultModelSummary = "--";
    private const string DefaultAccessSummary = "AUTH --";
    private const string DefaultLatencySummary = "-- ms";
    private const string DefaultWorkStatus = "WAIT";

    private static Brush NeutralBrush => GetStatusBrush("StatusOfflineBrush");
    private static Brush SuccessBrush => GetStatusBrush("SuccessBrush");
    private static Brush WarningBrush => GetStatusBrush("StatusReconnectingBrush");
    private static Brush ErrorBrush => GetStatusBrush("StatusErrorBrush");
    private static StatusBrushes CurrentStatusBrushes => new(NeutralBrush, SuccessBrush, WarningBrush, ErrorBrush);

    private readonly WebViewService _webViewService;
    private readonly HostedUiBridge _hostedUiBridge = new();
    private readonly ControlUiLatencyService _latencyService = new();
    private readonly LatencyHistory _latencyHistory = new(LatencyHistoryCapacity);
    private readonly StatusPresenter _statusPresenter = new();
    private readonly Action<Action> _dispatchToUi;

    private ShellSessionCoordinator? _coordinator;
    private EnvironmentConfig? _selectedEnvironment;
    private string _statusMessage = string.Empty;
    private Brush _statusIndicatorBrush = NeutralBrush;
    private ConnectionState _connectionState = ConnectionState.Offline;
    private bool _isLoading;
    private string _errorMessage = string.Empty;
    private bool _isErrorVisible;
    private bool _showRetryButton;
    private string _diagnosticSummary = string.Empty;
    private bool _isDiagnosticVisible;
    private string _heartbeatSummary = DefaultHeartbeatSummary;
    private Brush _heartbeatSummaryBrush = NeutralBrush;
    private string _modelSummaryText = DefaultModelSummary;
    private string _lastKnownModelSummaryText = DefaultModelSummary;
    private string _accessSummaryText = DefaultAccessSummary;
    private Brush _accessSummaryBrush = NeutralBrush;
    private string _latencySummaryText = DefaultLatencySummary;
    private Brush _latencySummaryBrush = NeutralBrush;
    private string _latencyTooltipText = LatencyTooltipFormatter.Format(LatencyHistorySummary.Empty);
    private string _workStatusText = DefaultWorkStatus;
    private Brush _workStatusBrush = NeutralBrush;
    private RunIndicatorMode _runIndicatorMode = RunIndicatorMode.Wait;
    private bool _isRunIndicatorsAnimating;
    private int _runAnimationFrame;
    private HeartbeatProbeStatus? _lastHeartbeatStatus;
    private bool _isHostVisible = true;
    private string? _lastKnownPoP;

    // Recovery state projection
    private RecoveryState _shellConnectionState = RecoveryState.Connecting;
    private bool _isRecovering;
    private string _recoveryMessage = string.Empty;

    private static Brush GetStatusBrush(string key)
    {
        return Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }
}
