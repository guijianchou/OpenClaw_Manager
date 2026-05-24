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

$coreFiles = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core') -Recurse -File -Include *.cs
$forbiddenCorePattern = 'using Microsoft\.UI|using Microsoft\.Web\.WebView2|using Windows\.Graphics|using Windows\.UI|using WinRT|App\.Configuration|App\.Logger|App\.MainWindow'
foreach ($file in $coreFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    if ($content -match $forbiddenCorePattern) {
        throw "Core boundary violation: $($file.FullName)"
    }
}

$project = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/OpenClaw.csproj') -Raw
foreach ($resource in @(
    'HostedUiBridge.Script.js',
    'HostedUiBridge.HostMessaging.js',
    'HostedUiBridge.MutationFilter.js',
    'HostedUiBridge.ModelResolver.js',
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

$webViewService = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.cs') -Raw
if ($webViewService -match 'ParseControlUiSnapshot|ExecuteControlUiInspectionAsync|_latestControlUiSnapshot') {
    throw 'WebViewService.cs must not own Control UI inspection internals.'
}

$heartbeat = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.Heartbeat.cs') -Raw
if ($heartbeat -match 'CancellationTokenSource\? _heartbeatCts|Task\? _heartbeatTask|ObserveHeartbeatShutdownAsync') {
    throw 'Heartbeat loop ownership must live in HeartbeatRuntime.'
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

$commandFile = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.Commands.cs') -Raw
if ($commandFile -match 'ExecuteScriptAsync\(@"|ExecuteScriptAsync\(\$@"|const string script = """') {
    throw 'WebViewService.Commands.cs must load browser scripts from embedded JS assets, not large inline strings.'
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

$settingsViewModel = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/SettingsViewModel.cs') -Raw
if ($settingsViewModel -match 'using OpenClaw\.Views;') {
    throw 'SettingsViewModel must not depend on the Views namespace for SettingsSaveResult.'
}

$settingsDialogShared = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Views/SettingsDialog.Shared.cs') -Raw
if ($settingsDialogShared -match 'record struct SettingsSaveResult') {
    throw 'SettingsSaveResult must live in OpenClaw.Core models, not SettingsDialog.Shared.cs.'
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

$mainWindowXaml = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.xaml') -Raw
if ($mainWindowXaml -notmatch 'x:Name="RootLayout"[\s\S]*VisualStateManager\.VisualStateGroups') {
    throw 'Compact visual states must be attached to RootLayout.'
}

$mainBridgeScript = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/HostedUiBridge.Script.js') -Raw
$mainBridgeLines = ($mainBridgeScript -split "`n").Count
if ($mainBridgeLines -gt 250) {
    throw "HostedUiBridge.Script.js should be a composition file under 250 lines; found $mainBridgeLines."
}

foreach ($asset in @(
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
    '__OPENCLAW_HOST_MESSAGING_SCRIPT__',
    '__OPENCLAW_MUTATION_FILTER_SCRIPT__',
    '__OPENCLAW_MODEL_RESOLVER_SCRIPT__',
    '__OPENCLAW_STATUS_INSPECTION_SCRIPT__',
    '__OPENCLAW_COMMAND_DISPATCH_SCRIPT__'
)) {
    if ($mainBridgeScript -notmatch [regex]::Escape($placeholder)) {
        throw "HostedUiBridge.Script.js is missing composition placeholder: $placeholder"
    }
}

Write-Host 'PASS: repository structure guardrails'
