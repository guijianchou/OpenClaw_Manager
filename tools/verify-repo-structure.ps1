Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

$testsProjectPath = Join-Path $repoRoot 'tests/OpenClaw.Core.Tests/OpenClaw.Core.Tests.csproj'
if (-not (Test-Path -LiteralPath $testsProjectPath)) {
    throw 'tests/OpenClaw.Core.Tests is missing. The Core regression test project must stay in the repository.'
}

$solution = Get-Content -LiteralPath (Join-Path $repoRoot 'OpenClaw.sln') -Raw
if ($solution -notmatch [regex]::Escape('tests\OpenClaw.Core.Tests\OpenClaw.Core.Tests.csproj')) {
    throw 'OpenClaw.sln must reference tests/OpenClaw.Core.Tests so the regression suite builds with the solution.'
}

$coreProjectId = '{BC4C7184-C8DD-4748-AC82-D26123568BD1}'
$coreProject = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/OpenClaw.Core.csproj') -Raw
if ($coreProject -match '<Platforms>') {
        throw 'OpenClaw.Core.csproj must stay platform-independent. Do not declare x64/x86/ARM64 platforms on the pure SDK class library; map solution platforms to Debug/Release|Any CPU instead.'
}

$coreSolutionMappingPattern = [regex]::Escape($coreProjectId) + '\.(Debug|Release)\|(x64|x86|ARM64)\.(ActiveCfg|Build\.0) = (Debug|Release)\|([^`\r\n]+)'
foreach ($coreSolutionMapping in [regex]::Matches($solution, $coreSolutionMappingPattern)) {
    if ($coreSolutionMapping.Groups[5].Value -ne 'Any CPU') {
        throw "OpenClaw.Core solution platform mappings must target the pure class-library Any CPU configuration; invalid mapping: $($coreSolutionMapping.Value)"
    }
}

$allCoreSolutionMappingPattern = [regex]::Escape($coreProjectId) + '\.(?<configuration>[^|`\r\n]+)\|(?<solutionPlatform>[^.=`\r\n]+)\.(?<mapping>ActiveCfg|Build\.0|Deploy\.0) = (?<projectConfiguration>[^|`\r\n]+)\|(?<projectPlatform>[^`\r\n]+)'
foreach ($coreSolutionMapping in [regex]::Matches($solution, $allCoreSolutionMappingPattern)) {
    if ($coreSolutionMapping.Groups['configuration'].Value -notin @('Debug', 'Release') -or
        $coreSolutionMapping.Groups['projectConfiguration'].Value -ne $coreSolutionMapping.Groups['configuration'].Value -or
        $coreSolutionMapping.Groups['solutionPlatform'].Value -notin @('x64', 'x86', 'ARM64') -or
        $coreSolutionMapping.Groups['projectPlatform'].Value -ne 'Any CPU') {
        throw "OpenClaw.Core solution mappings must map Debug/Release x64/x86/ARM64 solution platforms to the matching Debug/Release|Any CPU project configuration: $($coreSolutionMapping.Value)"
    }
}

foreach ($configuration in @('Debug', 'Release')) {
    foreach ($platform in @('x64', 'x86', 'ARM64')) {
        foreach ($mapping in @('ActiveCfg', 'Build.0')) {
            $expectedCoreMapping = "$coreProjectId.$configuration|$platform.$mapping = $configuration|Any CPU"
            if ($solution -notmatch [regex]::Escape($expectedCoreMapping)) {
                throw "OpenClaw.Core solution platform mapping is missing or invalid: $expectedCoreMapping"
            }
        }
    }
}

$coreFiles = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core') -Recurse -File -Include *.cs
$forbiddenCorePattern = 'using Microsoft\.UI|using Microsoft\.Web\.WebView2|using Windows\.Graphics|using Windows\.UI|using WinRT|Microsoft\.Web\.WebView2|Type\.GetType\("Microsoft\.Web\.WebView2|App\.Configuration|App\.Logger|App\.MainWindow'
foreach ($file in $coreFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    if ($content -match $forbiddenCorePattern) {
        throw "Core boundary violation: $($file.FullName)"
    }
}

$approvedSynchronousWaits = @{
    'src/OpenClaw.Core/Services/ConfigurationService.cs' = @(
        'task.Wait(TimeSpan.FromSeconds(2))'
    )
    'src/OpenClaw.Core/Services/LoggingService.cs' = @(
        '_writerTask.Wait(TimeSpan.FromSeconds(2))'
    )
    'src/OpenClaw.Core/Services/SingleInstanceCoordinator.cs' = @(
        'StopAsync().GetAwaiter().GetResult()'
    )
    'src/OpenClaw/App.xaml.cs' = @(
        '_singleInstancePreferenceGate.Wait()',
        'coordinator.StopAsync().GetAwaiter().GetResult()'
    )
}

$repoRootPath = ([string]$repoRoot).TrimEnd('\', '/')
$synchronousWaitMatches = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Recurse -File -Filter *.cs |
    Select-String -Pattern '\.Wait\(|GetAwaiter\(\)\.GetResult\(\)|Task\.Result|Thread\.Sleep\('
foreach ($match in $synchronousWaitMatches) {
    $relativePath = $match.Path.Substring($repoRootPath.Length + 1).Replace('\', '/')
    if (-not $approvedSynchronousWaits.ContainsKey($relativePath)) {
        throw "Unapproved synchronous wait in runtime code: ${relativePath}:$($match.LineNumber)"
    }

    $approved = $false
    foreach ($approvedSnippet in $approvedSynchronousWaits[$relativePath]) {
        if ($match.Line.IndexOf($approvedSnippet, [StringComparison]::Ordinal) -ge 0) {
            $approved = $true
            break
        }
    }

    if (-not $approved) {
        throw "Unapproved synchronous wait in runtime code: ${relativePath}:$($match.LineNumber)"
    }
}

$project = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/OpenClaw.csproj') -Raw
$linkedCompileItems = [regex]::Matches($project, '<Compile\s+(?:Include|Update)="(?<path>[^"]+)"')
foreach ($linkedCompileItem in $linkedCompileItems) {
    $compilePath = $linkedCompileItem.Groups['path'].Value
    if ($compilePath -match '(^|[\\/])\.\.[\\/]' -or
        $compilePath -match '(^|[\\/])OpenClaw\.Core([\\/]|$)') {
        throw "OpenClaw.csproj must not link Core source files or compile sources outside the WinUI project: $compilePath"
    }
}

$currentVersion = '5.2.0'
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

$hostedBridgeScriptBuilder = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/HostedUiBridge.Script.cs') -Raw
$hostedBridgeScriptTemplate = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/HostedUiBridge.Script.js') -Raw
$bridgeScriptResourcePairs = @(
    @{ Resource = 'HostedUiBridge.HostMessaging.js'; ResourceField = 'HostMessagingResourceName'; ScriptField = 'HostMessagingScript'; PlaceholderField = 'HostMessagingPlaceholder'; Placeholder = '__OPENCLAW_HOST_MESSAGING_SCRIPT__' },
    @{ Resource = 'HostedUiBridge.MutationFilter.js'; ResourceField = 'MutationFilterResourceName'; ScriptField = 'MutationFilterScript'; PlaceholderField = 'MutationFilterPlaceholder'; Placeholder = '__OPENCLAW_MUTATION_FILTER_SCRIPT__' },
    @{ Resource = 'HostedUiBridge.ModelResolver.js'; ResourceField = 'ModelResolverResourceName'; ScriptField = 'ModelResolverScript'; PlaceholderField = 'ModelResolverPlaceholder'; Placeholder = '__OPENCLAW_MODEL_RESOLVER_SCRIPT__' },
    @{ Resource = 'HostedUiBridge.DomUtilities.js'; ResourceField = 'DomUtilitiesResourceName'; ScriptField = 'DomUtilitiesScript'; PlaceholderField = 'DomUtilitiesPlaceholder'; Placeholder = '__OPENCLAW_DOM_UTILITIES_SCRIPT__' },
    @{ Resource = 'HostedUiBridge.ModelDomFallback.js'; ResourceField = 'ModelDomFallbackResourceName'; ScriptField = 'ModelDomFallbackScript'; PlaceholderField = 'ModelDomFallbackPlaceholder'; Placeholder = '__OPENCLAW_MODEL_DOM_FALLBACK_SCRIPT__' },
    @{ Resource = 'HostedUiBridge.ActivityState.js'; ResourceField = 'ActivityStateResourceName'; ScriptField = 'ActivityStateScript'; PlaceholderField = 'ActivityStatePlaceholder'; Placeholder = '__OPENCLAW_ACTIVITY_STATE_SCRIPT__' },
    @{ Resource = 'HostedUiBridge.PhaseClassifier.js'; ResourceField = 'PhaseClassifierResourceName'; ScriptField = 'PhaseClassifierScript'; PlaceholderField = 'PhaseClassifierPlaceholder'; Placeholder = '__OPENCLAW_PHASE_CLASSIFIER_SCRIPT__' },
    @{ Resource = 'HostedUiBridge.StatusInspection.js'; ResourceField = 'StatusInspectionResourceName'; ScriptField = 'StatusInspectionScript'; PlaceholderField = 'StatusInspectionPlaceholder'; Placeholder = '__OPENCLAW_STATUS_INSPECTION_SCRIPT__' },
    @{ Resource = 'HostedUiBridge.CommandDispatch.js'; ResourceField = 'CommandDispatchResourceName'; ScriptField = 'CommandDispatchScript'; PlaceholderField = 'CommandDispatchPlaceholder'; Placeholder = '__OPENCLAW_COMMAND_DISPATCH_SCRIPT__' }
)

foreach ($pair in $bridgeScriptResourcePairs) {
    if ($hostedBridgeScriptTemplate -notmatch [regex]::Escape($pair.Placeholder) -or
        $hostedBridgeScriptBuilder -notmatch [regex]::Escape($pair.ResourceField) -or
        $hostedBridgeScriptBuilder -notmatch [regex]::Escape($pair.ScriptField) -or
        $hostedBridgeScriptBuilder -notmatch [regex]::Escape($pair.PlaceholderField) -or
        $hostedBridgeScriptBuilder -notmatch "\.Replace\($($pair.PlaceholderField),\s*$($pair.ScriptField)\.Value,\s*StringComparison\.Ordinal\)") {
        throw "Hosted bridge script builder must compose $($pair.Resource) through its matching placeholder."
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

$webViewNavigation = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.Navigation.cs') -Raw
$webViewHostMessages = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.HostMessages.cs') -Raw
$webViewNavigationState = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.NavigationState.cs') -Raw
$webViewNavigationWatchdogs = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.NavigationWatchdogs.cs') -Raw
$webViewNavigationCommands = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.NavigationCommands.cs') -Raw
$webViewPageToken = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.PageToken.cs') -Raw
$webViewNavigationRecovery = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.NavigationRecovery.cs') -Raw
$webViewLifecycle = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.Lifecycle.cs') -Raw
$webViewSession = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.Session.cs') -Raw
if ($webViewService -match 'private\s+(?:async\s+)?(?:void|Task|Task<[^>]+>|bool|int)\s+(?:OnNavigationStarting|OnNavigationCompleted|ObserveNavigationStartTimeout|CaptureCurrentPageTokenAsync)|private enum AutoRetryOutcome' -or
    $webViewNavigation -notmatch 'OnNavigationStarting' -or
    $webViewNavigation -notmatch 'OnNavigationCompleted' -or
    $webViewNavigationWatchdogs -notmatch 'ObserveNavigationStartTimeout' -or
    $webViewPageToken -notmatch 'CaptureCurrentPageTokenAsync' -or
    $webViewNavigationRecovery -notmatch 'AutoRetryOutcome') {
    throw 'WebView navigation events, watchdogs, page-token capture, and retry helpers must live in focused WebViewService navigation partials.'
}

if ($webViewNavigation -match 'private\s+(?:async\s+)?(?:void|Task|Task<[^>]+>|bool|int)\s+(?:ObserveNavigation(?:Start|Completion)Timeout|HandleNavigation(?:Start|Completion)Timeout|CancelNavigation(?:Start|Completion)Watchdog|TryNavigateCoreWebView|TryReloadCoreWebView|CaptureCurrentPageTokenAsync|RetryPageTokenCaptureAsync|RequestSessionReadyReportAsync|TryAutoRetryAfterConnectionErrorAsync|OnProcessFailed|OnWebMessageReceived|PrepareNavigationStart|CancelActiveNavigation|ReplaceNavigationCancellation|CancelNavigationCancellation|IsRecoveredNavigationCompletionForPendingTarget|AreNavigationUrlsEquivalent)\b|private enum AutoRetryOutcome') {
    throw 'WebViewService.Navigation.cs must stay focused on WebView2 event entry and completion flow; host-message, navigation-state, watchdog, command, page-token, and recovery helpers belong in dedicated partials.'
}

if ($webViewService -match 'public\s+async\s+Task\s+InitializeAsync|private\s+void\s+DetachCurrentWebView\(|public\s+void\s+Dispose\(|private\s+static\s+CoreWebView2\?\s+TryGetCoreWebView2|private\s+CoreWebView2\?\s+GetCoreWebView\(|private\s+bool\s+IsCurrent(?:Initialization|Host|Navigation)\(' -or
    $webViewService -match 'public\s+async\s+Task\s+ClearBrowsingDataAsync|public\s+void\s+OpenDevTools\(|public\s+async\s+Task\s+ClearEnvironmentSessionAsync|public\s+string\?\s+GetCurrentUrl\(|public\s+bool\s+IsUsingEnvironmentProfile\(' -or
    $webViewLifecycle -notmatch 'public async Task InitializeAsync' -or
    $webViewLifecycle -notmatch 'private void DetachCurrentWebView\(' -or
    $webViewLifecycle -notmatch 'public void Dispose\(' -or
    $webViewLifecycle -notmatch 'private CoreWebView2\? GetCoreWebView\(' -or
    $webViewSession -notmatch 'public async Task ClearBrowsingDataAsync' -or
    $webViewSession -notmatch 'public void OpenDevTools\(' -or
    $webViewSession -notmatch 'public async Task ClearEnvironmentSessionAsync' -or
    $webViewSession -notmatch 'public string\? GetCurrentUrl\(' -or
    $webViewSession -notmatch 'public bool IsUsingEnvironmentProfile\(') {
    throw 'WebViewService.cs must keep fields, events, construction, and navigation commands; lifecycle and session/profile operations belong in dedicated partials.'
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

if ($webViewPageToken -notmatch 'TrySetUnavailableSnapshot\(\s*"Hosted bridge page token was not accepted after navigation\.",\s*generation\)' -or
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

if ($latencyService -match 'control-ui-config\.json' -or
    $latencyService -notmatch '__openclaw__/a2ui/' -or
    $latencyService -notmatch 'GatewayHttpStatusClassifier\.Classify' -or
    $latencyService -match 'ControlUiLatencySnapshot\.Success\([\s\S]*HTTP \{\(int\)response\.StatusCode\}') {
    throw 'Control UI latency probes must target the documented Gateway A2UI HTTP path and classify HTTP status before publishing success.'
}

$singleInstanceCoordinator = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Services/SingleInstanceCoordinator.cs') -Raw
$app = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/App.xaml.cs') -Raw

if ($app -match '(?<!Microsoft\.)Windows\.Globalization\.ApplicationLanguages\.PrimaryLanguageOverride' -or
    $app -notmatch 'Microsoft\.Windows\.Globalization\.ApplicationLanguages\.PrimaryLanguageOverride') {
    throw 'WinUI language preference must use Microsoft.Windows.Globalization.ApplicationLanguages from Windows App SDK, not Windows.Globalization.ApplicationLanguages.'
}

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
if ($gatewayHeartbeatTransport -notmatch 'GatewayHttpStatusClassifier\.Classify' -or
    $gatewayHeartbeatTransport -match 'statusCode switch') {
    throw 'Gateway heartbeat transport must classify Cloudflare/proxy 5xx, missing Control UI paths, rejected probes, and unexpected responses as failures.'
}

$hostedSessionHeartbeatPolicy = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/HostedSessionHeartbeatPolicy.cs') -Raw
if ($hostedSessionHeartbeatPolicy -notmatch 'ControlUiPhase\.Unavailable\s*=>\s*HeartbeatProbeResult\.Failure') {
    throw 'Hosted-session heartbeat must treat unavailable bridge/status inspection as a failure instead of falling through to healthy HTTP transport.'
}

$sessionProbeModels = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Services/SessionProbeModels.cs') -Raw
$isTerminalExpression = [regex]::Match($sessionProbeModels, 'public bool IsTerminal =>(?<body>[\s\S]*?);')
if (-not $isTerminalExpression.Success -or
    $isTerminalExpression.Groups['body'].Value -notmatch 'ControlUiPhase\.Unavailable') {
    throw 'Control UI Unavailable must be terminal for post-navigation status probes so recovery can take ownership without extra script churn.'
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
$statusInspectorInspection = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewStatusInspector.Inspection.cs') -Raw
$statusInspectorParsing = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewStatusInspector.Parsing.cs') -Raw
$statusInspectorProbe = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewStatusInspector.Probe.cs') -Raw
$statusInspectorScriptExecution = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewStatusInspector.ScriptExecution.cs') -Raw
if ($statusInspector -match 'const string script = """|ExecuteScriptAsync\(@"|ExecuteScriptAsync\(\$@"' -or
    $statusInspectorInspection -match 'const string script = """|ExecuteScriptAsync\(@"|ExecuteScriptAsync\(\$@"' -or
    $statusInspectorScriptExecution -match 'const string script = """|ExecuteScriptAsync\(@"|ExecuteScriptAsync\(\$@"') {
    throw 'WebViewStatusInspector must load browser scripts from embedded JS assets.'
}

if ($statusInspector -match 'private static ControlUiProbeSnapshot ParseControlUiSnapshot|private static string GetString|private static ControlUiPhase ParsePhase|private static async Task<string> ExecuteStatusScriptWithTimeoutAsync|private\s+(?:async\s+)?Task<ControlUiProbeSnapshot>\s+ExecuteControlUiInspectionAsync|private\s+(?:async\s+)?Task\s+CompleteControlUiInspectionAsync|private\s+(?:async\s+)?Task\s+ProbeControlUiStateAfterNavigationAsync') {
    throw 'WebViewStatusInspector.cs must keep state, construction, public entry points, and snapshot publication only; inspection, probe, parsing, and script execution belong in focused partials.'
}

if ($statusInspector -notmatch 'partial class WebViewStatusInspector' -or
    $statusInspectorInspection -notmatch 'partial class WebViewStatusInspector' -or
    $statusInspectorParsing -notmatch 'partial class WebViewStatusInspector' -or
    $statusInspectorProbe -notmatch 'partial class WebViewStatusInspector' -or
    $statusInspectorScriptExecution -notmatch 'partial class WebViewStatusInspector') {
    throw 'WebViewStatusInspector must use focused partials for inspection, parsing, probe, and script execution.'
}

if ($statusInspector -notmatch 'InspectionTimeout' -or
    $statusInspectorScriptExecution -notmatch 'CancellationTokenSource\(InspectionTimeout\)' -or
    $statusInspectorScriptExecution -notmatch '\.AsTask\(timeout\.Token\)') {
    throw 'WebViewStatusInspector script execution must have a bounded timeout.'
}

if ($statusInspectorParsing -notmatch 'ParseControlUiSnapshot\(string json\)' -or
    $statusInspectorParsing -notmatch 'ControlUiStatusMessageKind' -or
    $statusInspectorParsing -notmatch 'ParsePhase\(GetString\(root, "phase"\)\)' -or
    $statusInspectorParsing -notmatch 'ModelSource = currentModelSource') {
    throw 'WebViewStatusInspector parsing must stay in the focused parsing partial and preserve full Control UI snapshot payload support.'
}

if ($statusInspector -notmatch '_statusProbeTask' -or
    $statusInspector -notmatch '_probeGate' -or
    $statusInspectorProbe -notmatch 'ProbeControlUiStateAfterNavigationAsync\(CancellationTokenSource cancellation' -or
    $statusInspectorProbe -notmatch 'finally[\s\S]*cancellation\.Dispose\(\)') {
    throw 'WebViewStatusInspector probe loop must own its task/cancellation lifetime.'
}

if ($statusInspector -notmatch 'UiTaskDispatcher _uiDispatcher' -or
    $statusInspectorProbe -notmatch '_uiDispatcher\.RunAsync\(') {
    throw 'WebViewStatusInspector status probe loop must marshal WebView2 inspection through the UI dispatcher.'
}

if ($statusInspectorProbe -notmatch 'PublishProbeExhaustedSnapshot\(generation\)' -or
    $statusInspectorProbe -notmatch 'private void PublishProbeExhaustedSnapshot\(int generation\)' -or
    $statusInspectorProbe -notmatch 'Control UI did not report a terminal session state after navigation probes were exhausted' -or
    $statusInspectorProbe -notmatch 'TrySetUnavailableSnapshot') {
    throw 'WebViewStatusInspector status probe loop must publish an owned Unavailable snapshot when post-navigation probes are exhausted.'
}

if ($statusInspector -notmatch 'WebViewMessageOwnership _messageOwnership' -or
    $statusInspectorInspection -notmatch 'CaptureAcceptedPageVersion' -or
    $statusInspector -notmatch '_latestControlUiSnapshotPageVersion' -or
    $statusInspector -notmatch '_inFlightInspectionPageVersion' -or
    $statusInspectorInspection -notmatch 'IsCurrentInspectionTarget\(int generation, int pageVersion\)' -or
    $statusInspectorInspection -notmatch 'TryPublishInspectionSnapshot\([\s\S]*ControlUiProbeSnapshot snapshot' -or
    $statusInspectorInspection -notmatch 'ControlUiProbeSnapshot\.Unavailable\("Control UI inspection timed out\."\)[\s\S]*TryPublishInspectionSnapshot' -or
    $statusInspectorInspection -notmatch 'HasActiveInFlightPublishWaiter\(int inspectionId\)' -or
    $statusInspector -notmatch 'TryApplyHostMessage\(string json, int pageVersion, out ControlUiProbeSnapshot snapshot\)' -or
    $statusInspectorInspection -notmatch 'ExecuteControlUiInspectionAsync\([\s\S]*int generation,[\s\S]*int pageVersion,[\s\S]*int inspectionId' -or
    $statusInspector -notmatch 'ApplyControlUiSnapshot\([\s\S]*int generation,[\s\S]*int pageVersion,[\s\S]*bool notifySnapshotUpdated') {
    throw 'WebViewStatusInspector direct script inspections must be scoped by generation, accepted page version, and active caller cancellation before publishing.'
}

if ($statusInspector -match 'CancelProbeLoop\(\)[\s\S]*?_statusProbeCts\.Dispose\(') {
    throw 'WebViewStatusInspector stop paths must cancel probe CTS and let the running probe dispose it.'
}

if ($statusInspector -notmatch 'CancelProbeLoop\(\)[\s\S]*?_statusProbeCts\?\.Cancel\(\)[\s\S]*?_statusProbeCts = null') {
    throw 'WebViewStatusInspector stop paths must cancel the active probe CTS before clearing ownership.'
}

if ($statusInspector -notmatch 'public void SetUnknownSnapshot\(\)[\s\S]*notifySnapshotUpdated:\s*true') {
    throw 'WebView detach must publish an Unknown snapshot so visible MODEL/AUTH/WORK projection cannot survive host replacement.'
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
    $webViewHostMessages -notmatch 'CreateWebMessageReceivedHandler\(int hostGeneration\)' -or
    $webViewHostMessages -notmatch 'OnWebMessageReceived[\s\S]*IsCurrentHost\(hostGeneration\)' -or
    $webViewHostMessages -notmatch 'TryCaptureCurrentVersion\(args, root, out var pageVersion\)' -or
    $webViewHostMessages -notmatch 'TryApplyHostMessage\(message, pageVersion, out var snapshot\)' -or
    $webViewHostMessages -notmatch 'IsCurrentAcceptedPageVersion\(pageVersion\)' -or
    $webViewPageToken -notmatch 'CaptureCurrentPageTokenAsync') {
    throw 'WebViewService must reject stale WebView messages by host generation and page ownership.'
}

if ($webViewNavigationState -notmatch 'PrepareNavigationStart[\s\S]*_messageOwnership\.BeginNavigation\(\)') {
    throw 'Programmatic WebView navigation must invalidate the accepted page token before CoreWebView2 starts navigating.'
}

$cancelActiveNavigationMethod = [regex]::Match($webViewNavigationState, 'private void CancelActiveNavigation\([\s\S]*?\n    \}')
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

$navigationStartingMethod = [regex]::Match($webViewNavigation, 'private void OnNavigationStarting\([\s\S]*?\n    \}')
$navigationCompletedMethod = [regex]::Match($webViewNavigation, 'private async Task HandleNavigationCompletedAsync\([\s\S]*?\n    \}')
if (-not $navigationStartingMethod.Success -or
    -not $navigationCompletedMethod.Success -or
    $navigationStartingMethod.Value -match 'ReferenceEquals\(sender, _coreWebView\)' -or
    $navigationCompletedMethod.Value -match 'ReferenceEquals\(sender, _coreWebView\)') {
    throw 'WebView2 navigation events must not be rejected by CoreWebView2 COM wrapper reference identity; rely on active host state, navigation id, and generation instead.'
}

if ($navigationCompletedMethod.Value -notmatch 'TryClaimNavigationCompleted\(sender, args, hostGeneration\)' -or
    $webViewNavigation -notmatch 'TryClaimNavigationCompleted[\s\S]*_currentNavigationId == NoCurrentNavigationId[\s\S]*IsRecoveredNavigationCompletionForPendingTarget\(sender, hostGeneration\)[\s\S]*navigation\.starting\.recovered_from_completion' -or
    $webViewNavigationState -notmatch 'private bool IsRecoveredNavigationCompletionForPendingTarget\(CoreWebView2 sender, int hostGeneration\)' -or
    $webViewNavigationState -notmatch 'TryGetActiveNavigationStartWatchdog\([\s\S]*out var navigationGeneration,[\s\S]*out var expectedUrl,[\s\S]*out var previousSource' -or
    $webViewNavigationState -notmatch 'AreNavigationUrlsEquivalent\(currentSource, expectedUrl\)' -or
    $webViewNavigationState -notmatch 'AreNavigationUrlsEquivalent\(expectedUrl, previousSource\)' -or
    $webViewNavigationState -notmatch 'private static bool AreNavigationUrlsEquivalent') {
    throw 'WebView2 NavigationCompleted must be able to claim a current navigation when NavigationStarting was not delivered.'
}

if ($webViewServiceMain -notmatch 'NavigationStartTimeout' -or
    $webViewServiceMain -notmatch 'NavigationStartRecoveryWindow = NavigationCompletionTimeout - NavigationStartTimeout' -or
    $webViewServiceMain -notmatch 'ObserveNavigationStartTimeout\(navigationGeneration, url, previousSource\)' -or
    $webViewNavigationWatchdogs -notmatch 'navigation\.start\.timeout' -or
    $webViewNavigationWatchdogs -notmatch 'Navigation did not start within' -or
    $webViewNavigationWatchdogs -notmatch 'NavigationStartTimedOut\?\.Invoke' -or
    $webViewNavigationWatchdogs -notmatch 'CancelNavigationStartWatchdog\(\)' -or
    $webViewNavigationWatchdogs -notmatch '_activeNavigationStartWatchdogGeneration' -or
    $webViewNavigationWatchdogs -notmatch '_activeNavigationStartWatchdogUrl' -or
    $webViewNavigationWatchdogs -notmatch '_activeNavigationStartWatchdogPreviousSource' -or
    $webViewNavigationWatchdogs -notmatch '_hasActiveNavigationStartWatchdogOwnership = true' -or
    $webViewNavigationWatchdogs -notmatch 'TryGetActiveNavigationStartWatchdog' -or
    $webViewNavigationWatchdogs -notmatch 'Task\.Delay\(NavigationStartRecoveryWindow, cancellation\.Token\)' -or
    $webViewNavigationWatchdogs -notmatch 'ClearExpiredNavigationStartWatchdogOwnership' -or
    $webViewNavigationWatchdogs -notmatch 'private async Task ObserveNavigationStartTimeoutAsync') {
    throw 'WebView navigation must have a bounded start watchdog so a missing WebView2 NavigationStarting/Completed callback cannot leave the shell stuck in Loading.'
}

$navigationStartTimeoutMethod = [regex]::Match($webViewNavigationWatchdogs, 'private void HandleNavigationStartTimeout\([\s\S]*?\n    \}')
if (-not $navigationStartTimeoutMethod.Success -or
    $navigationStartTimeoutMethod.Value -match 'ReferenceEquals\(coreWebView, _coreWebView\)' -or
    $navigationStartTimeoutMethod.Value -match 'NavigationErrorOccurred\?\.Invoke|SetState\(ConnectionState\.Error\)|CancelNavigationCancellation\(\)' -or
    $navigationStartTimeoutMethod.Value -notmatch 'SetState\(ConnectionState\.Reconnecting\)[\s\S]*NavigationStartTimedOut\?\.Invoke') {
    throw 'Navigation-start timeout is a recoverable WebView2 startup stall and must request WebView recreation without relying on CoreWebView2 wrapper reference identity.'
}

$tryGetNavigationStartWatchdogMethod = [regex]::Match($webViewNavigationWatchdogs, 'private bool TryGetActiveNavigationStartWatchdog\([\s\S]*?\n    \}')
if (-not $tryGetNavigationStartWatchdogMethod.Success -or
    $tryGetNavigationStartWatchdogMethod.Value -match 'return _navigationStartWatchdogCts is not null' -or
    $tryGetNavigationStartWatchdogMethod.Value -notmatch 'return _hasActiveNavigationStartWatchdogOwnership') {
    throw 'Navigation-start timeout must keep pending target ownership after its CTS fires so a late NavigationCompleted can still be recovered.'
}

$clearExpiredNavigationStartWatchdogMethod = [regex]::Match($webViewNavigationWatchdogs, 'private void ClearExpiredNavigationStartWatchdogOwnership\([\s\S]*?\n    \}')
if (-not $clearExpiredNavigationStartWatchdogMethod.Success -or
    $clearExpiredNavigationStartWatchdogMethod.Value -notmatch '_activeNavigationStartWatchdogGeneration != navigationGeneration' -or
    $clearExpiredNavigationStartWatchdogMethod.Value -notmatch 'ClearNavigationStartWatchdogOwnershipLocked\(\)' -or
    $clearExpiredNavigationStartWatchdogMethod.Value -notmatch 'navigation\.start\.recovery_window_expired') {
    throw 'Navigation-start timeout recovery ownership must expire after a bounded grace window when no late NavigationCompleted arrives.'
}

if ($webViewServiceMain -notmatch 'NavigationCompletionTimeout' -or
    $webViewNavigationWatchdogs -notmatch 'ObserveNavigationCompletionTimeout' -or
    $webViewNavigationWatchdogs -notmatch 'NavigationCompletionTimedOut\?\.Invoke' -or
    $webViewNavigationWatchdogs -notmatch 'CancelNavigationCompletionWatchdog\(\)' -or
    $webViewNavigationWatchdogs -notmatch 'navigation\.completion\.timeout' -or
    $webViewNavigationWatchdogs -notmatch 'Navigation did not complete within' -or
    $webViewNavigationWatchdogs -notmatch 'private async Task ObserveNavigationCompletionTimeoutAsync') {
    throw 'WebView navigation must have a bounded completion watchdog so a started navigation cannot leave the shell stuck in Loading forever.'
}

$navigationStartingMethod = [regex]::Match($webViewNavigation, 'private void OnNavigationStarting\([\s\S]*?\n    \}')
$navigationCompletionTimeoutMethod = [regex]::Match($webViewNavigationWatchdogs, 'private void HandleNavigationCompletionTimeout\([\s\S]*?\n    \}')
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
    $webViewNavigation -notmatch 'CreateNavigationStartingHandler\(int hostGeneration\)' -or
    $webViewNavigation -notmatch 'CreateNavigationCompletedHandler\(int hostGeneration\)' -or
    $webViewNavigation -notmatch 'CreateProcessFailedHandler\(int hostGeneration\)' -or
    $webViewLifecycle -notmatch 'CreateNavigationStartingHandler\(hostGeneration\)' -or
    $webViewLifecycle -notmatch 'CreateNavigationCompletedHandler\(hostGeneration\)' -or
    $webViewLifecycle -notmatch 'CreateProcessFailedHandler\(hostGeneration\)' -or
    $webViewLifecycle -notmatch 'IsCurrentHost\(hostGeneration\)' -or
    $webViewLifecycle -notmatch 'CancelNavigationCompletionWatchdog\(\)[\s\S]*_generations\.Next\(\)[\s\S]*_messageOwnership\.BeginNavigation\(\)' -or
    $webViewNavigationState -notmatch 'CancelNavigationCompletionWatchdog\(\)[\s\S]*_generations\.Next\(\)[\s\S]*_messageOwnership\.BeginNavigation\(\)' -or
    $webViewNavigationRecovery -notmatch 'CancelActiveNavigation\(\)') {
    throw 'WebViewService must use an explicit host generation for WebView2 events/watchdogs and cancel both navigation watchdogs when detaching a host.'
}

if ($webViewNavigationWatchdogs -match 'ObserveNavigation(Start|Completion)TimeoutAsync\(\s*CoreWebView2 coreWebView') {
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
    $mainViewModelLifecycle -notmatch 'NavigationTimeoutRecovered \+= OnNavigationTimeoutRecovered' -or
    $mainViewModelLifecycle -notmatch 'NavigationTimeoutRecovered -= OnNavigationTimeoutRecovered' -or
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

$mainWindowInitialization = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.Initialization.cs') -Raw
$mainWindowCommands = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.Commands.cs') -Raw
$mainWindowLifecycle = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.Lifecycle.cs') -Raw
$mainWindowShared = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.Shared.cs') -Raw
$mainViewModelShared = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/MainViewModel.Shared.cs') -Raw
$webViewRecreationService = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewRecreationService.cs') -Raw
if ($webViewServiceMain -notmatch 'event Action\? NavigationTimeoutRecovered' -or
    $webViewNavigation -notmatch 'NavigationTimeoutRecovered\?\.Invoke\(\)[\s\S]*NavigationCompleted\?\.Invoke' -or
    $mainViewModelStatus -notmatch 'private void OnNavigationTimeoutRecovered\(\)[\s\S]*NavigationTimeoutRecoveryNoLongerNeeded\?\.Invoke\(\)' -or
    $mainViewModelShared -notmatch 'event Action\? NavigationTimeoutRecoveryNoLongerNeeded' -or
    $mainWindowInitialization -notmatch 'NavigationTimeoutRecoveryNoLongerNeeded \+= OnNavigationTimeoutRecoveryNoLongerNeeded' -or
    $mainWindowLifecycle -notmatch 'NavigationTimeoutRecoveryNoLongerNeeded -= OnNavigationTimeoutRecoveryNoLongerNeeded' -or
    $mainWindowCommands -notmatch 'OnNavigationTimeoutRecoveryNoLongerNeeded\(\)[\s\S]*TryCancelNavigationTimeoutRecovery\([\s\S]*_webViewRecreationTimer\.Stop\(\)' -or
    $mainWindowCommands -notmatch 'webview\.recreation\.cancelled_after_navigation_recovered' -or
    $webViewRecreationService -notmatch 'TryCancelNavigationTimeoutRecovery\(out WebViewRecreationCancelledRecoveryResult result\)' -or
    $webViewRecreationService -notmatch 'IsNavigationTimeoutRecoveryReason\(string\? reason\)' -or
    $webViewRecreationService -notmatch 'ChooseHigherPriorityReason\(string\? currentReason, string newReason\)' -or
    $webViewRecreationService -notmatch 'var pendingReason = ChooseHigherPriorityReason\(_pendingReason, normalizedReason\)' -or
    $webViewRecreationService -notmatch '_pendingReason = pendingReason' -or
    $webViewRecreationService -notmatch 'public string Defer\(string reason\)' -or
    $webViewRecreationService -notmatch 'public bool TryConsumeDeferred\(out string\? reason\)' -or
    $webViewRecreationService -notmatch 'public bool HasPendingDeferredOrActiveWork' -or
    $webViewRecreationService -notmatch '_pendingReason is not null[\s\S]*_deferredReason is not null[\s\S]*IsRecreating' -or
    $webViewRecreationService -notmatch 'public void ClearPending\(\)[\s\S]*_pendingReason = null;[\s\S]*_deferredReason = null;[\s\S]*_activeReasonCancelled = IsRecreating;' -or
    $webViewRecreationService -notmatch 'navigation_start_timeout' -or
    $webViewRecreationService -notmatch 'navigation_completion_timeout' -or
    $webViewRecreationService -notmatch 'CancelledActive' -or
    $webViewRecreationService -notmatch 'IsRecreating && IsNavigationTimeoutRecoveryReason\(LastReason\)' -or
    $webViewRecreationService -notmatch 'ShouldSkipCurrentRecreation\(\)' -or
    $mainWindowWebView -notmatch 'ShouldSkipCurrentRecreation\(\)[\s\S]*webview\.recreation\.skipped_after_navigation_recovered' -or
    $mainWindowCommands -notmatch 'activeReason = cancelled\.ActiveReason') {
    throw 'Late navigation completion after a timeout must cancel pending/deferred timeout-driven WebView recreation without cancelling or overwriting settings, initial-load, or topology-change recreation.'
}

if ($webViewNavigation -notmatch 'TryEnsureNavigationCancellationForRecoveredCompletion\(\s*args\.NavigationId,\s*completionGeneration,\s*hostGeneration\)' -or
    $webViewNavigation -notmatch 'private NavigationCancellationScope\? TryEnsureNavigationCancellationForRecoveredCompletion' -or
    $webViewNavigation -notmatch 'navigation\.completion\.recovered_after_timeout') {
    throw 'Late successful NavigationCompleted after a completion timeout must recreate navigation cancellation ownership and run the normal page-token/probe recovery path.'
}

if ($mainWindowWebView -notmatch 'if \(!await WaitForWebViewHostLayoutAsync\(nextWebView, cancellationToken\)\)' -or
    $mainWindowWebView -notmatch 'private async Task<bool> WaitForWebViewHostLayoutAsync\(WebView2 webView, CancellationToken cancellationToken\)' -or
    $mainWindowWebView -notmatch 'webview\.host\.layout_ready' -or
    $mainWindowWebView -notmatch 'webview\.host\.layout_wait_timeout' -or
    $mainWindowWebView -notmatch 'webview\.recreation\.deferred_until_visible_layout' -or
    $mainWindowWebView -notmatch 'webview\.recreation\.retry_after_layout_timeout' -or
    $mainWindowWebView -notmatch 'ScheduleDeferredWebViewRecreationRetry' -or
    $mainWindowWebView -notmatch '_webViewRecreationService\.Defer\(reason\)' -or
    $mainWindowWebView -notmatch 'ScheduleDeferredWebViewRecreationRetry\(_webViewRecreationService\.LastReason \?\? begin\.Reason\)' -or
    $mainWindowWebView -notmatch 'ScheduleDeferredWebViewRecreationRetry\(string reason\)[\s\S]*_webViewRecreationService\.Schedule\(reason\)' -or
    $mainWindowWebView -notmatch 'TryConsumeDeferred\(out var reason\)' -or
    $mainWindowWebView -notmatch 'ScheduleWebViewRecreation\(reason\)' -or
    $mainWindowWebView -notmatch 'webView\.Loaded \+=' -or
    $mainWindowWebView -notmatch 'webView\.SizeChanged \+=' -or
    $mainWindowWebView -notmatch 'webView\.SizeChanged -=' -or
    $mainWindowWebView -notmatch 'webView\.ActualSize\.X > 0' -or
    $mainWindowWebView -notmatch 'webView\.ActualSize\.Y > 0' -or
    $mainWindowWebView -notmatch 'webView\.Visibility == Visibility\.Visible' -or
    $mainWindowWebView -notmatch '!_isCompactMode' -or
    $mainWindowWebView -notmatch '!_isWindowHidden' -or
    $mainWindowWebView -notmatch '!WindowFrameHelper\.IsWindowMinimized\(this\)' -or
    $mainWindowWebView -notmatch 'isCompactMode = _isCompactMode' -or
    $mainWindowWebView -notmatch 'isWindowHidden = _isWindowHidden' -or
    $mainWindowWebView -notmatch 'isMinimized = WindowFrameHelper\.IsWindowMinimized\(this\)' -or
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
    $mainWindowWebView -notmatch 'WaitForWebViewHostLayoutAsync[\s\S]*_webViewRecreationService\.RecordAttempt\(' -or
    $mainWindowWebView -notmatch 'CanAttemptInLoop\(\)' -or
    $mainWindowWebView -notmatch 'webview\.recreation\.circuit_breaker_tripped_in_loop' -or
    $mainWindowWebView -notmatch 'ShowCircuitBreakerError\(\)' -or
    $circuitBreaker -notmatch 'maxAttempts = 5' -or
    $circuitBreaker -notmatch 'windowSeconds = 60') {
    throw 'WebView recreation must bound repeated navigation_start_timeout recovery attempts and surface circuit-breaker suppression.'
}

if ($mainWindowWebView -notmatch 'catch \(Exception ex\)[\s\S]*Failed to recreate WebView2 host[\s\S]*ViewModel\.ShowWebViewRecreationError\(') {
    throw 'WebView recreation exceptions must surface an actionable visible error instead of only logging after timeout recovery hid the InfoBar.'
}

$directCoreNavigationCalls = [regex]::Matches($webViewNavigationCommands, 'coreWebView\.(Navigate|Reload)\(')
if ($directCoreNavigationCalls.Count -ne 2 -or
    $webViewNavigationCommands -notmatch 'private bool TryNavigateCoreWebView[\s\S]*coreWebView\.Navigate\(url\)' -or
    $webViewNavigationCommands -notmatch 'private bool TryReloadCoreWebView[\s\S]*coreWebView\.Reload\(\)') {
    throw 'CoreWebView2 Navigate/Reload calls must be centralized behind helpers that are paired with PrepareNavigationStart().'
}

if ($webViewNavigation -notmatch 'await CaptureCurrentPageTokenAsync[\s\S]*if \(!IsCurrentNavigation' -or
    $webViewLifecycle -notmatch 'private bool IsCurrentNavigation') {
    throw 'Navigation completion must re-check WebView generation after awaiting page-token capture before publishing loaded state.'
}

if ($webViewPageToken -notmatch 'ObserveSessionReadyReportRequest' -or
    $webViewPageToken -notmatch 'reportSessionReady') {
    throw 'WebViewService must request a session-ready replay after accepting the hosted page token.'
}

$navigationCancellationScope = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/NavigationCancellationScope.cs') -Raw
$navigationCancellationLinks = [regex]::Matches($webViewPageToken, 'CancellationTokenSource\.CreateLinkedTokenSource\(timeout\.Token, cancellationToken\)')
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

if ($webViewNavigation -notmatch 'var navigationCancellation = _navigationCancellation' -or
    $webViewNavigation -notmatch 'var navigationLease = navigationCancellation\??\.TryAcquire\(\)' -or
    $webViewPageToken -notmatch 'CaptureCurrentPageTokenAsync\([\s\S]*CancellationToken cancellationToken = default' -or
    $navigationCancellationLinks.Count -lt 2 -or
    $webViewNavigation -notmatch 'CaptureCurrentPageTokenAsync\([\s\S]*navigationLease\.Token' -or
    $webViewNavigation -notmatch 'ObservePageTokenCaptureRetry\(sender, args\.NavigationId, completionGeneration, hostGeneration, navigationCancellation\)' -or
    $webViewNavigation -notmatch 'ObserveSessionReadyReportRequest\(sender, args\.NavigationId, completionGeneration, hostGeneration, navigationCancellation\)' -or
    $webViewPageToken -notmatch 'RetryPageTokenCaptureAsync\([\s\S]*NavigationCancellationScope\.Lease navigationLease' -or
    $webViewPageToken -notmatch 'RequestSessionReadyReportAsync\([\s\S]*NavigationCancellationScope\.Lease navigationLease' -or
    $webViewPageToken -notmatch 'Task\.Delay\(PageTokenCaptureRetryDelay, cancellationToken\)' -or
    $webViewPageToken -notmatch 'catch \(OperationCanceledException\) when \(cancellationToken\.IsCancellationRequested\)' -or
    $webViewPageToken -notmatch 'Hosted session-ready report request was interrupted by disposed resource' -or
    $webViewPageToken -notmatch 'Hosted session-ready report request failed') {
    throw 'WebView page-token retry and session-ready replay work must carry the current navigation cancellation token and observe late failures.'
}

if ($webViewNavigation -notmatch 'private async void OnNavigationCompleted[\s\S]*try[\s\S]*HandleNavigationCompletedAsync' -or
    $webViewNavigation -notmatch 'private async Task HandleNavigationCompletedAsync' -or
    $webViewNavigation -notmatch 'Navigation completion handling failed' -or
    $webViewNavigation -notmatch 'Navigation completion handling failed[\s\S]*_statusInspector\.SetUnavailableSnapshot' -or
    $webViewNavigation -notmatch 'Navigation completion handling failed[\s\S]*SetState\(ConnectionState\.Error\)' -or
    $webViewNavigation -notmatch 'Navigation completion handling failed[\s\S]*NavigationErrorOccurred\?\.Invoke') {
    throw 'WebView navigation completion async event handling must be observed, logged, and projected as an error instead of leaving Loading stale.'
}

if ($webViewSession -notmatch 'await Task\.Run\(\(\) => DeleteUserDataFolderForEnvironment\(environmentName, _logger\)\)') {
    throw 'Inactive WebView2 profile deletion must run off the UI thread.'
}

if ($webViewNavigationRecovery -notmatch 'private void OnProcessFailed[\s\S]*CancelActiveNavigation\(\)[\s\S]*_statusInspector\.SetUnavailableSnapshot') {
    throw 'WebView process-failure handling must retire navigation retry/replay cancellation before publishing unavailable state.'
}

if ($webViewNavigationRecovery -notmatch 'TryAutoRetryAfterConnectionErrorAsync' -or
    $webViewNavigationRecovery -notmatch 'AutoRetryOutcome\.Stale' -or
    $webViewNavigationRecovery -notmatch 'AutoRetryOutcome\.Failed' -or
    $webViewNavigationRecovery -notmatch 'AutoRetryOutcome\.NotAttempted' -or
    $webViewNavigation -notmatch 'Auto-retry failed before WebView2 was ready' -or
    $webViewNavigationRecovery -notmatch 'return AutoRetryOutcome\.Stale' -or
    $webViewNavigationRecovery -notmatch 'ObserveNavigationStartTimeout\(navigationGeneration, _lastNavigatedUrl, previousSource\)' -or
    $webViewNavigationRecovery -notmatch 'CancelNavigationStartWatchdog\(\)' -or
    $webViewNavigation -notmatch 'SetState\(ConnectionState\.Error\)[\s\S]*NavigationErrorOccurred\?\.Invoke\("Auto-retry failed before WebView2 was ready\."\)' -or
    $webViewNavigation -notmatch '_ when isConnectionError => ConnectionState\.Error' -or
    $webViewNavigation -match '_ when isConnectionError => ConnectionState\.Reconnecting') {
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
if ($mainWindowWebView -match '_webViewRecreationMergedCount|_pendingWebViewRecreationReason|_isRecreatingWebView|_deferredWebViewRecreationReason' -or
    $mainWindowCommands -match '_deferredWebViewRecreationReason' -or
    $mainWindowShared -match '_deferredWebViewRecreationReason') {
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
    $mainViewModelLifecycle -notmatch 'var environmentName = _selectedEnvironment\.Name' -or
    $mainViewModelLifecycle -notmatch 'var gatewayUrl = _selectedEnvironment\.GatewayUrl' -or
    $mainViewModelLifecycle -notmatch 'InitializeAsync\(webView, environmentName, cancellationToken\)' -or
    $mainViewModelLifecycle -notmatch 'InitializeAsync\(webView, cancellationToken\)' -or
    $mainViewModelLifecycle -notmatch 'IsCurrentSelectedEnvironment\(environmentName, gatewayUrl\)' -or
    $mainViewModelLifecycle -notmatch 'private bool IsCurrentSelectedEnvironment\(string environmentName, string gatewayUrl\)') {
    throw 'MainViewModel WebView initialization must honor the ViewModel lifetime cancellation token and selected-environment identity across awaits.'
}

$environmentConfig = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Models/EnvironmentConfig.cs') -Raw
if ($environmentConfig -notmatch 'PlaceholderGatewayUrl = "https://example\.com"' -or
    $environmentConfig -notmatch '\[JsonIgnore\][\s\S]*public bool IsPlaceholder') {
    throw 'EnvironmentConfig must expose a JSON-ignored placeholder-environment predicate for first-run startup gating.'
}

$mainViewModelEnvironment = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/MainViewModel.Environment.cs') -Raw
$mainViewModelConstructor = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/MainViewModel.cs') -Raw
if ($mainViewModelLifecycle -notmatch 'if \(_selectedEnvironment\.IsPlaceholder\)[\s\S]*ApplyPlaceholderEnvironmentState\(\)[\s\S]*return;' -or
    $mainViewModelLifecycle -match 'IsPlaceholder[\s\S]{0,900}Navigate\(' -or
    $mainViewModelLifecycle -notmatch '_selectedEnvironment\?\.IsPlaceholder == true[\s\S]*ApplyPlaceholderEnvironmentState\(\)' -or
    $mainViewModelEnvironment -notmatch 'environment\.IsPlaceholder[\s\S]*var shouldClearWebViewHost = _webViewService\.IsInitialized[\s\S]*ApplyPlaceholderEnvironmentState\(\)' -or
    $mainViewModelEnvironment -notmatch 'WebViewRecreationRequested\?\.Invoke\("environment_placeholder_selected"\)' -or
    $mainViewModelEnvironment -notmatch 'private void ApplyPlaceholderEnvironmentState\(\)' -or
    $mainViewModelEnvironment -notmatch 'ApplyConnectionState\((?:OpenClaw\.Services\.)?ConnectionState\.Offline\)' -or
    $mainViewModelEnvironment -notmatch 'StatusMessage = StringResources\.StatusConfigureGateway' -or
    $mainViewModelEnvironment -notmatch '_webViewService\.StopHeartbeat\(\)' -or
    $mainViewModelEnvironment -notmatch '_latencyService\.Stop\(\)' -or
    $mainViewModelEnvironment -notmatch 'WebViewRecreationRequested\?\.Invoke\("environment_placeholder_replaced"\)' -or
    $mainViewModelConstructor -match 'LoadEnvironments\(\);\s*UpdateStatusPresentation\(\);') {
    throw 'Placeholder environment selection must stop probes, clear stale status, and avoid navigating to example.com.'
}

if ($mainWindowWebView -notmatch 'ViewModel\.IsPlaceholderEnvironment[\s\S]*ClearWebViewHostForPlaceholderEnvironment\(reason\)' -or
    $mainWindowWebView -notmatch 'ViewModel\.IsPlaceholderEnvironment[\s\S]*_webViewRecreationService\.HasPendingDeferredOrActiveWork \|\| WebViewHost\.Children\.Count > 0[\s\S]*ClearWebViewHostForPlaceholderEnvironment\("deferred_resume_placeholder"\)' -or
    $mainWindowWebView -notmatch 'private void ClearWebViewHostForPlaceholderEnvironment\(string reason\)[\s\S]*_webViewRecreationService\.ClearPending\(\)[\s\S]*ViewModel\.DetachWebViewHost\(\)[\s\S]*WebViewHost\.Children\.Clear\(\)' -or
    $mainWindowWebView -notmatch 'ShouldSkipCurrentRecreation\(\)[\s\S]*webview\.recreation\.skipped_after_navigation_recovered' -or
    $mainWindowWebView -notmatch 'webview\.recreation\.skipped_placeholder_environment') {
    throw 'MainWindow must skip WebView host recreation while the selected environment is the first-run placeholder.'
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

if ($coordinatorStateEffects -notmatch 'case ControlUiPhase\.GatewayError:[\s\S]*case ControlUiPhase\.Unavailable:[\s\S]*MarkRecoveryDegraded\(snapshot\.DetailOrSummary\)') {
    throw 'ShellSessionCoordinator terminal hosted-session failures must move recovery state out of stale Ready/Healthy state.'
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

$settingsViewModelWebViewServiceCalls = [regex]::Matches($settingsViewModel, 'WebViewService\.(?<method>[A-Za-z_][A-Za-z0-9_]*)')
foreach ($call in $settingsViewModelWebViewServiceCalls) {
    if ($call.Groups['method'].Value -ne 'TryMoveUserDataFolderToRenamedEnvironment') {
        throw "SettingsViewModel must not call WebViewService runtime/session APIs; keep WebView work behind MainViewModel/MainWindow/service boundaries: $($call.Value)"
    }
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

$settingsViewModel = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/SettingsViewModel.cs') -Raw
if ($settingsPersistenceAdapter -notmatch 'public SettingsPersistenceSaveResult Save\(\)' -or
    $settingsPersistenceAdapter -match 'void Save\(\)' -or
    $settingsViewModel -notmatch 'var saveResult = _settingsPersistence\.Save\(\)' -or
    $settingsViewModel -notmatch 'if \(!saveResult\.Succeeded\)' -or
    $settingsViewModel -match '_settingsPersistence\.Save\(\);\s*ValidationMessage') {
    throw 'Settings save failures must flow from persistence to SettingsViewModel instead of being reported as successful saves.'
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

if ($configurationService -notmatch 'public SettingsWriteResult Save\(\)' -or
    $configurationService -notmatch 'return SettingsWriteResult\.Success' -or
    $configurationService -notmatch 'return SettingsWriteResult\.Failure' -or
    $configurationService -match 'public void Save\(\)') {
    throw 'ConfigurationService.Save must return a write result so callers can surface persistence failures.'
}

$settingsDialogShared = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Views/SettingsDialog.Shared.cs') -Raw
if ($settingsDialogShared -match 'record struct SettingsSaveResult') {
    throw 'SettingsSaveResult must live in OpenClaw.Core models, not SettingsDialog.Shared.cs.'
}

$settingsDialogTheme = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Views/SettingsDialog.Theme.cs') -Raw
if ($settingsDialogTheme -notmatch 'ReloadFromCurrentSettings') {
    throw 'Prewarmed SettingsDialog must reload its ViewModel from current persisted settings before activation.'
}

$appSettings = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Models/AppSettings.cs') -Raw
$settingsDialogInitialization = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Views/SettingsDialog.Initialization.cs') -Raw
if ($appSettings -notmatch 'SettingsWindowWidth' -or
    $appSettings -notmatch 'SettingsWindowHeight' -or
    $appSettings -notmatch 'SettingsWindowLeft' -or
    $appSettings -notmatch 'SettingsWindowTop' -or
    $configurationService -notmatch 'NormalizeSettingsWindowBounds' -or
    $settingsDialogInitialization -notmatch 'RestoreSettingsWindowBounds' -or
    $settingsDialogInitialization -notmatch 'SaveSettingsWindowBounds' -or
    $settingsDialogInitialization -notmatch 'WindowFrameHelper\.TryGetWindowRect' -or
    $settingsDialogInitialization -notmatch 'WindowFrameHelper\.TrySetWindowRect' -or
    $settingsDialogInitialization -notmatch 'WindowFrameHelper\.IsNativeWindowRectVisibleWithinAnyMonitor' -or
    $settingsDialogInitialization -notmatch 'WindowFrameHelper\.TryCenterNativeWindowRectInNearestMonitor' -or
    $settingsDialogInitialization -notmatch 'WindowBoundsUtilities\.CanPersistWindowBounds') {
    throw 'SettingsDialog must persist and restore its own window bounds instead of reopening at the default size and position.'
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

$shouldClearModelSummaryMethod = [regex]::Match($viewModelStatus, 'private static bool ShouldClearModelSummary\(ControlUiProbeSnapshot snapshot\)[\s\S]*?\n    \}')
if (-not $shouldClearModelSummaryMethod.Success) {
    throw 'MainViewModel must keep MODEL reset policy in ShouldClearModelSummary.'
}

if ($shouldClearModelSummaryMethod.Value -match 'ControlUiPhase\.Unavailable|ControlUiPhase\.Unknown') {
    throw 'Transient unavailable/unknown Control UI inspections must not clear the last non-empty MODEL summary.'
}

if ($shouldClearModelSummaryMethod.Value -match 'ControlUiPhase\.Loading') {
    throw 'Transient loading/navigation Control UI snapshots must not clear the last non-empty MODEL summary.'
}

if ($viewModelStatus -notmatch 'ApplySnapshotErrorState\(ControlUiProbeSnapshot snapshot\)[\s\S]*snapshot\.IsIssue[\s\S]*ErrorMessage = snapshot\.DetailOrSummary[\s\S]*IsErrorVisible = true') {
    throw 'Control UI issue snapshots must show a visible error InfoBar, not only change status text.'
}

if ($viewModelStatus -notmatch 'snapshot\.Phase == ControlUiPhase\.Unavailable[\s\S]*ConnectionState == ConnectionState\.Reconnecting[\s\S]*ErrorMessage = snapshot\.DetailOrSummary[\s\S]*IsErrorVisible = true') {
    throw 'Terminal Control UI Unavailable snapshots must show a visible InfoBar while the shell is reconnecting.'
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

$viewModelHeartbeat = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/MainViewModel.Heartbeat.cs') -Raw
$viewModelIndicators = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/MainViewModel.Indicators.cs') -Raw
if ($viewModelIndicators -notmatch 'IsLatencySnapshotForSelectedEnvironment' -or
    $viewModelIndicators -notmatch 'TryGetEnvironmentHost') {
    throw 'MainViewModel latency updates must reject stale snapshots from non-selected environment hosts.'
}

if ($mainViewModelLifecycle -notmatch 'ApplyWebViewHostDetachedState\(\)' -or
    $mainViewModelLifecycle -notmatch '_latencyService\.Stop\(\)[\s\S]*_webViewService\.StopHeartbeat\(\)[\s\S]*ResetResourceProbeProjection\(\)' -or
    $mainViewModelLifecycle -notmatch 'ApplyWebViewHostDetachedState\(\)[\s\S]*ApplyConnectionState\(ConnectionState\.Loading\)[\s\S]*ResetTelemetry\(\)[\s\S]*ApplyRecoveryState\(RecoveryState\.Connecting\)' -or
    $viewModelHeartbeat -notmatch 'ResetHeartbeatProjection\(\)[\s\S]*HeartbeatSummary = StringResources\.HeartbeatWait[\s\S]*ResetHeartbeatIndicatorsToWarning\(\)' -or
    $viewModelIndicators -notmatch 'ResetLatencyProjection\(\)[\s\S]*LatencySummaryText = DefaultLatencySummary[\s\S]*LatencySummaryBrush = NeutralBrush' -or
    $viewModelIndicators -notmatch 'snapshot\.State == ControlUiLatencyState\.Unknown') {
    throw 'Stopping probes or detaching WebView must reset visible heartbeat, latency, MODEL, access, work, and shell projections instead of leaving stale healthy status visible.'
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

$appGlobalAllowedFiles = @(
    'src/OpenClaw/App.xaml.cs',
    'src/OpenClaw/MainWindow.xaml.cs',
    'src/OpenClaw/MainWindow.AlwaysOnTop.cs',
    'src/OpenClaw/MainWindow.Commands.cs',
    'src/OpenClaw/MainWindow.CompactMode.cs',
    'src/OpenClaw/MainWindow.Hotkey.cs',
    'src/OpenClaw/MainWindow.Lifecycle.cs',
    'src/OpenClaw/MainWindow.Theme.cs',
    'src/OpenClaw/MainWindow.Tray.cs',
    'src/OpenClaw/MainWindow.WebView.cs',
    'src/OpenClaw/Views/LogViewerDialog.xaml.cs',
    'src/OpenClaw/Views/SettingsDialog.Actions.cs',
    'src/OpenClaw/Views/SettingsDialog.Initialization.cs',
    'src/OpenClaw/Views/SettingsDialog.Theme.cs',
    'src/OpenClaw/Views/SettingsDialog.xaml.cs'
)
$appGlobalAccessMatches = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/OpenClaw') -Recurse -File -Filter *.cs |
    Select-String -Pattern 'App\.(Logger|Configuration|MainWindow)'
foreach ($match in $appGlobalAccessMatches) {
    $relativePath = $match.Path.Substring($repoRootPath.Length + 1).Replace('\', '/')
    if ($relativePath -notin $appGlobalAllowedFiles) {
        throw "App global access must stay at the WinUI app edge and use injected/adapted dependencies elsewhere: ${relativePath}:$($match.LineNumber)"
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

if ($commands -notmatch 'private void OnReload\(\)[\s\S]*if \(_webViewService\.Reload\(\)\)[\s\S]*IsErrorVisible = false') {
    throw 'Reload must clear stale visible errors only after WebViewService confirms reload navigation started.'
}

if ($commands -notmatch 'public void ShowWebViewRecreationError\(string message\)[\s\S]*ApplyConnectionState\(ConnectionState\.Error\)[\s\S]*ErrorMessage = message[\s\S]*IsErrorVisible = true[\s\S]*ShowRetryButton = true') {
    throw 'WebView recreation failure projection must publish an actionable visible error state.'
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

    if ($resources -notmatch 'name="WebViewRecreationFailedFormat"') {
        throw "Missing localized WebView recreation failure resource: $resourceFile"
    }

    if ($resources -notmatch 'name="SettingsControlUiUrlPlaceholder"') {
        throw "Missing localized Control UI URL placeholder resource: $resourceFile"
    }

    if ($resources -notmatch 'name="StatusConfigureGateway"') {
        throw "Missing localized placeholder-environment status resource: $resourceFile"
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

if ($diagnosticService -match 'WebViewService\s+\w+|WebViewService\?\s+\w+') {
    throw 'DiagnosticService must depend on a diagnostic WebView interface instead of the concrete WebViewService.'
}

$diagnosticWebViewSession = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/DiagnosticWebViewSession.cs') -Raw
if ($diagnosticWebViewSession -notmatch 'interface IDiagnosticWebViewSession' -or
    $diagnosticWebViewSession -notmatch 'InspectControlUiStateAsync' -or
    $diagnosticWebViewSession -notmatch 'LatestControlUiSnapshot' -or
    $webViewService -notmatch 'WebViewService\s*:\s*IDiagnosticWebViewSession,\s*IDisposable') {
    throw 'Diagnostic WebView session abstraction must exist and WebViewService must implement it explicitly.'
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
    $activityStateScript -notmatch 'isBusy: isChatBusy \|\| isShellBusy' -or
    $activityStateScript -notmatch 'activitySignature: isChatBusy \? readChatActivitySignature\(app\) : ''' -or
    $statusInspectionScript -notmatch 'const isBusyStaleCandidate = Boolean\(appState\?\.isChatBusy\) \|\| apiBusy \|\| domBusy' -or
    $statusInspectionScript -match 'isBusyStaleCandidate = Boolean\(appState\?\.isBusy\)') {
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
