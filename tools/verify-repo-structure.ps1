Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

$testsPath = Join-Path $repoRoot 'tests'
if (Test-Path -LiteralPath $testsPath) {
    throw 'Active tests/ directory exists, but this checkpoint intentionally keeps tests out of the solution.'
}

$solution = Get-Content -LiteralPath (Join-Path $repoRoot 'OpenClaw.sln') -Raw
if ($solution -match 'OpenClaw\.Tests|tests\\OpenClaw\.Tests') {
    throw 'OpenClaw.sln still references the removed test harness.'
}

$coreSolutionMappingPattern = '\{BC4C7184-C8DD-4748-AC82-D26123568BD1\}\.(Debug|Release)\|(x64|x86|ARM64)\.(ActiveCfg|Build\.0) = (Debug|Release)\|(x64|x86|ARM64)'
if ($solution -match $coreSolutionMappingPattern) {
    throw 'OpenClaw.Core solution platform mappings must stay on Any CPU because the class library does not define architecture-specific project configurations.'
}

$coreFiles = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core') -Recurse -File -Include *.cs
$forbiddenCorePattern = 'using Microsoft\.UI|using Microsoft\.Web\.WebView2|using Windows\.Graphics|using Windows\.UI|using WinRT|Microsoft\.Web\.WebView2|Type\.GetType\("Microsoft\.Web\.WebView2|App\.Configuration|App\.Logger|App\.MainWindow'
foreach ($file in $coreFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    if ($content -match $forbiddenCorePattern) {
        throw "Core boundary violation: $($file.FullName)"
    }
}

$project = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/OpenClaw.csproj') -Raw
$currentVersion = '3.0.1'
$currentFileVersion = "$currentVersion.0"
if ($project -notmatch [regex]::Escape("<Version>$currentVersion</Version>") -or
    $project -notmatch [regex]::Escape("<AssemblyVersion>$currentFileVersion</AssemblyVersion>") -or
    $project -notmatch [regex]::Escape("<FileVersion>$currentFileVersion</FileVersion>")) {
    throw "OpenClaw.csproj version metadata must stay aligned at $currentVersion / $currentFileVersion."
}

$packageManifest = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Package.appxmanifest') -Raw
$appManifest = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/app.manifest') -Raw
if ($packageManifest -notmatch [regex]::Escape("Version=`"$currentFileVersion`"")) {
    throw "Package.appxmanifest identity version must stay aligned at $currentFileVersion."
}

if ($appManifest -notmatch [regex]::Escape("version=`"$currentFileVersion`"")) {
    throw "app.manifest assembly identity version must stay aligned at $currentFileVersion."
}

$readme = Get-Content -LiteralPath (Join-Path $repoRoot 'README.md') -Raw
$readmeZhLines = Get-Content -LiteralPath (Join-Path $repoRoot 'readme_zh.md')
$changelogLines = Get-Content -LiteralPath (Join-Path $repoRoot 'changelog.md')
$changelog = Get-Content -LiteralPath (Join-Path $repoRoot 'changelog.md') -Raw
$readmeZhHeading = $readmeZhLines | Where-Object { $_ -like "## *$currentVersion*" } | Select-Object -First 1
$currentVersionCodeSpan = '`' + $currentVersion + '`'
$changelogMetadataLines = $changelogLines | Where-Object {
    $_ -match 'app.*assembly.*file.*package manifest.*application manifest.*README.*changelog' -and
    $_ -match [regex]::Escape($currentVersionCodeSpan)
}
if ($readme -notmatch [regex]::Escape("**Current version:** $currentVersion") -or
    $readme -notmatch [regex]::Escape("## Current $currentVersion Notes") -or
    $readmeZhLines.Count -lt 5 -or
    $readmeZhLines[4] -notmatch [regex]::Escape($currentVersion) -or
    [string]::IsNullOrWhiteSpace($readmeZhHeading) -or
    $changelog -notmatch [regex]::Escape("metadata to $currentVersionCodeSpan") -or
    $changelogMetadataLines.Count -lt 2) {
    throw "README, Chinese README, and changelog current-version metadata must stay aligned at $currentVersion."
}

$englishResources = [xml](Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Strings/en-us/Resources.resw') -Raw -Encoding UTF8)
$chineseResources = [xml](Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Strings/zh-cn/Resources.resw') -Raw -Encoding UTF8)
$englishResourceKeys = @($englishResources.root.data | ForEach-Object { $_.name } | Sort-Object)
$chineseResourceKeys = @($chineseResources.root.data | ForEach-Object { $_.name } | Sort-Object)
$resourceKeyDifferences = @(Compare-Object $englishResourceKeys $chineseResourceKeys)
if ($resourceKeyDifferences.Count -gt 0) {
    $missingKeys = $resourceKeyDifferences |
        ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" }
    throw "Localized resource key mismatch between en-us and zh-cn: $($missingKeys -join ', ')"
}

foreach ($resource in @(
    'HostedUiBridge.Script.js',
    'HostedUiBridge.HostMessaging.js',
    'HostedUiBridge.MutationFilter.js',
    'HostedUiBridge.ModelResolver.js',
    'HostedUiBridge.DomUtilities.js',
    'HostedUiBridge.ModelDomFallback.js',
    'HostedUiBridge.ActivityState.js',
    'HostedUiBridge.PhaseClassifier.js',
    'HostedUiBridge.StatusInspection.js',
    'HostedUiBridge.CommandDispatch.js'
)) {
    if ($project -notmatch [regex]::Escape($resource)) {
        throw "Missing embedded bridge resource entry: $resource"
    }
}

foreach ($resource in @('WebViewCommands.StopInjection.js', 'WebViewCommands.AbortRun.js')) {
    if ($project -notmatch [regex]::Escape($resource)) {
        throw "Missing embedded WebView command resource entry: $resource"
    }
}

if ($project -notmatch [regex]::Escape('WebViewStatusInspector.Inspect.js')) {
    throw 'Missing embedded WebView status inspection script resource.'
}

$webViewService = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.cs') -Raw
if ($webViewService -match 'ParseControlUiSnapshot|ExecuteControlUiInspectionAsync|_latestControlUiSnapshot') {
    throw 'WebViewService.cs must not own Control UI inspection internals.'
}

if ($webViewService -notmatch 'public bool Reload\(\)' -or
    $webViewService -match 'public void Reload\(\)' -or
    $webViewService -notmatch 'Cannot reload: WebView2 not initialized' -or
    $webViewService -notmatch 'return false' -or
    $webViewService -notmatch 'return true' -or
    $webViewService -notmatch 'Reload failed before WebView2 was ready' -or
    $webViewService -notmatch 'Retry failed before WebView2 was ready') {
    throw 'WebView reload/retry command-start failures must publish error state and expose whether navigation actually started.'
}

if ($webViewService -notmatch 'TrySetUnavailableSnapshot\(\s*"Hosted bridge page token was not accepted after navigation\.",\s*generation\)' -or
    $webViewService -match 'SetUnavailableSnapshot\("Hosted bridge page token was not accepted after navigation\."\)') {
    throw 'WebView page-token retry exhaustion must publish Unavailable only for the original navigation generation.'
}

$heartbeat = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.Heartbeat.cs') -Raw
if ($heartbeat -match 'CancellationTokenSource\? _heartbeatCts|Task\? _heartbeatTask|ObserveHeartbeatShutdownAsync') {
    throw 'Heartbeat loop ownership must live in HeartbeatRuntime.'
}

if ($heartbeat -notmatch '_heartbeatRunId' -or
    $heartbeat -notmatch '_heartbeatStateGate' -or
    $heartbeat -notmatch 'IsCurrentHeartbeatRun' -or
    $heartbeat -notmatch 'RunSessionAwareHeartbeatLoopAsync\(string gatewayUrl, TimeSpan interval, int runId, CancellationToken token\)' -or
    $heartbeat -notmatch 'TryScheduleHeartbeatReload\(string message, int runId' -or
    $heartbeat -notmatch 'TryStopHeartbeatForRecovery\(int runId\)' -or
    $heartbeat -notmatch 'if \(!TryStopHeartbeatForRecovery\(runId\)\)[\s\S]*?return true;[\s\S]*?HeartbeatFailed\?\.Invoke\(message\)') {
    throw 'Heartbeat loops must reject stale run observations and recovery requests after stop/restart.'
}

if ($heartbeat -notmatch 'private async Task<bool> ProcessHeartbeatTickAsync\(string gatewayUrl, int runId, CancellationToken token\)' -or
    $heartbeat -notmatch 'if \(!await ProcessHeartbeatTickAsync\(gatewayUrl, runId, token\)\)[\s\S]*?return;[\s\S]*?while \(await timer\.WaitForNextTickAsync\(token\)\)') {
    throw 'Heartbeat loops must publish an immediate first observation before waiting for the first periodic interval.'
}

if ($heartbeat -notmatch 'InspectControlUiStateAsync\(token, publishSnapshot: false\)') {
    throw 'Heartbeat hosted-session inspection must not publish UI snapshots before heartbeat failure accounting runs.'
}

$heartbeatRuntime = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/HeartbeatRuntime.cs') -Raw
if ($heartbeatRuntime -notmatch 'Task\.Run\(\(\) => RunObservedAsync\(key, loop, cancellation\)\)' -or
    $heartbeatRuntime -match '_task = RunObservedAsync\(key, loop, _cancellation\)') {
    throw 'HeartbeatRuntime.Start must schedule the loop asynchronously instead of running the immediate first tick on the caller thread.'
}

$latencyService = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Services/ControlUiLatencyService.cs') -Raw
if ($latencyService -notmatch '_probeTask' -or
    $latencyService -notmatch 'ObserveStopAsync' -or
    $latencyService -notmatch '_probeRunId' -or
    $latencyService -notmatch 'IsCurrentProbeRun' -or
    $latencyService -notmatch 'IsCurrentProbeRunLocked' -or
    $latencyService -notmatch 'PublishIfChanged\(ControlUiLatencySnapshot snapshot, int runId\)' -or
    $latencyService -notmatch 'control_ui\.latency\.start' -or
    $latencyService -notmatch 'control_ui\.latency\.success' -or
    $latencyService -notmatch 'control_ui\.latency\.failure' -or
    $latencyService -match 'ControlUiLatencySnapshot\.Unknown with \{ Detail = snapshot\.Detail \}' -or
    $latencyService -notmatch 'finally[\s\S]*DisposeProbeResources' -or
    $latencyService -match 'Stop\(\)[\s\S]*?_probeCts\.Dispose\(\)' -or
    $latencyService -match 'Stop\(\)[\s\S]*?_probeTimer\?\.Dispose\(\)') {
    throw 'ControlUiLatencyService stop paths must cancel active probes, reject stale publications, preserve first-failure state, log start/result diagnostics, and let the observed probe task dispose CTS/timer resources.'
}

$singleInstanceCoordinator = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Services/SingleInstanceCoordinator.cs') -Raw
$app = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/App.xaml.cs') -Raw
if ($singleInstanceCoordinator -notmatch 'public async Task StopAsync\(\)' -or
    $singleInstanceCoordinator -notmatch 'TryCreatePrimaryAfterActivationFailureAsync' -or
    $singleInstanceCoordinator -notmatch 'TryCreatePrimaryAsync\(' -or
    $singleInstanceCoordinator -notmatch 'NamedPipeServerStream\? _activeServer' -or
    $singleInstanceCoordinator -notmatch 'DisposeActiveServer\(\)' -or
    $singleInstanceCoordinator -notmatch 'await _listenTask\.ConfigureAwait\(false\)' -or
    $singleInstanceCoordinator -notmatch 'public void Dispose\(\)[\s\S]*StopAsync\(\)\.GetAwaiter\(\)\.GetResult\(\)[\s\S]*_singleInstanceLock\.Release\(\)' -or
    $singleInstanceCoordinator -notmatch 'public static Task<bool> RequestActivationOfPrimaryInstanceAsync\(' -or
    $singleInstanceCoordinator -notmatch 'ConnectAsync\(timeoutMilliseconds, cancellationToken\)' -or
    $singleInstanceCoordinator -notmatch 'public static Task<SingleInstanceCoordinator\?> TryCreatePrimaryAfterActivationFailureAsync\(' -or
    $singleInstanceCoordinator -notmatch 'var deadline = DateTimeOffset\.UtcNow \+ timeout' -or
    $singleInstanceCoordinator -notmatch 'TryCreateSingleInstanceLockAsync\([\s\S]*deadline,[\s\S]*cancellationToken' -or
    $singleInstanceCoordinator -notmatch 'TryOwnLockAsync\(singleInstanceLock, deadline, cancellationToken\)' -or
    $singleInstanceCoordinator -notmatch 'await Task\.Delay\(GetRetryDelay\(deadline\), cancellationToken\)' -or
    $singleInstanceCoordinator -notmatch 'private static TimeSpan GetRetryDelay\(DateTimeOffset deadline\)' -or
    $app -notmatch 'await SingleInstanceCoordinator\.RequestActivationOfPrimaryInstanceAsync\(Logger\)' -or
    $app -notmatch 'await SingleInstanceCoordinator\.TryCreatePrimaryAfterActivationFailureAsync\(Logger\)' -or
    $app -notmatch 'ObserveSingleInstancePreferenceChangeAsync' -or
    $app -notmatch 'ApplySingleInstancePreferenceAsync' -or
    $app -notmatch '_singleInstancePreferenceGate' -or
    $app -notmatch 'StopSingleInstanceCoordinatorAsync' -or
    $app -notmatch 'internal void ApplySingleInstancePreference\(bool allowMultipleInstances\)\s*\{\s*_ = ObserveSingleInstancePreferenceChangeAsync\(allowMultipleInstances\);\s*\}' -or
    $app -notmatch 'private void StopSingleInstanceCoordinator\(\)[\s\S]*StopAsync\(\)\.GetAwaiter\(\)\.GetResult\(\)[\s\S]*coordinator\.Dispose\(\)') {
    throw 'App startup single-instance handoff must use async activation/takeover waits, app shutdown must wait for SingleInstanceCoordinator.StopAsync, and live multiple-instance settings changes must use an observed async path.'
}

if ($singleInstanceCoordinator -match '\bMutex\b|ReleaseMutex|AbandonedMutexException' -or
    $singleInstanceCoordinator -match 'WaitAsync\(TimeSpan\.FromSeconds\(2\)\)|TimeoutException\)[\s\S]*Best-effort' -or
    $singleInstanceCoordinator -match 'Thread\.Sleep' -or
    $singleInstanceCoordinator -match 'pipe\.Connect\(timeoutMilliseconds\)' -or
    $singleInstanceCoordinator -notmatch 'private readonly Semaphore\? _singleInstanceLock' -or
    $singleInstanceCoordinator -notmatch 'new Semaphore\(' -or
    $singleInstanceCoordinator -notmatch '_singleInstanceLock\.Release\(\)') {
    throw 'SingleInstanceCoordinator must use a named Semaphore instead of a Mutex so async live settings and shutdown can release ownership from any thread.'
}

$webViewServiceFiles = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services') -File -Filter 'WebViewService*.cs'
foreach ($file in $webViewServiceFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    if ($content -match 'App\.Logger') {
        throw "WebViewService partial must use injected logger, not App.Logger: $($file.Name)"
    }
}

if ($heartbeat -match 'App\.Configuration\.Settings\.Heartbeat|App\.Configuration\.Settings\.RecoveryPolicy') {
    throw 'Heartbeat must capture settings at start time instead of reading App.Configuration mid-loop.'
}

if ($heartbeat -match 'HttpClient|new\(\) \{ Timeout = TimeSpan\.FromSeconds\(10\) \}') {
    throw 'Heartbeat HTTP transport must live in GatewayHeartbeatTransport.'
}

$gatewayHeartbeatTransport = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/GatewayHeartbeatTransport.cs') -Raw
if ($gatewayHeartbeatTransport -notmatch '>= 500 => HeartbeatProbeResult\.Failure' -or
    $gatewayHeartbeatTransport -notmatch '404 => HeartbeatProbeResult\.Failure' -or
    $gatewayHeartbeatTransport -notmatch '405 => HeartbeatProbeResult\.Failure' -or
    $gatewayHeartbeatTransport -notmatch '_ => HeartbeatProbeResult\.Failure') {
    throw 'Gateway heartbeat transport must classify Cloudflare/proxy 5xx, missing Control UI paths, rejected probes, and unexpected responses as failures.'
}

$hostedSessionHeartbeatPolicy = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/HostedSessionHeartbeatPolicy.cs') -Raw
if ($hostedSessionHeartbeatPolicy -notmatch 'ControlUiPhase\.Unavailable\s*=>\s*HeartbeatProbeResult\.Failure') {
    throw 'Hosted-session heartbeat must treat unavailable bridge/status inspection as a failure instead of falling through to healthy HTTP transport.'
}

$commandFile = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.Commands.cs') -Raw
if ($commandFile -match 'ExecuteScriptAsync\(@"|ExecuteScriptAsync\(\$@"|const string script = """') {
    throw 'WebViewService.Commands.cs must load browser scripts from embedded JS assets, not large inline strings.'
}

if ($commandFile -notmatch 'CommandScriptTimeout' -or
    $commandFile -notmatch '\.AsTask\(timeout\.Token\)' -or
    $commandFile -notmatch 'CaptureAcceptedPageVersion' -or
    $commandFile -notmatch 'IsStillCurrentWebViewCommandTarget\(CoreWebView2 coreWebView, int pageVersion\)' -or
    $commandFile -notmatch 'IsCurrentAcceptedPageVersion\(pageVersion\)') {
    throw 'WebViewService command script execution must have a bounded timeout and reject stale WebView/page results after awaits.'
}

if ($commandFile -notmatch 'public async Task StopAsync\(\)[\s\S]*?var coreWebView = GetCoreWebView\(\)[\s\S]*?var pageVersion = _messageOwnership\.CaptureAcceptedPageVersion\(\)[\s\S]*?StopCurrentNavigation\(coreWebView\)[\s\S]*?TryAbortActiveRunAsync\(coreWebView, pageVersion\)[\s\S]*?InjectStopCommandAsync\(coreWebView, pageVersion\)[\s\S]*?IsStillCurrentWebViewCommandTarget\(coreWebView, pageVersion\)[\s\S]*?StopCurrentNavigation\(coreWebView\)' -or
    $commandFile -notmatch 'private void StopCurrentNavigation\(CoreWebView2 coreWebView\)[\s\S]*?ReferenceEquals\(coreWebView, _coreWebView\)[\s\S]*?coreWebView\.Stop\(\)[\s\S]*?CancelActiveNavigation\(\)' -or
    $commandFile -notmatch 'private async Task<bool> InjectStopCommandAsync\(CoreWebView2 coreWebView, int pageVersion\)' -or
    $commandFile -notmatch 'private async Task<bool> TryAbortActiveRunAsync\(CoreWebView2 coreWebView, int pageVersion\)') {
    throw 'WebViewService StopAsync fallback must stay bound to the original WebView/page target instead of stopping a newer page after stale command rejection.'
}

$statusInspector = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewStatusInspector.cs') -Raw
if ($statusInspector -match 'const string script = """|ExecuteScriptAsync\(@"|ExecuteScriptAsync\(\$@"') {
    throw 'WebViewStatusInspector must load browser scripts from embedded JS assets.'
}

if ($statusInspector -notmatch 'InspectionTimeout' -or
    $statusInspector -notmatch 'CancellationTokenSource\(InspectionTimeout\)' -or
    $statusInspector -notmatch '\.AsTask\(timeout\.Token\)') {
    throw 'WebViewStatusInspector script execution must have a bounded timeout.'
}

if ($statusInspector -notmatch '_statusProbeTask' -or
    $statusInspector -notmatch '_probeGate' -or
    $statusInspector -notmatch 'ProbeControlUiStateAfterNavigationAsync\(CancellationTokenSource cancellation' -or
    $statusInspector -notmatch 'finally[\s\S]*cancellation\.Dispose\(\)') {
    throw 'WebViewStatusInspector probe loop must own its task/cancellation lifetime.'
}

if ($statusInspector -notmatch 'UiTaskDispatcher _uiDispatcher' -or
    $statusInspector -notmatch '_uiDispatcher\.RunAsync\(') {
    throw 'WebViewStatusInspector status probe loop must marshal WebView2 inspection through the UI dispatcher.'
}

if ($statusInspector -notmatch 'WebViewMessageOwnership _messageOwnership' -or
    $statusInspector -notmatch 'CaptureAcceptedPageVersion' -or
    $statusInspector -notmatch '_latestControlUiSnapshotPageVersion' -or
    $statusInspector -notmatch '_inFlightInspectionPageVersion' -or
    $statusInspector -notmatch 'IsCurrentInspectionTarget\(int generation, int pageVersion\)' -or
    $statusInspector -notmatch 'TryPublishInspectionSnapshot\([\s\S]*ControlUiProbeSnapshot snapshot' -or
    $statusInspector -notmatch 'ControlUiProbeSnapshot\.Unavailable\("Control UI inspection timed out\."\)[\s\S]*TryPublishInspectionSnapshot' -or
    $statusInspector -notmatch 'HasActiveInFlightPublishWaiter\(int inspectionId\)' -or
    $statusInspector -notmatch 'TryApplyHostMessage\(string json, int pageVersion, out ControlUiProbeSnapshot snapshot\)' -or
    $statusInspector -notmatch 'ExecuteControlUiInspectionAsync\([\s\S]*int generation,[\s\S]*int pageVersion,[\s\S]*int inspectionId' -or
    $statusInspector -notmatch 'ApplyControlUiSnapshot\([\s\S]*int generation,[\s\S]*int pageVersion,[\s\S]*bool notifySnapshotUpdated') {
    throw 'WebViewStatusInspector direct script inspections must be scoped by generation, accepted page version, and active caller cancellation before publishing.'
}

if ($statusInspector -match 'CancelProbeLoop\(\)[\s\S]*?_statusProbeCts\.Dispose\(') {
    throw 'WebViewStatusInspector stop paths must cancel probe CTS and let the running probe dispose it.'
}

if ($statusInspector -notmatch 'CancelProbeLoop\(\)[\s\S]*?_statusProbeCts\?\.Cancel\(\)[\s\S]*?_statusProbeCts = null') {
    throw 'WebViewStatusInspector stop paths must cancel the active probe CTS before clearing ownership.'
}

$ownership = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewMessageOwnership.cs') -Raw
if ($ownership -notmatch 'nativeOwnerToken' -or $ownership -notmatch 'nativePageToken') {
    throw 'WebView host messages must validate native owner and page tokens.'
}

if ($ownership -notmatch 'CaptureAcceptedPageVersion' -or
    $ownership -notmatch 'TryCaptureCurrentVersion' -or
    $ownership -notmatch 'IsCurrentAcceptedPageVersion' -or
    $ownership -notmatch '_version\+\+') {
    throw 'WebView message ownership must expose a page-version guard for command results that complete after navigation.'
}

$webViewServiceMain = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.cs') -Raw
if ($webViewServiceMain -notmatch 'WebViewMessageOwnership' -or
    $webViewServiceMain -notmatch 'OnWebMessageReceived[\s\S]*IsCurrentHost\(hostGeneration\)' -or
    $webViewServiceMain -notmatch 'TryCaptureCurrentVersion\(args, root, out var pageVersion\)' -or
    $webViewServiceMain -notmatch 'TryApplyHostMessage\(message, pageVersion, out var snapshot\)' -or
    $webViewServiceMain -notmatch 'IsCurrentAcceptedPageVersion\(pageVersion\)' -or
    $webViewServiceMain -notmatch 'CaptureCurrentPageTokenAsync') {
    throw 'WebViewService must reject stale WebView messages by host generation and page ownership.'
}

if ($webViewServiceMain -notmatch 'PrepareNavigationStart[\s\S]*_messageOwnership\.BeginNavigation\(\)') {
    throw 'Programmatic WebView navigation must invalidate the accepted page token before CoreWebView2 starts navigating.'
}

$cancelActiveNavigationMethod = [regex]::Match($webViewServiceMain, 'private void CancelActiveNavigation\([\s\S]*?\n    \}')
if (!$cancelActiveNavigationMethod.Success -or
    $cancelActiveNavigationMethod.Value -notmatch 'CancelNavigationStartWatchdog\(\)' -or
    $cancelActiveNavigationMethod.Value -notmatch 'CancelNavigationCompletionWatchdog\(\)' -or
    $cancelActiveNavigationMethod.Value -notmatch 'CancelStatusProbeLoop\(\)' -or
    $cancelActiveNavigationMethod.Value -notmatch '_generations\.Next\(\)' -or
    $cancelActiveNavigationMethod.Value -notmatch '_currentNavigationId = NoCurrentNavigationId' -or
    $cancelActiveNavigationMethod.Value -notmatch '_messageOwnership\.BeginNavigation\(\)' -or
    $cancelActiveNavigationMethod.Value -notmatch 'CancelNavigationCancellation\(\)') {
    throw 'Stopping an in-flight navigation must cancel navigation watchdogs, probes, page ownership, and async navigation callbacks.'
}

$navigationStartingMethod = [regex]::Match($webViewServiceMain, 'private void OnNavigationStarting\([\s\S]*?\n    \}')
$navigationCompletedMethod = [regex]::Match($webViewServiceMain, 'private async Task HandleNavigationCompletedAsync\([\s\S]*?\n    \}')
if (-not $navigationStartingMethod.Success -or
    -not $navigationCompletedMethod.Success -or
    $navigationStartingMethod.Value -match 'ReferenceEquals\(sender, _coreWebView\)' -or
    $navigationCompletedMethod.Value -match 'ReferenceEquals\(sender, _coreWebView\)') {
    throw 'WebView2 navigation events must not be rejected by CoreWebView2 COM wrapper reference identity; rely on active host state, navigation id, and generation instead.'
}

if ($navigationCompletedMethod.Value -notmatch 'TryClaimNavigationCompleted\(sender, args, hostGeneration\)' -or
    $webViewServiceMain -notmatch 'TryClaimNavigationCompleted[\s\S]*_currentNavigationId == NoCurrentNavigationId[\s\S]*navigation\.starting\.recovered_from_completion') {
    throw 'WebView2 NavigationCompleted must be able to claim a current navigation when NavigationStarting was not delivered.'
}

if ($webViewServiceMain -notmatch 'NavigationStartTimeout' -or
    $webViewServiceMain -notmatch 'ObserveNavigationStartTimeout\(navigationGeneration, url\)' -or
    $webViewServiceMain -notmatch 'navigation\.start\.timeout' -or
    $webViewServiceMain -notmatch 'Navigation did not start within' -or
    $webViewServiceMain -notmatch 'NavigationStartTimedOut\?\.Invoke' -or
    $webViewServiceMain -notmatch 'CancelNavigationStartWatchdog\(\)' -or
    $webViewServiceMain -notmatch 'private async Task ObserveNavigationStartTimeoutAsync') {
    throw 'WebView navigation must have a bounded start watchdog so a missing WebView2 NavigationStarting/Completed callback cannot leave the shell stuck in Loading.'
}

$navigationStartTimeoutMethod = [regex]::Match($webViewServiceMain, 'private void HandleNavigationStartTimeout\([\s\S]*?\n    \}')
if (-not $navigationStartTimeoutMethod.Success -or
    $navigationStartTimeoutMethod.Value -match 'ReferenceEquals\(coreWebView, _coreWebView\)' -or
    $navigationStartTimeoutMethod.Value -match 'NavigationErrorOccurred\?\.Invoke|SetState\(ConnectionState\.Error\)' -or
    $navigationStartTimeoutMethod.Value -notmatch 'SetState\(ConnectionState\.Reconnecting\)[\s\S]*NavigationStartTimedOut\?\.Invoke') {
    throw 'Navigation-start timeout is a recoverable WebView2 startup stall and must request WebView recreation without relying on CoreWebView2 wrapper reference identity.'
}

if ($webViewServiceMain -notmatch 'NavigationCompletionTimeout' -or
    $webViewServiceMain -notmatch 'ObserveNavigationCompletionTimeout' -or
    $webViewServiceMain -notmatch 'NavigationCompletionTimedOut\?\.Invoke' -or
    $webViewServiceMain -notmatch 'CancelNavigationCompletionWatchdog\(\)' -or
    $webViewServiceMain -notmatch 'navigation\.completion\.timeout' -or
    $webViewServiceMain -notmatch 'Navigation did not complete within' -or
    $webViewServiceMain -notmatch 'private async Task ObserveNavigationCompletionTimeoutAsync') {
    throw 'WebView navigation must have a bounded completion watchdog so a started navigation cannot leave the shell stuck in Loading forever.'
}

$navigationStartingMethod = [regex]::Match($webViewServiceMain, 'private void OnNavigationStarting\([\s\S]*?\n    \}')
$navigationCompletionTimeoutMethod = [regex]::Match($webViewServiceMain, 'private void HandleNavigationCompletionTimeout\([\s\S]*?\n    \}')
if (-not $navigationStartingMethod.Success -or
    $navigationStartingMethod.Value -notmatch 'ObserveNavigationCompletionTimeout' -or
    -not $navigationCompletionTimeoutMethod.Success -or
    $navigationCompletionTimeoutMethod.Value -match 'ReferenceEquals\(coreWebView, _coreWebView\)' -or
    $navigationCompletionTimeoutMethod.Value -match 'NavigationErrorOccurred\?\.Invoke|SetState\(ConnectionState\.Error\)' -or
    $navigationCompletionTimeoutMethod.Value -notmatch 'SetState\(ConnectionState\.Reconnecting\)[\s\S]*NavigationCompletionTimedOut\?\.Invoke' -or
    $webViewServiceMain -notmatch '_activeNavigationCompletionWatchdogId' -or
    $navigationCompletionTimeoutMethod.Value -notmatch '_activeNavigationCompletionWatchdogId') {
    throw 'Navigation-completion timeout must start from NavigationStarting and request WebView recreation without relying on CoreWebView2 wrapper reference identity.'
}

if ($webViewServiceMain -notmatch '_hostGeneration' -or
    $webViewServiceMain -notmatch 'CreateNavigationStartingHandler\(hostGeneration\)' -or
    $webViewServiceMain -notmatch 'CreateNavigationCompletedHandler\(hostGeneration\)' -or
    $webViewServiceMain -notmatch 'CreateProcessFailedHandler\(hostGeneration\)' -or
    $webViewServiceMain -notmatch 'IsCurrentHost\(hostGeneration\)' -or
    $webViewServiceMain -notmatch 'CancelNavigationCompletionWatchdog\(\)[\s\S]*_generations\.Next\(\)[\s\S]*_messageOwnership\.BeginNavigation\(\)') {
    throw 'WebViewService must use an explicit host generation for WebView2 events/watchdogs and cancel both navigation watchdogs when detaching a host.'
}

if ($webViewServiceMain -match 'ObserveNavigation(Start|Completion)TimeoutAsync\(\s*CoreWebView2 coreWebView') {
    throw 'Navigation watchdog tasks must not capture CoreWebView2 wrapper instances after scheduling; validate by generation/id instead.'
}

$hostedUiBridge = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/HostedUiBridge.cs') -Raw
$hostedBridgeMessageMethod = [regex]::Match($hostedUiBridge, 'private void OnWebMessageReceived\([\s\S]*?\n    \}')
if ($hostedUiBridge -notmatch '_hostGeneration' -or
    $hostedUiBridge -notmatch 'CreateWebMessageReceivedHandler\(hostGeneration\)' -or
    -not $hostedBridgeMessageMethod.Success -or
    $hostedBridgeMessageMethod.Value -match 'ReferenceEquals\(sender, _coreWebView\)' -or
    $hostedBridgeMessageMethod.Value -notmatch 'IsCurrentHost\(hostGeneration\)') {
    throw 'HostedUiBridge must guard WebView messages with host generation and page ownership, not CoreWebView2 wrapper reference identity.'
}

$mainViewModelLifecycle = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/MainViewModel.Lifecycle.cs') -Raw
$mainViewModelStatus = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/MainViewModel.Status.cs') -Raw
$mainWindowWebView = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.WebView.cs') -Raw
if ($mainViewModelLifecycle -notmatch 'NavigationStartTimedOut \+= OnNavigationStartTimedOut' -or
    $mainViewModelLifecycle -notmatch 'NavigationStartTimedOut -= OnNavigationStartTimedOut' -or
    $mainViewModelStatus -notmatch 'private void OnNavigationStartTimedOut\(string message\)' -or
    $mainViewModelStatus -notmatch 'WebViewRecreationRequested\?\.Invoke\("navigation_start_timeout"\)' -or
    $mainViewModelStatus -notmatch 'navigation\.start\.timeout\.recovery_requested' -or
    $mainViewModelStatus -match 'OnNavigationStartTimedOut[\s\S]{0,600}ApplyNavigationError|OnNavigationStartTimedOut[\s\S]{0,600}ErrorOccurred\?\.Invoke|OnNavigationStartTimedOut[\s\S]{0,600}IsErrorVisible = true') {
    throw 'Navigation-start timeout must be handled by MainViewModel as a WebView recreation request without showing a first-hop Connection Issue.'
}

if ($mainViewModelStatus -match 'IsLoading = state is ConnectionState\.Loading or ConnectionState\.GatewayConnecting' -or
    $mainViewModelStatus -notmatch 'IsLoading = state == ConnectionState\.Loading') {
    throw 'The full-window loading overlay must clear after browser navigation; GatewayConnecting belongs in status text, not LoadingRing visibility.'
}

if ($mainViewModelLifecycle -notmatch 'NavigationCompletionTimedOut \+= OnNavigationCompletionTimedOut' -or
    $mainViewModelLifecycle -notmatch 'NavigationCompletionTimedOut -= OnNavigationCompletionTimedOut' -or
    $mainViewModelStatus -notmatch 'private void OnNavigationCompletionTimedOut\(string message\)' -or
    $mainViewModelStatus -notmatch 'WebViewRecreationRequested\?\.Invoke\("navigation_completion_timeout"\)' -or
    $mainViewModelStatus -notmatch 'navigation\.completion\.timeout\.recovery_requested' -or
    $mainViewModelStatus -match 'OnNavigationCompletionTimedOut[\s\S]{0,600}ApplyNavigationError|OnNavigationCompletionTimedOut[\s\S]{0,600}ErrorOccurred\?\.Invoke|OnNavigationCompletionTimedOut[\s\S]{0,600}IsErrorVisible = true') {
    throw 'Navigation-completion timeout must be handled by MainViewModel as a WebView recreation request without showing a first-hop Connection Issue.'
}

if ($mainWindowWebView -notmatch 'if \(!await WaitForWebViewHostLayoutAsync\(nextWebView, cancellationToken\)\)' -or
    $mainWindowWebView -notmatch 'private async Task<bool> WaitForWebViewHostLayoutAsync\(WebView2 webView, CancellationToken cancellationToken\)' -or
    $mainWindowWebView -notmatch 'webview\.host\.layout_ready' -or
    $mainWindowWebView -notmatch 'webview\.host\.layout_wait_timeout' -or
    $mainWindowWebView -notmatch 'webview\.recreation\.deferred_until_visible_layout' -or
    $mainWindowWebView -notmatch 'ScheduleWebViewRecreation\("visible_layout_ready"\)' -or
    $mainWindowWebView -notmatch 'webView\.Loaded \+=' -or
    $mainWindowWebView -notmatch 'WebViewHost\.ActualSize') {
    throw 'Dynamically recreated WebView2 hosts must defer initialization/navigation until Loaded and non-zero visible host layout are available.'
}

$recreationService = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewRecreationService.cs') -Raw
$circuitBreaker = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Services/WebViewCircuitBreaker.cs') -Raw
if ($recreationService -notmatch 'new\(\)' -or
    $recreationService -notmatch 'WebViewCircuitBreaker _circuitBreaker' -or
    $recreationService -notmatch 'TryBegin[\s\S]*_circuitBreaker\.CanAttempt\(\)' -or
    $recreationService -notmatch 'CircuitBreakerTripped\(LastReason, TotalRecreations\)' -or
    $recreationService -notmatch 'RecordAttempt\(\)[\s\S]*_circuitBreaker\.RecordAttempt\(\)' -or
    $mainWindowWebView -notmatch 'CanAttemptInLoop\(\)' -or
    $mainWindowWebView -notmatch 'webview\.recreation\.circuit_breaker_tripped_in_loop' -or
    $mainWindowWebView -notmatch 'ShowCircuitBreakerError\(\)' -or
    $circuitBreaker -notmatch 'maxAttempts = 5' -or
    $circuitBreaker -notmatch 'windowSeconds = 60') {
    throw 'WebView recreation must bound repeated navigation_start_timeout recovery attempts and surface circuit-breaker suppression.'
}

$directCoreNavigationCalls = [regex]::Matches($webViewServiceMain, 'coreWebView\.(Navigate|Reload)\(')
if ($directCoreNavigationCalls.Count -ne 2 -or
    $webViewServiceMain -notmatch 'private bool TryNavigateCoreWebView[\s\S]*coreWebView\.Navigate\(url\)' -or
    $webViewServiceMain -notmatch 'private bool TryReloadCoreWebView[\s\S]*coreWebView\.Reload\(\)') {
    throw 'CoreWebView2 Navigate/Reload calls must be centralized behind helpers that are paired with PrepareNavigationStart().'
}

if ($webViewServiceMain -notmatch 'await CaptureCurrentPageTokenAsync[\s\S]*if \(!IsCurrentNavigation' -or
    $webViewServiceMain -notmatch 'private bool IsCurrentNavigation') {
    throw 'Navigation completion must re-check WebView generation after awaiting page-token capture before publishing loaded state.'
}

if ($webViewServiceMain -notmatch 'ObserveSessionReadyReportRequest' -or
    $webViewServiceMain -notmatch 'reportSessionReady') {
    throw 'WebViewService must request a session-ready replay after accepting the hosted page token.'
}

$navigationCancellationScope = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/NavigationCancellationScope.cs') -Raw
$navigationCancellationLinks = [regex]::Matches($webViewServiceMain, 'CancellationTokenSource\.CreateLinkedTokenSource\(timeout\.Token, cancellationToken\)')
if ($webViewServiceMain -match '_retryCts|CancellationTokenSource\? _navigationCancellation|_navigationCancellation\?\.Dispose\(' -or
    $navigationCancellationScope -notmatch 'internal sealed class NavigationCancellationScope' -or
    $navigationCancellationScope -notmatch 'public Lease\? TryAcquire\(\)' -or
    $navigationCancellationScope -notmatch 'public void CancelAndRetire\(\)' -or
    $navigationCancellationScope -notmatch '_leaseCount\+\+' -or
    $navigationCancellationScope -notmatch '_source\.Cancel\(\)' -or
    $navigationCancellationScope -notmatch 'catch \(AggregateException\)' -or
    $navigationCancellationScope -notmatch 'finally[\s\S]*cancelLease\.Dispose\(\)' -or
    $navigationCancellationScope -notmatch 'sourceToDispose\?\.Dispose\(\)' -or
    $navigationCancellationScope -notmatch 'Interlocked\.Exchange') {
    throw 'WebView navigation cancellation must use a lease-owned scope, not a raw CTS that can be disposed while retry/replay work still holds its token.'
}

if ($webViewServiceMain -notmatch 'var navigationCancellation = _navigationCancellation' -or
    $webViewServiceMain -notmatch 'var navigationLease = navigationCancellation\?\.TryAcquire\(\)' -or
    $webViewServiceMain -notmatch 'CaptureCurrentPageTokenAsync\([\s\S]*CancellationToken cancellationToken = default' -or
    $navigationCancellationLinks.Count -lt 2 -or
    $webViewServiceMain -notmatch 'CaptureCurrentPageTokenAsync\([\s\S]*navigationLease\.Token' -or
    $webViewServiceMain -notmatch 'ObservePageTokenCaptureRetry\(sender, args\.NavigationId, completionGeneration, hostGeneration, navigationCancellation\)' -or
    $webViewServiceMain -notmatch 'ObserveSessionReadyReportRequest\(sender, args\.NavigationId, completionGeneration, hostGeneration, navigationCancellation\)' -or
    $webViewServiceMain -notmatch 'RetryPageTokenCaptureAsync\([\s\S]*NavigationCancellationScope\.Lease navigationLease' -or
    $webViewServiceMain -notmatch 'RequestSessionReadyReportAsync\([\s\S]*NavigationCancellationScope\.Lease navigationLease' -or
    $webViewServiceMain -notmatch 'Task\.Delay\(PageTokenCaptureRetryDelay, cancellationToken\)' -or
    $webViewServiceMain -notmatch 'catch \(OperationCanceledException\) when \(cancellationToken\.IsCancellationRequested\)' -or
    $webViewServiceMain -notmatch 'Hosted session-ready report request was interrupted by disposed resource' -or
    $webViewServiceMain -notmatch 'Hosted session-ready report request failed') {
    throw 'WebView page-token retry and session-ready replay work must carry the current navigation cancellation token and observe late failures.'
}

if ($webViewServiceMain -notmatch 'private async void OnNavigationCompleted[\s\S]*try[\s\S]*HandleNavigationCompletedAsync' -or
    $webViewServiceMain -notmatch 'private async Task HandleNavigationCompletedAsync' -or
    $webViewServiceMain -notmatch 'Navigation completion handling failed') {
    throw 'WebView navigation completion async event handling must be observed and logged.'
}

if ($webViewServiceMain -notmatch 'await Task\.Run\(\(\) => DeleteUserDataFolderForEnvironment\(environmentName, _logger\)\)') {
    throw 'Inactive WebView2 profile deletion must run off the UI thread.'
}

if ($webViewServiceMain -notmatch 'private void OnProcessFailed[\s\S]*CancelNavigationCancellation\(\)[\s\S]*_statusInspector\.SetUnavailableSnapshot') {
    throw 'WebView process-failure handling must retire navigation retry/replay cancellation before publishing unavailable state.'
}

if ($webViewServiceMain -notmatch 'TryAutoRetryAfterConnectionErrorAsync' -or
    $webViewServiceMain -notmatch 'AutoRetryOutcome\.Stale' -or
    $webViewServiceMain -notmatch 'AutoRetryOutcome\.Failed' -or
    $webViewServiceMain -notmatch 'AutoRetryOutcome\.NotAttempted' -or
    $webViewServiceMain -notmatch 'Auto-retry failed before WebView2 was ready' -or
    $webViewServiceMain -notmatch 'return AutoRetryOutcome\.Stale' -or
    $webViewServiceMain -notmatch 'SetState\(ConnectionState\.Error\)[\s\S]*NavigationErrorOccurred\?\.Invoke\("Auto-retry failed before WebView2 was ready\."\)' -or
    $webViewServiceMain -notmatch '_ when isConnectionError => ConnectionState\.Error' -or
    $webViewServiceMain -match '_ when isConnectionError => ConnectionState\.Reconnecting') {
    throw 'WebView auto-retry must not let stale navigation-completed continuations publish state, and exhausted or failed retries must surface as Error.'
}

$hostedBridgeMain = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/HostedUiBridge.cs') -Raw
if ($hostedBridgeMain -notmatch 'WebViewMessageOwnership' -or
    $hostedBridgeMain -notmatch 'IsCurrentHost\(hostGeneration\)' -or
    $hostedBridgeMain -notmatch 'TryCaptureCurrentVersion\(args, root, out var pageVersion\)' -or
    $hostedBridgeMain -notmatch 'IsCurrentAcceptedPageVersion\(pageVersion\)' -or
    $hostedBridgeMain -notmatch 'HostedUiBridgeScript\.Build\(_messageOwnership\.OwnerToken\)') {
    throw 'HostedUiBridge must inject and validate WebView message ownership tokens.'
}

if ($hostedBridgeMain -notmatch 'public void DetachCurrentWebView\(\)') {
    throw 'HostedUiBridge must expose a non-disposing detach path for WebView recreation.'
}

if ($hostedBridgeMain -notmatch '_documentCreatedScriptId' -or
    $hostedBridgeMain -notmatch 'RemoveScriptToExecuteOnDocumentCreated') {
    throw 'HostedUiBridge must remove its document-created script when detaching a WebView2 instance.'
}

$hostMessagingScript = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/HostedUiBridge.HostMessaging.js') -Raw
if ($hostMessagingScript -notmatch 'nativeOwnerToken' -or
    $hostMessagingScript -notmatch 'nativePageToken' -or
    $hostMessagingScript -notmatch 'setOwnerToken') {
    throw 'Hosted bridge host messages must include native owner and page tokens.'
}

foreach ($asset in @('WebViewCommands.StopInjection.js', 'WebViewCommands.AbortRun.js')) {
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot "src/OpenClaw/Services/$asset"))) {
        throw "Missing WebView command script asset: $asset"
    }
}

$mainWindowWebView = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.WebView.cs') -Raw
if ($mainWindowWebView -match '_webViewRecreationMergedCount|_pendingWebViewRecreationReason|_isRecreatingWebView') {
    throw 'WebView recreation scheduling state must live in WebViewRecreationService.'
}

if ($mainWindowWebView -notmatch 'CancellationToken' -or
    $mainWindowWebView -notmatch 'WebView2 recreation cancelled') {
    throw 'WebView recreation must be cancellable during window shutdown.'
}

if ($mainWindowWebView -match '_ = RecreateWebViewAsync' -or
    $mainWindowWebView -notmatch '_webViewRecreationTask' -or
    $mainWindowWebView -notmatch 'ObserveWebViewRecreationAsync') {
    throw 'WebView recreation tasks must be tracked and observed instead of discarded fire-and-forget.'
}

if ($mainWindowWebView -notmatch 'ViewModel\.DetachWebViewHost\(\)[\s\S]*foreach \(var child in WebViewHost\.Children\.OfType<WebView2>\(\)\.ToArray\(\)\)') {
    throw 'MainWindow must detach WebView services before closing old WebView2 controls during recreation.'
}

$mainViewModelLifecycle = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/MainViewModel.Lifecycle.cs') -Raw
if ($mainViewModelLifecycle -notmatch '_lifetimeCts' -or
    $mainViewModelLifecycle -notmatch 'InitializeAsync\(webView, _selectedEnvironment\.Name, cancellationToken\)' -or
    $mainViewModelLifecycle -notmatch 'InitializeAsync\(webView, cancellationToken\)') {
    throw 'MainViewModel WebView initialization must honor the ViewModel lifetime cancellation token.'
}

if ($mainViewModelLifecycle -notmatch 'public void DetachWebViewHost\(\)') {
    throw 'MainViewModel must expose a WebView host detach path for recreation before old controls are closed.'
}

$mainWindowLifecycle = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.Lifecycle.cs') -Raw
if ($mainWindowLifecycle -notmatch 'private async void OnWindowVisibleAsync\(\)[\s\S]*try[\s\S]*NotifyHostVisibleAsync[\s\S]*catch \(Exception ex\)') {
    throw 'Foreground resume async void handler must catch and log exceptions.'
}

$mainWindowCommands = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.Commands.cs') -Raw
$mainWindowShared = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.Shared.cs') -Raw
if ($mainWindowShared -notmatch '_isLogViewerOpen' -or
    $mainWindowCommands -notmatch 'private async void OnViewLogsRequested\(\)[\s\S]*try[\s\S]*await ShowLogViewerAsync\(\)[\s\S]*catch \(Exception ex\)' -or
    $mainWindowCommands -notmatch '_isLogViewerOpen[\s\S]*ShowAsync') {
    throw 'Log viewer async dialog entry must guard reentry and log failures.'
}

if ($mainWindowShared -notmatch '_isAboutDialogOpen' -or
    $mainWindowCommands -notmatch 'private async void OnAboutClick[\s\S]*try[\s\S]*await ShowAboutDialogAsync\(\)[\s\S]*catch \(Exception ex\)' -or
    $mainWindowCommands -notmatch '_isAboutDialogOpen[\s\S]*ShowAsync') {
    throw 'About dialog async entry must guard reentry and log failures.'
}

$settingsDialogActions = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Views/SettingsDialog.Actions.cs') -Raw
$viewLogsHandler = [regex]::Match(
    $settingsDialogActions,
    'private void OnViewLogsClick[\s\S]*?\n    \}').Value
if ($viewLogsHandler -notmatch 'var mainViewModel = MainViewModel;[\s\S]*this\.Close\(\);[\s\S]*mainViewModel\?\.ViewLogsCommand\.Execute\(null\)' -or
    $viewLogsHandler -match 'ViewLogsCommand\.Execute\(null\)[\s\S]*this\.Close\(\)') {
    throw 'Settings View Logs action must close Settings before showing the main-window log dialog.'
}

if ($settingsDialogActions -notmatch 'private async void OnClearEnvironmentSessionClick[\s\S]*try[\s\S]*await ClearEnvironmentSessionAsync' -or
    $settingsDialogActions -notmatch 'private async Task ClearEnvironmentSessionAsync' -or
    $settingsDialogActions -notmatch 'button\.IsEnabled = false[\s\S]*finally[\s\S]*button\.IsEnabled = true' -or
    $settingsDialogActions -notmatch 'SettingsSessionResetFailedFormat') {
    throw 'Settings session clear async handler must guard reentry, catch failures, and report localized errors.'
}

foreach ($resourceFile in @(
    'src/OpenClaw/Strings/en-us/Resources.resw',
    'src/OpenClaw/Strings/zh-cn/Resources.resw'
)) {
    $resources = Get-Content -LiteralPath (Join-Path $repoRoot $resourceFile) -Raw
    if ($resources -notmatch 'name="SettingsSessionResetFailedFormat"') {
        throw "Missing localized session reset failure resource: $resourceFile"
    }
}

$coordinatorEvents = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Services/ShellSessionCoordinator.Events.cs') -Raw
if ($coordinatorEvents -match 'async void') {
    throw 'ShellSessionCoordinator event handlers must use SafeFireAndForget instead of async void.'
}

if ($coordinatorEvents -notmatch 'SafeFireAndForget' -or $coordinatorEvents -notmatch 'RunObservedAsync') {
    throw 'ShellSessionCoordinator fire-and-forget recovery paths must be observed and logged.'
}

if ($coordinatorEvents -notmatch 'Func<CancellationToken, Task>' -or
    $coordinatorEvents -notmatch 'CreateObservedOperationCancellation' -or
    $coordinatorEvents -notmatch 'ReleaseObservedOperationCancellation' -or
    $coordinatorEvents -notmatch 'CancelObservedOperations' -or
    $coordinatorEvents -notmatch '_observedOperationCancellations\.ToArray\(\)' -or
    $coordinatorEvents -notmatch 'cancellation\.Cancel\(\)' -or
    $coordinatorEvents -match 'Func<Task> operation') {
    throw 'ShellSessionCoordinator observed recovery tasks must own cancellable operation CTS instances so detach/dispose can cancel pending inspections.'
}

$coordinatorAttach = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Services/ShellSessionCoordinator.Attach.cs') -Raw
if ($coordinatorAttach -notmatch 'AttachAsync[\s\S]*DetachServicesCore\(\)' -or
    $coordinatorAttach -notmatch 'DetachServices\(\)[\s\S]*DetachServicesCore\(\)' -or
    $coordinatorAttach -notmatch 'DetachServicesCore\(\)[\s\S]*CancelObservedOperations\(\)[\s\S]*AbortRecoveryOperation\(\)') {
    throw 'ShellSessionCoordinator attach/detach/dispose paths must cancel observed recovery operations before replacing WebView/bridge services.'
}

$coordinatorStateEffects = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Services/ShellSessionCoordinator.StateEffects.cs') -Raw
if ($coordinatorStateEffects -match '_ = RequestHardRefreshAsync|_ = RequestSoftResyncAsync') {
    throw 'ShellSessionCoordinator state effects must observe fire-and-forget recovery tasks.'
}

if ($coordinatorStateEffects -notmatch 'SafeFireAndForget\(\s*async token' -or
    $coordinatorStateEffects -notmatch 'token\.ThrowIfCancellationRequested\(\)[\s\S]*RequestSoftResyncAsync' -or
    $coordinatorStateEffects -notmatch 'token\.ThrowIfCancellationRequested\(\)[\s\S]*RequestHardRefreshAsync') {
    throw 'ShellSessionCoordinator stale-busy recovery tasks must observe cancellation before starting recovery operations.'
}

$coordinatorHost = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Services/ShellSessionCoordinator.Host.cs') -Raw
if ($coordinatorHost -notmatch 'OnHostVisibleAsync\(CancellationToken cancellationToken = default\)' -or
    $coordinatorHost -notmatch 'CreateObservedOperationCancellation\(\)' -or
    $coordinatorHost -notmatch 'CreateLinkedTokenSource' -or
    $coordinatorHost -notmatch 'RequiresBackgroundReconnectAsync\(cancellationToken\)' -or
    $coordinatorHost -notmatch 'ReleaseObservedOperationCancellation\(operationCancellation\)' -or
    $coordinatorHost -notmatch 'Reset\(\)[\s\S]*CancelObservedOperations\(\)[\s\S]*AbortRecoveryOperation\(\)') {
    throw 'ShellSessionCoordinator foreground-resume inspections must be cancellable and reset must cancel observed recovery operations.'
}

$coordinatorRecoveryLifecycle = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Services/ShellSessionCoordinator.RecoveryLifecycle.cs') -Raw
if ($coordinatorRecoveryLifecycle -match 'AbortRecoveryOperation[\s\S]*cancellationSource\?\.Dispose\(') {
    throw 'ShellSessionCoordinator abort paths must cancel recovery CTS and leave disposal to the running operation.'
}

if ($coordinatorRecoveryLifecycle -notmatch 'TryStartRecoveryOperation\([\s\S]*CancellationToken cancellationToken' -or
    $coordinatorRecoveryLifecycle -notmatch 'cancellationToken\.ThrowIfCancellationRequested\(\)' -or
    $coordinatorRecoveryLifecycle -notmatch 'PrepareRecoveryCancellationSource\(cancellationToken\)' -or
    $coordinatorRecoveryLifecycle -notmatch 'private CancellationTokenSource PrepareRecoveryCancellationSource\(CancellationToken cancellationToken\)' -or
    $coordinatorRecoveryLifecycle -notmatch 'CancellationTokenSource\.CreateLinkedTokenSource\(cancellationToken\)') {
    throw 'ShellSessionCoordinator recovery operation CTS must link the caller cancellation token before any inspection, bridge command, or reload is queued.'
}

$prepareRecoveryCts = [regex]::Match(
    $coordinatorRecoveryLifecycle,
    'private CancellationTokenSource PrepareRecoveryCancellationSource\(CancellationToken cancellationToken\)[\s\S]*?private int RegisterRecoveryOperationStart').Value
if ($prepareRecoveryCts -match '_recoveryCts\?\.Dispose\(') {
    throw 'ShellSessionCoordinator recovery CTS replacement must cancel the previous operation and leave disposal to that operation.'
}

$settingsViewModel = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/SettingsViewModel.cs') -Raw
if ($settingsViewModel -match 'using OpenClaw\.Views;') {
    throw 'SettingsViewModel must not depend on the Views namespace for SettingsSaveResult.'
}

if ($settingsViewModel -match 'App\.Configuration') {
    throw 'SettingsViewModel must use SettingsPersistenceAdapter instead of App.Configuration.'
}

if ($settingsViewModel -match 'App\.Logger') {
    throw 'SettingsViewModel must use SettingsPersistenceAdapter instead of App.Logger.'
}

if ($settingsViewModel -notmatch '_didEditAlwaysOnTop' -or
    $settingsViewModel -notmatch '_didEditEnableGlobalHotkey' -or
    $settingsViewModel -notmatch '_didEditGlobalHotkey' -or
    $settingsViewModel -notmatch '_didEditAllowMultipleInstances' -or
    $settingsViewModel -notmatch 'private void ApplyChangedShellSettings\(AppSettings settings\)' -or
    $settingsViewModel -notmatch 'if \(_didEditAlwaysOnTop\)[\s\S]*settings\.AlwaysOnTop = AlwaysOnTop' -or
    $settingsViewModel -notmatch 'if \(_didEditEnableGlobalHotkey\)[\s\S]*settings\.EnableGlobalHotkey = EnableGlobalHotkey' -or
    $settingsViewModel -notmatch 'if \(_didEditGlobalHotkey\)[\s\S]*settings\.GlobalHotkey = NormalizeHotkey\(GlobalHotkey\)' -or
    $settingsViewModel -notmatch 'if \(_didEditAllowMultipleInstances\)[\s\S]*settings\.AllowMultipleInstances = AllowMultipleInstances') {
    throw 'SettingsViewModel must merge only fields edited in the open Settings window so stale snapshots cannot overwrite live shell changes.'
}

foreach ($property in @(
    'SelectedLanguage',
    'EnableDevLog',
    'MinimizeToTray',
    'CloseToTray',
    'AllowMultipleInstances',
    'EnableGlobalHotkey',
    'GlobalHotkey',
    'AlwaysOnTop'
)) {
    $setter = [regex]::Match(
        $settingsViewModel,
        "public [^{]+ $property[\s\S]*?set\s*\{[\s\S]*?\n    \}").Value
    if ([string]::IsNullOrWhiteSpace($setter) -or $setter -notmatch 'return;[\s\S]*_didEdit') {
        throw "SettingsViewModel.$property must ignore same-value setter writes before marking the field dirty."
    }
}

if ($settingsViewModel -match 'settings\.SelectedEnvironmentName = persistedSelection') {
    throw 'SettingsViewModel must not restore the originally selected environment name from a stale Settings snapshot.'
}

$settingsPersistenceAdapter = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/SettingsPersistenceAdapter.cs') -Raw
if ($settingsPersistenceAdapter -match 'App\.Configuration|App\.Logger') {
    throw 'SettingsPersistenceAdapter must receive ConfigurationService and logger explicitly.'
}

$configurationService = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Services/ConfigurationService.cs') -Raw
if ($configurationService -match '_ = Task\.Run\(ProcessDeferredSaveQueueAsync\)' -or
    $configurationService -match 'Interlocked\.CompareExchange\(ref _saveQueued' -or
    $configurationService -match 'Volatile\.Read\(ref _saveVersion' -or
    $configurationService -notmatch '_deferredSaveTask' -or
    $configurationService -notmatch '_deferredSaveCts' -or
    $configurationService -notmatch 'TryStartDeferredSaveWorker' -or
    $configurationService -notmatch 'TryCompleteDeferredSaveBatch' -or
    $configurationService -notmatch 'CancelDeferredSaveWorker' -or
    $configurationService -notmatch 'ProcessDeferredSaveQueueAsync\(CancellationToken' -or
    $configurationService -notmatch 'ObserveDeferredSaveWorkerShutdownAsync') {
    throw 'ConfigurationService deferred-save worker must own and cancel its task, and serialize coalesced save versions under one lifetime gate.'
}

$settingsDialogShared = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Views/SettingsDialog.Shared.cs') -Raw
if ($settingsDialogShared -match 'record struct SettingsSaveResult') {
    throw 'SettingsSaveResult must live in OpenClaw.Core models, not SettingsDialog.Shared.cs.'
}

$settingsDialogTheme = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Views/SettingsDialog.Theme.cs') -Raw
if ($settingsDialogTheme -notmatch 'ReloadFromCurrentSettings') {
    throw 'Prewarmed SettingsDialog must reload its ViewModel from current persisted settings before activation.'
}

$compact = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.CompactMode.cs') -Raw
if ($compact -match 'TopStatusPill\.MinWidth|ModelStatusSegment\.MinWidth|EnvironmentSummaryGroup\.Visibility|LatencyBadge\.Visibility') {
    throw 'Compact top-bar layout should be driven by XAML visual states, not code-behind property patching.'
}

$windowStatePattern = 'VisualStateManager\.GoToState\(\s*this'
if ($compact -match $windowStatePattern) {
    throw 'MainWindow compact mode must switch RootLayout, not the Window instance.'
}

if ($compact -notmatch 'VisualStateManager\.GoToState\(\s*RootLayout') {
    throw 'MainWindow compact mode must switch the RootLayout visual state owner.'
}

if ($compact -notmatch 'UpdateLoadingRingVisibility' -or
    $compact -notmatch 'LoadingRing\.Visibility = !_isCompactMode && ViewModel\.IsLoading' -or
    $compact -match 'FindName\("LoadingRing"\)[\s\S]*Visibility = visibility') {
    throw 'Compact mode must keep LoadingRing visibility derived from compact state and current loading state, not one-time SetCompactVisibility overrides.'
}

if ($compact -notmatch 'RestoreCompactWindowPosition' -or
    $compact -notmatch 'WindowBoundsUtilities\.HasSavedPosition\(settings\.CompactWindowLeft, settings\.CompactWindowTop\)' -or
    $compact -notmatch 'WindowBoundsUtilities\.IsVisibleWithinAnyWorkArea\(left, top, CompactWidth, CompactHeight, GetDisplayWorkAreas\(\)\)' -or
    $compact -notmatch 'TryCenterInWorkArea\(CompactWidth, CompactHeight') {
    throw 'Compact window position restore must validate current display work areas and center fallback when saved compact bounds are stale.'
}

$mainWindowLifecycle = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.Lifecycle.cs') -Raw
if ($mainWindowLifecycle -notmatch '_isCompactMode[\s\S]*SaveCompactWindowPosition') {
    throw 'Normal window bounds must not be persisted while compact mode is active.'
}

$mainWindowXaml = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.xaml') -Raw
if ($mainWindowXaml -notmatch 'x:Name="RootLayout"[\s\S]*VisualStateManager\.VisualStateGroups') {
    throw 'Compact visual states must be attached to RootLayout.'
}

if ($mainWindowXaml -match 'LoadingRing[\s\S]*Visibility="\{x:Bind ViewModel\.LoadingVisibility' -or
    $mainWindowXaml -notmatch '<Setter Target="LoadingRing\.Visibility" Value="Collapsed" />') {
    throw 'Compact visual state must own LoadingRing collapse so loading-state changes cannot re-show it in compact mode.'
}

if ($mainWindowXaml -match 'ToolTipService\.ToolTip="Always on Top"' -or
    $mainWindowXaml -notmatch 'x:Name="PinButton"[\s\S]*ToolTipService\.ToolTip="\{x:Bind helpers:StringResources\.SettingsAlwaysOnTop\}"') {
    throw 'Pin button tooltip must use localized StringResources text.'
}

if ($mainWindowXaml -notmatch '<VisualState x:Name="CompactMode"[\s\S]*EnvironmentSummaryGroup\.Visibility[\s\S]*LatencyBadge\.Visibility[\s\S]*HeartbeatSummarySegment\.Visibility[\s\S]*HeartbeatIndicatorSegment\.Visibility[\s\S]*AccessStatusSegment\.Visibility[\s\S]*RunIndicatorSegment\.Visibility[\s\S]*ThemeSwitcherContainer\.Visibility[\s\S]*AboutButton\.Visibility' -or
    $mainWindowXaml -notmatch 'x:Name="TopStatusContent"' -or
    $mainWindowXaml -notmatch 'x:Name="HeartbeatSummarySegment"' -or
    $mainWindowXaml -notmatch 'x:Name="HeartbeatIndicatorSegment"' -or
    $mainWindowXaml -notmatch 'x:Name="RunIndicatorSegment"') {
    throw 'Compact mode must collapse nonessential fixed-width top-bar segments and nonessential title actions so 480px layout does not clip.'
}

$mainBridgeScript = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/HostedUiBridge.Script.js') -Raw
$mainBridgeLines = ($mainBridgeScript -split "`n").Count
if ($mainBridgeLines -gt 250) {
    throw "HostedUiBridge.Script.js should be a composition file under 250 lines; found $mainBridgeLines."
}

foreach ($asset in @(
    'HostedUiBridge.DomUtilities.js',
    'HostedUiBridge.ModelDomFallback.js',
    'HostedUiBridge.ActivityState.js',
    'HostedUiBridge.PhaseClassifier.js',
    'HostedUiBridge.StatusInspection.js',
    'HostedUiBridge.CommandDispatch.js',
    'HostedUiBridge.MutationFilter.js',
    'HostedUiBridge.HostMessaging.js'
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot "src/OpenClaw/Services/$asset"))) {
        throw "Missing focused bridge asset: $asset"
    }
}

foreach ($placeholder in @(
    '__OPENCLAW_OWNER_TOKEN_JSON__',
    '__OPENCLAW_HOST_MESSAGING_SCRIPT__',
    '__OPENCLAW_MUTATION_FILTER_SCRIPT__',
    '__OPENCLAW_MODEL_RESOLVER_SCRIPT__',
    '__OPENCLAW_DOM_UTILITIES_SCRIPT__',
    '__OPENCLAW_MODEL_DOM_FALLBACK_SCRIPT__',
    '__OPENCLAW_ACTIVITY_STATE_SCRIPT__',
    '__OPENCLAW_PHASE_CLASSIFIER_SCRIPT__',
    '__OPENCLAW_STATUS_INSPECTION_SCRIPT__',
    '__OPENCLAW_COMMAND_DISPATCH_SCRIPT__'
)) {
    if ($mainBridgeScript -notmatch [regex]::Escape($placeholder)) {
        throw "HostedUiBridge.Script.js is missing composition placeholder: $placeholder"
    }
}

$statusInspectionScript = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/HostedUiBridge.StatusInspection.js') -Raw
$statusInspectionLines = ($statusInspectionScript -split "`n").Count
if ($statusInspectionLines -gt 300) {
    throw "HostedUiBridge.StatusInspection.js should be a composition file under 300 lines; found $statusInspectionLines."
}

$dependencies = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Services/ShellSessionCoordinator.Dependencies.cs') -Raw
if ($dependencies -notmatch 'Task<bool> ReloadAsync\(CancellationToken cancellationToken\)' -or
    $dependencies -match 'Task ReloadAsync\(\)') {
    throw 'ShellSessionCoordinator reload dependency must return whether hard refresh actually started and accept the active recovery cancellation token.'
}

if ($dependencies -notmatch 'Task<ControlUiProbeSnapshot> InspectControlUiStateAsync\(CancellationToken cancellationToken\)') {
    throw 'ShellSessionCoordinator inspections must accept the active recovery cancellation token.'
}

if ($dependencies -notmatch 'RequestSessionRefreshAsync\(CancellationToken cancellationToken\)' -or
    $dependencies -notmatch 'RequestRecentMessagesAsync\(CancellationToken cancellationToken\)' -or
    $dependencies -notmatch 'RequestLightweightSyncAsync\(CancellationToken cancellationToken\)' -or
    $dependencies -notmatch 'NotifyReconnectIntentAsync\(CancellationToken cancellationToken\)') {
    throw 'ShellSessionCoordinator bridge recovery commands must accept the active recovery cancellation token.'
}

$recovery = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Services/ShellSessionCoordinator.Recovery.cs') -Raw
if ($recovery -notmatch 'RequestReconnectAsync\(string reason, CancellationToken cancellationToken = default\)' -or
    $recovery -notmatch 'RequestSoftResyncAsync\(string reason, CancellationToken cancellationToken = default\)' -or
    $recovery -notmatch 'RequestHardRefreshAsync\(string reason, CancellationToken cancellationToken = default\)' -or
    $recovery -notmatch 'TryStartRecoveryOperation\(RecoveryOperationKind\.Reconnect, reason, cancellationToken\)' -or
    $recovery -notmatch 'TryStartRecoveryOperation\(RecoveryOperationKind\.SoftResync, reason, cancellationToken\)' -or
    $recovery -notmatch 'TryStartRecoveryOperation\(RecoveryOperationKind\.HardRefresh, reason, cancellationToken\)') {
    throw 'ShellSessionCoordinator public recovery requests must link the caller cancellation token into the active recovery operation.'
}

if ($recovery -notmatch 'var reloadStarted = await webViewService\.ReloadAsync\(operation\.CancellationToken\)' -or
    $recovery -notmatch 'if \(!reloadStarted\)[\s\S]*MarkRecoveryFailed\(') {
    throw 'ShellSessionCoordinator recovery must pass cancellation to reload and must not treat a no-op WebView reload as successful recovery.'
}

if ($recovery -notmatch 'InspectControlUiStateAsync\(operation\.CancellationToken\)') {
    throw 'ShellSessionCoordinator recovery inspections must pass the active operation cancellation token.'
}

if ($recovery -notmatch 'NotifyReconnectIntentAsync\(operation\.CancellationToken\)' -or
    $recovery -notmatch 'RequestSessionRefreshAsync\(operation\.CancellationToken\)' -or
    $recovery -notmatch 'RequestLightweightSyncAsync\(operation\.CancellationToken\)' -or
    $recovery -notmatch 'RequestRecentMessagesAsync\(operation\.CancellationToken\)') {
    throw 'ShellSessionCoordinator recovery bridge commands must pass the active operation cancellation token.'
}

$coordinatorRecoveryInspection = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Services/ShellSessionCoordinator.RecoveryInspection.cs') -Raw
if ($coordinatorRecoveryInspection -match 'CancellationToken\.None' -or
    $coordinatorRecoveryInspection -notmatch 'GetPreferredGapRecoveryAsync\(CancellationToken cancellationToken\)' -or
    $coordinatorRecoveryInspection -notmatch 'RequiresBackgroundReconnectAsync\(CancellationToken cancellationToken\)' -or
    $coordinatorRecoveryInspection -notmatch 'InspectControlUiStateAsync\(cancellationToken\)' -or
    $coordinatorRecoveryInspection -notmatch 'catch \(OperationCanceledException\)[\s\S]*throw;') {
    throw 'ShellSessionCoordinator gap/background-resume inspections must pass cancellation through and must not swallow cancellation as recovery fallback.'
}

$adapter = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/ShellSessionCoordinator.Adapters.cs') -Raw
if ($adapter -match 'App\.Configuration|App\.Logger') {
    throw 'ShellSessionCoordinator adapter must receive configuration and logger explicitly.'
}

if ($adapter -notmatch 'UiTaskDispatcher' -or
    $adapter -notmatch 'public Task<bool> ReloadAsync\(CancellationToken cancellationToken\)' -or
    $adapter -notmatch '_dispatcher\.RunAsync\(_inner\.Reload, cancellationToken\)' -or
    $adapter -match 'Task\.FromResult\(_inner\.Reload\(\)\)') {
    throw 'ShellSessionCoordinator app adapters must marshal WebView2 and bridge calls through the UI dispatcher.'
}

if ($adapter -notmatch 'RequestSessionRefreshAsync\(CancellationToken cancellationToken\)[\s\S]*_dispatcher\.RunAsync\(\(\) => _inner\.RequestSessionRefreshAsync\(cancellationToken\), cancellationToken\)' -or
    $adapter -notmatch 'RequestRecentMessagesAsync\(CancellationToken cancellationToken\)[\s\S]*_dispatcher\.RunAsync\(\(\) => _inner\.RequestRecentMessagesAsync\(cancellationToken\), cancellationToken\)' -or
    $adapter -notmatch 'RequestLightweightSyncAsync\(CancellationToken cancellationToken\)[\s\S]*_dispatcher\.RunAsync\(\(\) => _inner\.RequestLightweightSyncAsync\(cancellationToken\), cancellationToken\)' -or
    $adapter -notmatch 'NotifyReconnectIntentAsync\(CancellationToken cancellationToken\)[\s\S]*_dispatcher\.RunAsync\(\(\) => _inner\.NotifyReconnectIntentAsync\(cancellationToken\), cancellationToken\)') {
    throw 'ShellSessionCoordinator bridge adapter must cancel queued UI bridge commands when the recovery operation is cancelled.'
}

$webViewInspection = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.ControlUiInspection.cs') -Raw
if ($webViewInspection -notmatch '_uiDispatcher\.RunAsync\(\(\) => _statusInspector\.InspectAsync\(cancellationToken, publishSnapshot\), cancellationToken\)') {
    throw 'WebViewService public Control UI inspection entry must marshal WebView2 work through the UI dispatcher.'
}

$uiTaskDispatcher = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/UiTaskDispatcher.cs') -Raw
if ($uiTaskDispatcher -notmatch 'Func<Action, bool>' -or $uiTaskDispatcher -match 'Action<Action>') {
    throw 'UiTaskDispatcher must use a strict try-dispatch contract so failed UI enqueue does not execute WebView2 work on a background thread.'
}

if ($uiTaskDispatcher -match 'Func<Action, bool>\? dispatch|dispatch = null|action\(\);\s*return true;') {
    throw 'UiTaskDispatcher must require an explicit dispatcher instead of defaulting to inline execution.'
}

if ($uiTaskDispatcher -notmatch 'public Task<T> RunAsync<T>\(Func<T> action, CancellationToken cancellationToken\)' -or
    $uiTaskDispatcher -notmatch 'CancellationTokenRegistration' -or
    $uiTaskDispatcher -notmatch 'cancellationToken\.IsCancellationRequested' -or
    $uiTaskDispatcher -notmatch 'TrySetCanceled\(cancellationToken\)') {
    throw 'UiTaskDispatcher cancellable dispatch must observe cancellation before or while queued UI work runs.'
}

$mainWindow = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.xaml.cs') -Raw
if ($mainWindow -match 'if \(!DispatcherQueue\.TryEnqueue[\s\S]*?\{\s*action\(\);') {
    throw 'MainWindow must not run UI dispatcher fallbacks inline after DispatcherQueue.TryEnqueue fails.'
}

$mainViewModel = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/MainViewModel.cs') -Raw
if ($mainViewModel -match 'if \(dispatcher is null \|\| !dispatcher\.TryEnqueue[\s\S]*?\{\s*action\(\);') {
    throw 'MainViewModel must not run UI dispatcher fallbacks inline when the Window dispatcher is unavailable.'
}

if ($mainViewModel -match 'App\.MainWindow|DispatchThroughMainWindow|Func<Action, bool>\? dispatchToUi|dispatchToUi = null') {
    throw 'MainViewModel must require an injected UI dispatcher instead of reading App.MainWindow.'
}

if ($mainViewModel -notmatch 'DispatchUiUpdate\(Action action\)[\s\S]*_dispatchToUi\(\(\) => RunUiUpdate\(action\)\)' -or
    $mainViewModel -notmatch 'private void RunUiUpdate\(Action action\)[\s\S]*try[\s\S]*action\(\)[\s\S]*catch \(Exception ex\)[\s\S]*View-model UI update failed') {
    throw 'MainViewModel injected UI dispatch callbacks must catch and log update exceptions.'
}

$viewModelStatus = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/MainViewModel.Status.cs') -Raw
if ($viewModelStatus -match 'App\.MainWindow|RunOnUiThread') {
    throw 'MainViewModel status updates must use injected UI dispatcher.'
}

if ($viewModelStatus -match 'ShouldClearModelSummary[\s\S]*ControlUiPhase\.Unavailable|ShouldClearModelSummary[\s\S]*ControlUiPhase\.Unknown') {
    throw 'Transient unavailable/unknown Control UI inspections must not clear the last non-empty MODEL summary.'
}

if ($viewModelStatus -match 'ShouldClearModelSummary[\s\S]*ControlUiPhase\.Loading') {
    throw 'Transient loading/navigation Control UI snapshots must not clear the last non-empty MODEL summary.'
}

$webViewInspection = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.ControlUiInspection.cs') -Raw
if ($webViewInspection -notmatch 'case ControlUiPhase\.Unavailable:[\s\S]*SetState\(ConnectionState\.Reconnecting\)') {
    throw 'Unavailable Control UI inspections must downgrade the shell from stale Connected state.'
}

$mainViewModelLifecycle = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/MainViewModel.Lifecycle.cs') -Raw
if ($mainViewModelLifecycle -notmatch 'ShouldRunHeartbeatForCurrentState\(\)' -or
    $mainViewModelLifecycle -notmatch 'ControlUiPhase\.Unavailable' -or
    $mainViewModelLifecycle -notmatch 'ConnectionState\.Reconnecting') {
    throw 'Resource scheduling must keep heartbeat alive for owned Unavailable/Reconnecting hosted-session states.'
}

$viewModelIndicators = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/MainViewModel.Indicators.cs') -Raw
if ($viewModelIndicators -notmatch 'IsLatencySnapshotForSelectedEnvironment' -or
    $viewModelIndicators -notmatch 'TryGetEnvironmentHost') {
    throw 'MainViewModel latency updates must reject stale snapshots from non-selected environment hosts.'
}

$presenter = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/StatusPresenter.cs') -Raw
if ($presenter -notmatch 'ControlUiLatencyState\.Failure' -or
    $presenter -notmatch 'ERR') {
    throw 'Latency presentation must show a distinct failure state instead of leaving ping at the default placeholder after probe failures.'
}

$mainViewModelFiles = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels') -File -Filter 'MainViewModel*.cs'
foreach ($file in $mainViewModelFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    if ($content -match 'App\.Logger|App\.Configuration') {
        throw "MainViewModel must use AppRuntimeContext instead of App globals: $($file.Name)"
    }

    if ($content -match 'App\.MainWindow') {
        throw "MainViewModel must use the injected UI dispatcher instead of App.MainWindow: $($file.Name)"
    }
}

$fields = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/MainViewModel.Fields.cs') -Raw
if ($fields -match 'CreateBrush\(|new SolidColorBrush\(Color\.FromArgb') {
    throw 'MainViewModel must use theme-aware status brush resources.'
}

if ($presenter -match 'App\.Configuration|App\.MainWindow|SetProperty\(') {
    throw 'StatusPresenter must stay pure presentation logic.'
}

$coreProperties = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/MainViewModel.Core.Properties.cs') -Raw
if ($coreProperties -match 'public WebViewService|public HostedUiBridge|public ShellSessionCoordinator') {
    throw 'MainViewModel service properties must not be public.'
}

$commands = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/MainViewModel.Commands.cs') -Raw
if ($commands -match 'WebView recovery is temporarily paused|WebView2 recreation failed repeatedly') {
    throw 'Circuit breaker user-facing text must come from StringResources.'
}

if ($commands -notmatch 'Task\.Run\(\(\) => DiagnosticBundleService\.ExportBundleAsync') {
    throw 'Diagnostic bundle export must run log enumeration/compression off the UI thread.'
}

if ($commands -match 'private void OnRetry\(\)\s*\{\s*IsErrorVisible = false;\s*_webViewService\.RetryNavigation\(\);') {
    throw 'Manual retry must not hide the visible error before confirming that retry navigation started.'
}

if ($commands -notmatch 'private void OnRetry\(\)[\s\S]*if \(_webViewService\.RetryNavigation\(\)\)[\s\S]*IsErrorVisible = false[\s\S]*StringResources\.RetryUnavailable[\s\S]*ShowRetryButton = true') {
    throw 'Manual retry failures must keep an actionable localized error visible when navigation cannot start.'
}

foreach ($resourceFile in @(
    'src/OpenClaw/Strings/en-us/Resources.resw',
    'src/OpenClaw/Strings/zh-cn/Resources.resw'
)) {
    $resources = Get-Content -LiteralPath (Join-Path $repoRoot $resourceFile) -Raw
    if ($resources -notmatch 'name="CircuitBreakerRecreationSuppressed"') {
        throw "Missing localized circuit breaker resource: $resourceFile"
    }

    if ($resources -notmatch 'name="RetryUnavailable"') {
        throw "Missing localized retry-unavailable resource: $resourceFile"
    }

    if ($resources -notmatch 'name="SettingsControlUiUrlPlaceholder"') {
        throw "Missing localized Control UI URL placeholder resource: $resourceFile"
    }

    foreach ($trayResource in @('TrayMenuOpen', 'TrayMenuCompactMode', 'TrayMenuExit')) {
        if ($resources -notmatch "name=`"$trayResource`"") {
            throw "Missing localized tray menu resource '$trayResource': $resourceFile"
        }
    }

    foreach ($removedResource in @(
        'AppDisplayName',
        'OpenInBrowser',
        'QuickCommands',
        'QuickStop',
        'QuickStatus',
        'QuickNew',
        'QuickQueue'
    )) {
        if ($resources -match "name=`"$removedResource`"") {
            throw "Removed stale localized resource '$removedResource' must not be reintroduced without a live typed usage: $resourceFile"
        }
    }
}

$mainWindowTray = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.Tray.cs') -Raw
if ($mainWindowTray -match 'StringResources\.Get\("TrayMenu(Open|CompactMode|Exit)"\)' -or
    $mainWindowTray -match '"Open OpenClaw"|"Compact Mode"|"Exit"') {
    throw 'Tray menu labels must use typed StringResources properties instead of raw resource lookup or fallback literals.'
}

$hostedBridge = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/HostedUiBridge.cs') -Raw
if ($hostedBridge -match 'App\.Logger') {
    throw 'HostedUiBridge must use injected IAppLogger, not App.Logger.'
}

$diagnosticService = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/DiagnosticService.cs') -Raw
if ($diagnosticService -match 'App\.Logger|App\.Configuration|App\.MainWindow') {
    throw 'DiagnosticService must receive app runtime dependencies from its caller instead of reading App globals.'
}

if ($hostedBridge -match '_bridgeScriptResource|BridgeScriptResource') {
    throw 'HostedUiBridge must compose localized bridge scripts at initialization time, not cache localized strings statically.'
}

if ($hostedBridge -notmatch 'CommandTimeout' -or
    $hostedBridge -notmatch 'CancellationTokenSource\.CreateLinkedTokenSource\(\s*timeout\.Token,\s*cancellationToken\)' -or
    $hostedBridge -notmatch '\.AsTask\(commandCancellation\.Token\)' -or
    $hostedBridge -notmatch 'CaptureAcceptedPageVersion' -or
    $hostedBridge -notmatch 'IsStillCurrentCommandTarget\(CoreWebView2 coreWebView, int pageVersion\)' -or
    $hostedBridge -notmatch 'IsCurrentAcceptedPageVersion\(pageVersion\)') {
    throw 'HostedUiBridge command dispatch must have a bounded timeout and reject stale WebView/page results after awaits.'
}

$bridgeScript = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/HostedUiBridge.Script.js') -Raw
if ($bridgeScript -notmatch 'const postSessionReady' -or
    $bridgeScript -notmatch "snapshot\.phase !== 'connected'" -or
    $bridgeScript -notmatch 'sessionReadyModelEmitted' -or
    $bridgeScript -notmatch 'reportSessionReady:\s*\(\) => \{[\s\S]*return postSessionReady\(\)') {
    throw 'Hosted bridge must allow native-triggered session-ready replay after page-token ownership is accepted.'
}

$commandDispatchScript = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/HostedUiBridge.CommandDispatch.js') -Raw
if ($commandDispatchScript -notmatch 'if \(!handled\)[\s\S]*dispatchBridgeEvent\(command, payload\)' -or
    $commandDispatchScript -notmatch 'return handled;' -or
    $commandDispatchScript -notmatch 'default:[\s\S]*dispatchBridgeEvent\(command, payload\);[\s\S]*return false;' -or
    $commandDispatchScript -match 'return handled \|\| dispatchBridgeEvent') {
    throw 'Hosted bridge command fallback must not report CustomEvent dispatch as a handled soft-resync command.'
}

$activityStateScript = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/HostedUiBridge.ActivityState.js') -Raw
$statusInspectionScript = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/HostedUiBridge.StatusInspection.js') -Raw
if ($activityStateScript -notmatch 'isChatBusy' -or
    $activityStateScript -notmatch 'isBusyStaleCandidate === false' -or
    $statusInspectionScript -notmatch 'isBusyStaleCandidate') {
    throw 'Hosted bridge stale-busy recovery must be limited to chat/output activity, not non-chat settings/cron busy states.'
}

$liveShellSettings = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Models/LiveShellSettings.cs') -Raw
$liveShellSettingsChange = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Models/LiveShellSettingsChange.cs') -Raw
$liveShellSettingsApplier = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/LiveShellSettingsApplier.cs') -Raw
$mainWindowXamlCs = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.xaml.cs') -Raw
if ($liveShellSettings -notmatch 'bool AllowMultipleInstances' -or
    $liveShellSettingsChange -notmatch 'DidChangeAllowMultipleInstances' -or
    $liveShellSettingsApplier -notmatch 'Action<bool> _applySingleInstancePreference' -or
    $liveShellSettingsApplier -notmatch 'DidChangeAllowMultipleInstances' -or
    $mainWindowXamlCs -notmatch 'ApplySingleInstancePreference\(allowMultipleInstances\)') {
    throw 'Live shell settings must apply AllowMultipleInstances to the running single-instance coordinator.'
}

$settingsXaml = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Views/SettingsDialog.xaml') -Raw
if ($settingsXaml -match '>Export Diagnostic Bundle<') {
    throw 'Settings dialog user-facing text must come from StringResources.'
}

if ($settingsXaml -match 'PlaceholderText="https://your-gateway\.example\.com"') {
    throw 'Settings dialog Control UI URL placeholder must come from StringResources.'
}

if ($settingsXaml -notmatch 'PlaceholderText="\{x:Bind helpers:StringResources\.SettingsControlUiUrlPlaceholder\}"') {
    throw 'Settings dialog Control UI URL placeholder must bind to the localized StringResources value.'
}

$commandHelpers = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Helpers/SimpleCommand.cs') -Raw
if ($commandHelpers -notmatch 'private int _isExecuting' -or
    $commandHelpers -notmatch 'Volatile\.Read\(ref _isExecuting\) == 0' -or
    $commandHelpers -notmatch 'Interlocked\.CompareExchange\(ref _isExecuting, 1, 0\)' -or
    $commandHelpers -notmatch '_action\(\) \?\? Task\.CompletedTask' -or
    $commandHelpers -notmatch 'ResetExecuting\(\)' -or
    $commandHelpers -notmatch 'CanExecuteChanged\?\.Invoke\(this, EventArgs\.Empty\)') {
    throw 'AsyncCommand must guard against repeated execution while an async command is already running.'
}

Write-Host 'PASS: repository structure guardrails'
