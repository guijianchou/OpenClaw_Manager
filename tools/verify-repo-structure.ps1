Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

$ciWorkflowPath = Join-Path $repoRoot '.github/workflows/ci.yml'
if (-not (Test-Path -LiteralPath $ciWorkflowPath)) {
    throw 'GitHub Actions CI workflow must exist at .github/workflows/ci.yml.'
}

$ciWorkflow = Get-Content -LiteralPath $ciWorkflowPath -Raw -Encoding UTF8
function Assert-CiPattern {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Pattern,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if ($ciWorkflow -notmatch $Pattern) {
        throw $Message
    }
}

Assert-CiPattern '(?m)^\s*runs-on:\s*windows-2025-vs2026\s*$' 'GitHub Actions CI must pin the Windows runner to windows-2025-vs2026.'
Assert-CiPattern '(?ms)- name:\s*Checkout\s+uses:\s*actions/checkout@v4' 'GitHub Actions CI must check out the repository.'
Assert-CiPattern '(?ms)- name:\s*Setup \.NET\s+uses:\s*actions/setup-dotnet@v4\s+with:\s+dotnet-version:\s*10\.0\.300' 'GitHub Actions CI must install the pinned .NET SDK 10.0.300.'
Assert-CiPattern '(?ms)- name:\s*Setup Node\.js\s+uses:\s*actions/setup-node@v4\s+with:\s+node-version:\s*24\.x' 'GitHub Actions CI must install Node.js 24.x for bridge verification.'
Assert-CiPattern '(?ms)- name:\s*Restore locked packages\s+shell:\s*pwsh\s+run:\s*dotnet restore OpenClaw\.sln --locked-mode' 'GitHub Actions CI must restore locked packages.'
Assert-CiPattern '(?ms)- name:\s*Run Core executable harness\s+shell:\s*pwsh\s+run:\s*dotnet run --no-restore --project tests\\OpenClaw\.Core\.Tests\\OpenClaw\.Core\.Tests\.csproj' 'GitHub Actions CI must run the Core executable harness.'
Assert-CiPattern '(?ms)- name:\s*Run VSTest workflow\s+shell:\s*pwsh\s+run:\s*dotnet test OpenClaw\.sln -c Debug -p:Platform=x64 --no-restore' 'GitHub Actions CI must run VSTest for x64.'
Assert-CiPattern '(?ms)- name:\s*Build WinUI x64\s+shell:\s*pwsh\s+run:\s*dotnet build OpenClaw\.sln -c Debug -p:Platform=x64 --no-restore' 'GitHub Actions CI must build WinUI x64.'
Assert-CiPattern '(?ms)- name:\s*Verify formatting\s+shell:\s*pwsh\s+run:\s*\|\s*\r?\n\s*\$env:Platform = ''x64''\s*\r?\n\s*dotnet format OpenClaw\.sln --verify-no-changes --no-restore' 'GitHub Actions CI must verify formatting with Platform=x64.'
Assert-CiPattern '(?ms)- name:\s*Verify repository guardrails\s+shell:\s*pwsh\s+run:\s*powershell -NoProfile -ExecutionPolicy Bypass -File tools\\verify-repo-structure\.ps1' 'GitHub Actions CI must run repository guardrails.'
Assert-CiPattern '(?ms)- name:\s*Verify embedded bridge scripts\s+shell:\s*pwsh\s+run:\s*powershell -NoProfile -ExecutionPolicy Bypass -File tools\\verify-bridge-scripts\.ps1' 'GitHub Actions CI must verify embedded bridge scripts.'
Assert-CiPattern '(?ms)- name:\s*Verify whitespace\s+shell:\s*pwsh\s+run:\s*git diff --check' 'GitHub Actions CI must verify whitespace.'

$globalJsonPath = Join-Path $repoRoot 'global.json'
if (-not (Test-Path -LiteralPath $globalJsonPath)) {
    throw 'global.json must pin the supported .NET SDK feature band.'
}

$globalJson = Get-Content -LiteralPath $globalJsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($globalJson.sdk.version -ne '10.0.300' -or $globalJson.sdk.rollForward -ne 'latestPatch') {
    throw 'global.json must pin SDK 10.0.300 with rollForward latestPatch.'
}

$testsPath = Join-Path $repoRoot 'tests'
$coreTestsProjectPath = Join-Path $testsPath 'OpenClaw.Core.Tests/OpenClaw.Core.Tests.csproj'
if (-not (Test-Path -LiteralPath $coreTestsProjectPath)) {
    throw 'The Core regression harness must exist at tests/OpenClaw.Core.Tests.'
}

$solution = Get-Content -LiteralPath (Join-Path $repoRoot 'OpenClaw.sln') -Raw
if ($solution -match 'OpenClaw\.Tests|tests\\OpenClaw\.Tests') {
    throw 'OpenClaw.sln still references the removed test harness.'
}

if ($solution -notmatch 'tests\\OpenClaw\.Core\.Tests\\OpenClaw\.Core\.Tests\.csproj') {
    throw 'OpenClaw.sln must include the Core regression harness.'
}

$coreTestsProject = Get-Content -LiteralPath $coreTestsProjectPath -Raw
$coreTests = Get-Content -LiteralPath (Join-Path $repoRoot 'tests/OpenClaw.Core.Tests/Program.cs') -Raw
if ($coreTestsProject -notmatch 'Microsoft\.NET\.Test\.Sdk' -or
    $coreTestsProject -notmatch 'MSTest\.TestFramework' -or
    $coreTestsProject -notmatch 'MSTest\.TestAdapter' -or
    $coreTestsProject -notmatch '<IsTestProject>true</IsTestProject>' -or
    $coreTestsProject -notmatch '<GenerateProgramFile>false</GenerateProgramFile>' -or
    $coreTests -notmatch '\[TestClass\]' -or
    $coreTests -notmatch '\[TestMethod\]' -or
    $coreTests -notmatch 'await Program\.Main\(\)') {
    throw 'OpenClaw.Core.Tests must be discoverable by dotnet test while preserving the executable Program.Main harness.'
}

foreach ($testName in @(
    'Cloudflare1033ResponseBodyIsDetectedThroughProductionEntryPointAsync',
    'Cloudflare1033HeaderIsDetectedThroughProductionEntryPointAsync',
    'Cloudflare1033CodeHeaderIsDetectedThroughProductionEntryPointAsync',
    'CloudflareBrandedBodyWithUnrelated1033RemainsServerOrProxyErrorAsync',
    'CloudflareBodySnippetReadTimeoutFallsBackToStatusClassificationAsync',
    'ProbeUriAppendsAtRoot',
    'ProbeUriNormalizesConfiguredEndpointWithoutTrailingSlash',
    'ProbeKeyDistinguishesBasePathsAndPorts',
    'ProbeUriStripsUserInfo',
    'ProbeUriRejectsInvalidUrls',
    'SettingsWindowBoundsUseDedicatedPersistedWidthFloor',
    'LatencyHistoryClearRemovesStaleSamples',
    'ClassifierMarksMissingAndMethodRejectedPathsAsFailures',
    'ClassifierMarksAuthRateLimitAsReachableUserAction',
    'ClassifierMarksServerOrProxyErrorsAsUnreachable',
    'HeartbeatMapsAuthRateLimitToSessionBlocked',
    'HeartbeatMapsRedirectsToFailure',
    'HeartbeatMapsMissingPathToFailure',
    'LatencyServicePublishesRedirectsAsFailureAsync',
    'LatencyServicePublishesSuccessAsync',
    'DiagnosticsMapperMarksPathAndProxyFailuresAsFailures',
    'DiagnosticsMapperDistinguishesPassWarningAndFailureStates',
    'DiagnosticsMapperMarksRedirectsAsFailures',
    'DiagnosticBundleRedactsCopiedLogFilesAsync',
    'DiagnosticBundleUsesUniquePathsForRepeatedExportsAsync',
    'DiagnosticBundleLimitsTotalLogPayloadAndRedactsHeadersAsync',
    'DiagnosticProbeDowngradesReachableNonLocalHttpToWarningAsync',
    'HeartbeatResolverPreservesHostedConnectingStateWhenTransportFails',
    'HeartbeatResolverMapsTransportSessionBlockedToUserAction',
    'LatencyServiceDoesNotPublishAuthRequiredAsSuccessAsync',
    'SessionReadyAcceptsDeepRoutesUnderCurrentGatewayBasePathAsync',
    'SessionReadyRejectsCaseMismatchedGatewayBasePathAsync',
    'StaleSessionReadyDoesNotClearCurrentEnvironmentRecoveryStateAsync',
    'HardRefreshCooldownStartsOnlyAfterReloadSucceedsAsync',
    'HardRefreshReloadsTransitionalHostedUiStatesAsync',
    'ConfigurationNormalizesInvalidEnvironmentEntriesAsync',
    'ConfigurationNormalizesCaseOnlyDuplicateEnvironmentNamesAsync',
    'ConfigurationCandidateSaveReplacesLiveSettingsOnlyAfterWriteSuccessAsync',
    'LiveShellSettingsTolerateNullablePersistedHotkey'
)) {
    if ($coreTests -notmatch [regex]::Escape($testName)) {
        throw "Core regression harness is missing required Gateway/Control UI test: $testName"
    }
}

$coreProjectId = '{BC4C7184-C8DD-4748-AC82-D26123568BD1}'
$appProjectId = '{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}'
$coreTestsProjectId = '{CCE9A104-662A-4ADD-8953-AFD82C475B57}'
$coreProject = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/OpenClaw.Core.csproj') -Raw
if ($coreProject -match '<Platforms>') {
        throw 'OpenClaw.Core.csproj must stay platform-independent. Do not declare architecture-specific platforms on the pure SDK class library; map the x64 solution platform to Debug/Release|Any CPU instead.'
}

foreach ($forbiddenPlatformToken in @('Debug|x86', 'Release|x86', 'Debug|ARM64', 'Release|ARM64')) {
    if ($solution -match [regex]::Escape($forbiddenPlatformToken)) {
        throw "OpenClaw.sln must expose only x64 solution platforms; found $forbiddenPlatformToken."
    }
}

foreach ($requiredSolutionPlatform in @('Debug|x64 = Debug|x64', 'Release|x64 = Release|x64')) {
    if ($solution -notmatch [regex]::Escape($requiredSolutionPlatform)) {
        throw "OpenClaw.sln is missing required x64 solution platform: $requiredSolutionPlatform"
    }
}

$coreSolutionMappingPattern = [regex]::Escape($coreProjectId) + '\.(Debug|Release)\|x64\.(ActiveCfg|Build\.0) = (Debug|Release)\|([^`\r\n]+)'
foreach ($coreSolutionMapping in [regex]::Matches($solution, $coreSolutionMappingPattern)) {
    if ($coreSolutionMapping.Groups[4].Value -ne 'Any CPU') {
        throw "OpenClaw.Core solution platform mappings must target the pure class-library Any CPU configuration; invalid mapping: $($coreSolutionMapping.Value)"
    }
}

$allCoreSolutionMappingPattern = [regex]::Escape($coreProjectId) + '\.(?<configuration>[^|`\r\n]+)\|(?<solutionPlatform>[^.=`\r\n]+)\.(?<mapping>ActiveCfg|Build\.0|Deploy\.0) = (?<projectConfiguration>[^|`\r\n]+)\|(?<projectPlatform>[^`\r\n]+)'
foreach ($coreSolutionMapping in [regex]::Matches($solution, $allCoreSolutionMappingPattern)) {
    if ($coreSolutionMapping.Groups['configuration'].Value -notin @('Debug', 'Release') -or
        $coreSolutionMapping.Groups['projectConfiguration'].Value -ne $coreSolutionMapping.Groups['configuration'].Value -or
        $coreSolutionMapping.Groups['solutionPlatform'].Value -ne 'x64' -or
        $coreSolutionMapping.Groups['projectPlatform'].Value -ne 'Any CPU') {
        throw "OpenClaw.Core solution mappings must map Debug/Release x64 solution platforms to the matching Debug/Release|Any CPU project configuration: $($coreSolutionMapping.Value)"
    }
}

foreach ($configuration in @('Debug', 'Release')) {
    foreach ($mapping in @('ActiveCfg', 'Build.0')) {
        $expectedCoreMapping = "$coreProjectId.$configuration|x64.$mapping = $configuration|Any CPU"
        if ($solution -notmatch [regex]::Escape($expectedCoreMapping)) {
            throw "OpenClaw.Core solution platform mapping is missing or invalid: $expectedCoreMapping"
        }

        $expectedCoreTestsMapping = "$coreTestsProjectId.$configuration|x64.$mapping = $configuration|Any CPU"
        if ($solution -notmatch [regex]::Escape($expectedCoreTestsMapping)) {
            throw "OpenClaw.Core.Tests solution platform mapping is missing or invalid: $expectedCoreTestsMapping"
        }
    }

    foreach ($mapping in @('ActiveCfg', 'Build.0', 'Deploy.0')) {
        $expectedAppMapping = "$appProjectId.$configuration|x64.$mapping = $configuration|x64"
        if ($solution -notmatch [regex]::Escape($expectedAppMapping)) {
            throw "OpenClaw app solution platform mapping is missing or invalid: $expectedAppMapping"
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
if ($project -notmatch [regex]::Escape('<TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>') -or
    $project -notmatch [regex]::Escape('<TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>') -or
    $project -notmatch [regex]::Escape('<Platforms>x64</Platforms>') -or
    $project -notmatch [regex]::Escape('<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>') -or
    $project -match '<Platforms>[^<]*(x86|ARM64)[^<]*</Platforms>' -or
    $project -match '<RuntimeIdentifiers?>[^<]*(win-x86|win-arm64)[^<]*</RuntimeIdentifiers?>') {
    throw 'OpenClaw.csproj must target SDK 10.0.26100.0, declare Windows 10 1809 as the minimum platform, and expose only x64 / win-x64.'
}

$packageLock = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/packages.lock.json') -Raw
if ($packageLock -match 'win-x86|win-arm64' -or
    $packageLock -notmatch 'net10\.0-windows10\.0\.26100/win-x64') {
    throw 'OpenClaw packages.lock.json must contain only the win-x64 runtime graph for the WinUI app.'
}

$linkedCompileItems = [regex]::Matches($project, '<Compile\s+(?:Include|Update)="(?<path>[^"]+)"')
foreach ($linkedCompileItem in $linkedCompileItems) {
    $compilePath = $linkedCompileItem.Groups['path'].Value
    if ($compilePath -match '(^|[\\/])\.\.[\\/]' -or
        $compilePath -match '(^|[\\/])OpenClaw\.Core([\\/]|$)') {
        throw "OpenClaw.csproj must not link Core source files or compile sources outside the WinUI project: $compilePath"
    }
}

$currentVersion = '5.1.2'
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

if ([regex]::Matches($packageManifest, 'MinVersion="10\.0\.17763\.0"').Count -ne 2 -or
    [regex]::Matches($packageManifest, 'MaxVersionTested="10\.0\.26100\.0"').Count -ne 2) {
    throw 'Package.appxmanifest must keep Windows 10 1809 as MinVersion and SDK 10.0.26100.0 as MaxVersionTested for both target device families.'
}

if ($appManifest -notmatch [regex]::Escape("version=`"$currentFileVersion`"")) {
    throw "app.manifest assembly identity version must stay aligned at $currentFileVersion."
}

$readme = Get-Content -LiteralPath (Join-Path $repoRoot 'README.md') -Raw -Encoding UTF8
$codeStyle = Get-Content -LiteralPath (Join-Path $repoRoot 'docs/code-style.md') -Raw -Encoding UTF8
$deepRefactorPlan = Get-Content -LiteralPath (Join-Path $repoRoot 'docs/superpowers/plans/2026-05-23-deep-refactor-hardening.md') -Raw -Encoding UTF8
$readmeZh = Get-Content -LiteralPath (Join-Path $repoRoot 'readme_zh.md') -Raw -Encoding UTF8
$readmeZhLines = Get-Content -LiteralPath (Join-Path $repoRoot 'readme_zh.md') -Encoding UTF8
$changelogLines = Get-Content -LiteralPath (Join-Path $repoRoot 'changelog.md') -Encoding UTF8
$changelog = Get-Content -LiteralPath (Join-Path $repoRoot 'changelog.md') -Raw -Encoding UTF8
$readmeZhHeading = $readmeZhLines | Where-Object { $_ -like "## *$currentVersion*" } | Select-Object -First 1
$currentVersionCodeSpan = '`' + $currentVersion + '`'
$changelogMetadataLines = $changelogLines | Where-Object {
    $_ -match 'app.*assembly.*file.*package manifest.*application manifest.*README.*changelog' -and
    $_ -match [regex]::Escape($currentVersionCodeSpan)
}
if ($readme -notmatch [regex]::Escape("**Current version:** $currentVersion") -or
    $readme -notmatch [regex]::Escape("## Current $currentVersion Notes") -or
    $readme -notmatch [regex]::Escape('Windows 10 1809+ or Windows 11, x64 only') -or
    $readmeZhLines.Count -lt 5 -or
    $readmeZhLines[4] -notmatch [regex]::Escape($currentVersion) -or
    $readmeZh -notmatch 'Windows 10 1809' -or
    $readmeZh -notmatch 'x64' -or
    [string]::IsNullOrWhiteSpace($readmeZhHeading) -or
    $changelog -notmatch [regex]::Escape("metadata to $currentVersionCodeSpan") -or
    $changelogMetadataLines.Count -lt 2) {
    throw "README, Chinese README, and changelog current-version metadata must stay aligned at $currentVersion."
}

if ($codeStyle -match 'There is no active `\.NET tests/` regression harness|no-`tests/` checkpoint|not a valid substitute|false green|C:\\Users\\Zen\\\.cache\\codex-runtimes' -or
    $codeStyle -notmatch 'dotnet run --no-restore --project tests\\OpenClaw\.Core\.Tests\\OpenClaw\.Core\.Tests\.csproj' -or
    $codeStyle -notmatch 'dotnet test OpenClaw\.sln -c Debug -p:Platform=x64 --no-restore' -or
    $codeStyle -notmatch '`tests\\OpenClaw\.Core\.Tests` is the active executable Core regression harness' -or
    $codeStyle -notmatch 'supported VSTest workflow' -or
    $codeStyle -notmatch 'set `OPENCLAW_NODE` to a specific executable in your local shell') {
    throw 'docs/code-style.md must describe the active Core harness and avoid machine-local Node paths.'
}

if ($deepRefactorPlan -match 'C:\\Users\\Zen\\\.cache\\codex-runtimes|tests/` is intentionally absent|active solution free of `tests/`' -and
    ($deepRefactorPlan -notmatch 'This file is implementation history, not the current verification source of truth' -or
     $deepRefactorPlan -notmatch 'historical local verification evidence only' -or
     $deepRefactorPlan -notmatch 'Sections that say `tests/` is absent describe an older checkpoint')) {
    throw 'Historical deep-refactor plan must mark stale no-tests and machine-local Node snippets as historical.'
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

foreach ($requiredResourceKey in @(
    'DiagnosticBundleExportedFormat',
    'DiagnosticBundleExportFallbackDirectoryName',
    'StatusDefaultHeartbeat',
    'StatusDefaultAccess',
    'StatusDefaultLatency',
    'SettingsDevToolsUnavailable',
    'SettingsDevToolsDisabled',
    'SettingsDevToolsOpenFailedFormat',
    'AccessStatusOk',
    'AccessStatusLogin',
    'AccessStatusPair',
    'AccessStatusOrigin',
    'AccessStatusWait',
    'WorkStatusLive',
    'WorkStatusIdle',
    'WorkStatusWait'
)) {
    if ($englishResourceKeys -notcontains $requiredResourceKey -or
        $chineseResourceKeys -notcontains $requiredResourceKey) {
        throw "Localized resource key missing: $requiredResourceKey"
    }
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
$webViewProfile = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.Profile.cs') -Raw
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
    $webViewSession -notmatch 'public DevToolsOpenResult OpenDevTools\(' -or
    $webViewSession -notmatch 'public async Task ClearEnvironmentSessionAsync' -or
    $webViewSession -notmatch 'public string\? GetCurrentUrl\(' -or
    $webViewSession -notmatch 'public bool IsUsingEnvironmentProfile\(') {
    throw 'WebViewService.cs must keep fields, events, construction, and navigation commands; lifecycle and session/profile operations belong in dedicated partials.'
}

if ($webViewLifecycle -notmatch 'CoreWebView2Environment\.CreateWithOptionsAsync\(null, userDataFolder, null\)' -or
    $webViewLifecycle -notmatch 'EnsureCoreWebView2Async\(environment\)' -or
    $webViewLifecycle -match 'WEBVIEW2_USER_DATA_FOLDER|Environment\.SetEnvironmentVariable' -or
    $webViewService -notmatch 'Func<bool> _shouldEnableDevTools' -or
    $webViewLifecycle -notmatch 'coreWebView\.Settings\.AreDevToolsEnabled = ShouldEnableDevTools\(\)' -or
    ($webViewService + $webViewLifecycle + $webViewSession + $webViewProfile) -match 'App\.Configuration') {
    throw 'WebViewService must use an explicit CoreWebView2Environment user-data folder and injected DevTools policy instead of process-wide environment variables or global configuration.'
}

if ($webViewProfile -notmatch 'ProfileIdentityFileName' -or
    $webViewProfile -notmatch 'MigrateLegacyUserDataFolderIfNeededAsync' -or
    $webViewProfile -notmatch 'WriteProfileIdentityMarkerAsync' -or
    $webViewProfile -notmatch 'TryReadProfileIdentityMarkerAsync' -or
    $webViewProfile -notmatch 'GatewayUrlIdentity\.CreateProfileIdentityHash' -or
    $webViewProfile -notmatch 'GatewayUrlIdentity\.CreateProfileIdentityMarker' -or
    $webViewProfile -notmatch 'GatewayUrlIdentity\.ProfileIdentityMarkerMatches' -or
    $webViewProfile -notmatch 'EnumerateLegacyProfileFolders' -or
    $webViewProfile -notmatch 'Directory\.Move\(legacyFolder, profileFolder\)' -or
    $webViewProfile -notmatch 'Skipped legacy WebView2 profile migration') {
    throw 'WebView2 profile folders must use stable hashed Gateway URL identity and marker-aware legacy migration.'
}

if ($webViewService -notmatch 'public bool Reload\(\)' -or
    $webViewService -match 'public void Reload\(\)' -or
    $webViewService -notmatch 'StringResources\.WebViewReloadNotInitialized' -or
    $webViewService -notmatch 'return false' -or
    $webViewService -notmatch 'return true' -or
    $webViewService -notmatch 'StringResources\.WebViewReloadNotReady' -or
    $webViewService -notmatch 'StringResources\.WebViewRetryNotReady') {
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
$controlUiProbeUriFactory = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Services/ControlUiProbeUriFactory.cs') -Raw
$gatewayHttpStatusClassifier = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Services/GatewayHttpStatusClassifier.cs') -Raw
$gatewayDiagnosticProbeMapper = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Services/GatewayDiagnosticProbeMapper.cs') -Raw
$gatewayDiagnosticProbe = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Services/GatewayDiagnosticProbe.cs') -Raw
$heartbeatProbeResolver = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Services/HeartbeatProbeResolver.cs') -Raw
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

if ($controlUiProbeUriFactory -notmatch '__openclaw__/a2ui/' -or
    $controlUiProbeUriFactory -notmatch 'TryCreateConfigUri' -or
    $controlUiProbeUriFactory -notmatch 'TryCreateProbeKey' -or
    $controlUiProbeUriFactory -notmatch 'EndsWith\(\$"/\{ControlUiConfigPath\}"' -or
    $latencyService -match 'control-ui-config\.json' -or
    $latencyService -notmatch 'ControlUiProbeUriFactory\.TryCreateConfigUri' -or
    $latencyService -notmatch 'GatewayHttpStatusClassifier\.ClassifyResponseAsync' -or
    $latencyService -match 'ControlUiLatencySnapshot\.Success\([\s\S]*HTTP \{\(int\)response\.StatusCode\}') {
    throw 'Control UI latency probes must use the shared idempotent Gateway A2UI probe URI and classify HTTP status before publishing success.'
}

if ($gatewayHttpStatusClassifier -notmatch 'TryDetectCloudflareErrorCode' -or
    $gatewayHttpStatusClassifier -notmatch 'ClassifyResponseAsync' -or
    $gatewayHttpStatusClassifier -notmatch 'cf-error-type' -or
    $gatewayHttpStatusClassifier -notmatch 'cf-error-code' -or
    $gatewayHttpStatusClassifier -notmatch 'ReadBodySnippetWithTimeoutAsync' -or
    $gatewayHttpStatusClassifier -notmatch 'ReadBodySnippetAsync' -or
    $gatewayHttpStatusClassifier -notmatch 'Cloudflare error 1033' -or
    $gatewayHttpStatusClassifier -notmatch 'GatewayHttpStatusKind\.Redirected,[\s\S]*false') {
    throw 'Gateway HTTP status classification must detect Cloudflare error 1033 from headers/body snippets and must not treat redirects as reachable.'
}

if ($gatewayDiagnosticProbeMapper -notmatch 'GatewayDiagnosticProbeSeverity' -or
    $gatewayDiagnosticProbeMapper -notmatch 'GatewayHttpStatusKind\.Reachable => GatewayDiagnosticProbeSeverity\.Pass' -or
    $gatewayDiagnosticProbeMapper -notmatch 'GatewayHttpStatusKind\.Redirected or[\s\S]*GatewayHttpStatusKind\.MissingPath or[\s\S]*GatewayDiagnosticProbeSeverity\.Failure' -or
    $gatewayDiagnosticProbeMapper -notmatch '_ => GatewayDiagnosticProbeSeverity\.Warning') {
    throw 'Gateway diagnostics status severity must be mapped in Core and covered by the Core harness.'
}

if ($gatewayDiagnosticProbe -notmatch 'public sealed class GatewayDiagnosticProbe' -or
    $gatewayDiagnosticProbe -notmatch 'public GatewayDiagnosticProbe\(HttpMessageHandler messageHandler\)' -or
    $gatewayDiagnosticProbe -notmatch 'ControlUiProbeUriFactory\.TryCreateConfigUri\(gatewayUrl\)' -or
    $gatewayDiagnosticProbe -notmatch 'GatewayHttpStatusClassifier\.ClassifyResponseAsync' -or
    $gatewayDiagnosticProbe -notmatch 'GatewayDiagnosticProbeMapper\.Map' -or
    $gatewayDiagnosticProbe -notmatch 'GatewayDiagnosticProbeSeverity\.Pass && isNonLocalHttp' -or
    $gatewayDiagnosticProbe -notmatch 'GatewayDiagnosticProbeErrorKind\.InvalidUrl' -or
    $gatewayDiagnosticProbe -notmatch 'GatewayDiagnosticProbeErrorKind\.Timeout' -or
    $gatewayDiagnosticProbe -notmatch 'GatewayDiagnosticProbeErrorKind\.Unreachable') {
    throw 'Gateway diagnostic probing must live in Core, be injectable for tests, use shared A2UI/classifier mapping, and downgrade reachable non-local HTTP to warning.'
}

if ($heartbeatProbeResolver -notmatch 'public static class HeartbeatProbeResolver' -or
    $heartbeatProbeResolver -notmatch 'HeartbeatProbeStatus\.Failure when hostedSessionResult\.Status == HeartbeatProbeStatus\.Connecting' -or
    $heartbeatProbeResolver -notmatch 'HeartbeatProbeStatus\.SessionBlocked' -or
    $heartbeat -notmatch 'HeartbeatProbeResolver\.Resolve\(hostedSessionResult, transportResult\)') {
    throw 'Heartbeat hosted-session and transport observations must be merged through the Core resolver with explicit precedence tests.'
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
$gatewayHeartbeatProbeMapper = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Services/GatewayHeartbeatProbeMapper.cs') -Raw
if ($gatewayHeartbeatTransport -notmatch 'ControlUiProbeUriFactory\.TryCreateConfigUri' -or
    $gatewayHeartbeatTransport -notmatch 'GatewayHttpStatusClassifier\.ClassifyResponseAsync' -or
    $gatewayHeartbeatTransport -notmatch 'GatewayHeartbeatProbeMapper\.Map' -or
    $gatewayHeartbeatTransport -notmatch 'AllowAutoRedirect = false' -or
    $gatewayHeartbeatTransport -match 'statusCode switch' -or
    $latencyService -notmatch 'AllowAutoRedirect = false' -or
    $heartbeat -notmatch 'HeartbeatProbeResolver\.Resolve\(hostedSessionResult, transportResult\)' -or
    $gatewayHeartbeatProbeMapper -match 'GatewayHttpStatusKind\.Redirected' -or
    $gatewayHeartbeatProbeMapper -notmatch 'GatewayHttpStatusKind\.AccessRequired or[\s\S]*=> HeartbeatProbeResult\.SessionBlocked') {
    throw 'Gateway heartbeat and latency transports must preserve raw redirects and classify Cloudflare/proxy, missing path, rejected probe, redirect, and access-required responses consistently.'
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

if ($statusInspectorParsing -notmatch 'ParseControlUiSnapshot\(string json(?:, bool allowStringEnvelope = false)?\)' -or
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

if ($webViewSession -notmatch 'await DeleteUserDataFolderForEnvironmentAsync\(environmentName, gatewayUrl, _logger\)' -or
    $webViewProfile -notmatch 'await Task\.Run\(\(\) => Directory\.Delete\(folder, recursive: true\)' -or
    $webViewProfile -notmatch 'await Task\.Delay\(DeleteProfileRetryDelay \* attempt, cancellationToken\)') {
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

$bridgeScriptInstallPrelude = [regex]::Match(
    $hostedBridgeMain,
    'var scriptId = await coreWebView\.AddScriptToExecuteOnDocumentCreatedAsync[\s\S]*?if \(_isDisposed').Value
if ($bridgeScriptInstallPrelude -match '_documentCreatedScriptId = scriptId') {
    throw 'HostedUiBridge must assign _documentCreatedScriptId only after stale initialization checks pass.'
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

$stopInjectionScript = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewCommands.StopInjection.js') -Raw
if ($stopInjectionScript -match 'querySelectorAll\(''textarea, input\[type="text"\], input:not\(\[type\]\)''' -or
    $stopInjectionScript -match 'form\.submit\(\)' -or
    $stopInjectionScript -match 'dispatchEvent\(submitEvent\)' -or
    $stopInjectionScript -notmatch 'findChatComposer' -or
    $stopInjectionScript -notmatch 'requestSubmit') {
    throw 'Stop command fallback must target a known chat composer and must not submit arbitrary first-page inputs or bypass hosted UI validation.'
}

$abortRunScript = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewCommands.AbortRun.js') -Raw
if ($abortRunScript -match 'querySelectorAll\(''button, \[role="button"\], \[aria-label\], \[title\]''' -or
    $abortRunScript -notmatch 'findChatActionSurface' -or
    $abortRunScript -notmatch 'chat\.abort') {
    throw 'Abort fallback must target a known chat/run action surface and prefer the hosted chat.abort API.'
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
    $mainViewModelLifecycle -notmatch 'InitializeAsync\(webView, environmentName, gatewayUrl, cancellationToken\)' -or
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
    $settingsDialogActions -notmatch 'button\?\.Tag is not EnvironmentConfig environment' -or
    $settingsDialogActions -notmatch 'MainViewModel\.ClearSessionForEnvironmentAsync\(environment\)' -or
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
    $settingsViewModel -notmatch '_didEditEnableDevTools' -or
    $settingsViewModel -notmatch 'private void ApplyChangedShellSettings\(AppSettings settings\)' -or
    $settingsViewModel -notmatch 'if \(_didEditAlwaysOnTop\)[\s\S]*settings\.AlwaysOnTop = AlwaysOnTop' -or
    $settingsViewModel -notmatch 'if \(_didEditEnableGlobalHotkey\)[\s\S]*settings\.EnableGlobalHotkey = EnableGlobalHotkey' -or
    $settingsViewModel -notmatch 'if \(_didEditGlobalHotkey\)[\s\S]*settings\.GlobalHotkey = NormalizeHotkey\(GlobalHotkey\)' -or
    $settingsViewModel -notmatch 'if \(_didEditAllowMultipleInstances\)[\s\S]*settings\.AllowMultipleInstances = AllowMultipleInstances' -or
    $settingsViewModel -notmatch 'if \(_didEditEnableDevTools\)[\s\S]*settings\.Diagnostics\.EnableDevTools = EnableDevTools') {
    throw 'SettingsViewModel must merge only fields edited in the open Settings window so stale snapshots cannot overwrite live shell changes.'
}

foreach ($property in @(
    'SelectedLanguage',
    'EnableDevLog',
    'EnableDevTools',
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
    $settingsPersistenceAdapter -notmatch 'public SettingsPersistenceSaveResult Save\(AppSettings settings\)' -or
    $settingsPersistenceAdapter -match 'void Save\(\)' -or
    $settingsViewModel -notmatch 'var candidateSettings = currentSettings\.Clone\(\)' -or
    $settingsViewModel -notmatch 'var saveResult = _settingsPersistence\.Save\(candidateSettings\)' -or
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
    $configurationService -notmatch 'RetainDeferredSaveAfterFailure' -or
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

if ($configurationService -notmatch 'NormalizeEnvironments' -or
    $configurationService -notmatch 'GatewayUrlIdentity\.IsSupportedGatewayUrl' -or
    $configurationService -notmatch 'uniqueName = \$"\{name\} \(\{suffix\+\+\}\)"' -or
    $configurationService -notmatch 'settings\.SelectedEnvironmentName = defaultEnvironment\.Name' -or
    $configurationService -notmatch 'environment\.IsDefault = shouldBeDefault' -or
    $configurationService -notmatch 'EnvironmentConfig\.PlaceholderGatewayUrl') {
    throw 'ConfigurationService must normalize environment entries, selected environment, and default ownership before saving settings.'
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

$windowBoundsUtilities = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Helpers/WindowBoundsUtilities.cs') -Raw
$minimumSettingsWindowWidthMatch = [regex]::Match(
    $windowBoundsUtilities,
    'MinimumPersistedSettingsWindowWidth\s*=\s*(?<width>\d+)')
if (-not $minimumSettingsWindowWidthMatch.Success -or
    [int]$minimumSettingsWindowWidthMatch.Groups['width'].Value -lt 428) {
    throw 'SettingsDialog minimum persisted width must fit the fixed 160px navigation pane, 48px content padding, and 220px controls.'
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
if ($latencyService -notmatch 'string ProbeKey' -or
    $latencyService -notmatch 'ControlUiProbeUriFactory\.TryCreateProbeKey' -or
    $viewModelIndicators -notmatch 'IsLatencySnapshotForSelectedEnvironment' -or
    $viewModelIndicators -notmatch 'TryGetEnvironmentProbeKey' -or
    $viewModelIndicators -notmatch 'snapshot\.ProbeKey') {
    throw 'MainViewModel latency updates must reject stale snapshots from non-selected environment probe keys, including same-host different-port or base-path environments.'
}

if ($mainViewModelLifecycle -notmatch 'ApplyWebViewHostDetachedState\(\)' -or
    $mainViewModelLifecycle -notmatch '_latencyService\.Stop\(\)[\s\S]*_webViewService\.StopHeartbeat\(\)[\s\S]*ResetResourceProbeProjection\(\)' -or
    $mainViewModelLifecycle -notmatch 'ApplyWebViewHostDetachedState\(\)[\s\S]*ApplyConnectionState\(ConnectionState\.Loading\)[\s\S]*ResetTelemetry\(\)[\s\S]*ApplyRecoveryState\(RecoveryState\.Connecting\)' -or
    $viewModelHeartbeat -notmatch 'ResetHeartbeatProjection\(\)[\s\S]*HeartbeatSummary = StringResources\.HeartbeatWait[\s\S]*ResetHeartbeatIndicatorsToWarning\(\)' -or
    $viewModelIndicators -notmatch 'ResetLatencyProjection\([^)]*\)[\s\S]*LatencySummaryText = DefaultLatencySummary[\s\S]*LatencySummaryBrush = NeutralBrush' -or
    $viewModelIndicators -notmatch 'snapshot\.State == ControlUiLatencyState\.Unknown') {
    throw 'Stopping probes or detaching WebView must reset visible heartbeat, latency, MODEL, access, work, and shell projections instead of leaving stale healthy status visible.'
}

$latencyHistory = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Services/LatencyHistory.cs') -Raw
if ($latencyHistory -match 'Latency history|Latest:|Avg:|PoP:' -or
    $latencyHistory -notmatch 'public void Clear\(\)' -or
    $viewModelIndicators -notmatch 'StringResources\.LatencyHistoryNoSamples' -or
    $viewModelIndicators -notmatch 'StringResources\.LatencyPoPFormat' -or
    $viewModelIndicators -notmatch '_latencyHistory\.Clear\(\)' -or
    $viewModelIndicators -notmatch '_lastKnownPoP = null') {
    throw 'Latency history tooltip text must be localized in the WinUI layer and environment/host resets must clear stale samples and Cloudflare PoP state.'
}

$presenter = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/StatusPresenter.cs') -Raw
if ($presenter -notmatch 'ControlUiLatencyState\.Failure' -or
    $presenter -notmatch 'StringResources\.LatencyError') {
    throw 'Latency presentation must show a distinct failure state instead of leaving ping at the default placeholder after probe failures.'
}

if ($presenter -notmatch 'ControlUiLatencyState\.Stale' -or
    $presenter -notmatch 'StringResources\.LatencyStale' -or
    $presenter -notmatch 'new StatusPresentation\(StringResources\.LatencyStale, brushes\.Warning\)') {
    throw 'Latency presentation must show stale failed probes as warning text instead of keeping an old healthy-looking ping.'
}

if ($presenter -cmatch '"AUTH (OK|LOGIN|PAIR|ORIGIN|WAIT)"|"LIVE"|"IDLE"' -or
    $presenter -notmatch 'StringResources\.AccessStatusOk' -or
    $presenter -notmatch 'StringResources\.AccessStatusLogin' -or
    $presenter -notmatch 'StringResources\.AccessStatusPair' -or
    $presenter -notmatch 'StringResources\.AccessStatusOrigin' -or
    $presenter -notmatch 'StringResources\.AccessStatusWait' -or
    $presenter -notmatch 'StringResources\.WorkStatusLive' -or
    $presenter -notmatch 'StringResources\.WorkStatusIdle') {
    throw 'StatusPresenter access/work status tokens must come from localized StringResources.'
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

if ($commands -match 'Diagnostic bundle exported to Desktop' -or
    $commands -notmatch 'ResolveDiagnosticBundleOutputDirectory' -or
    $commands -notmatch 'StringResources\.DiagnosticBundleExportedFormat' -or
    $commands -notmatch 'Environment\.SpecialFolder\.DesktopDirectory' -or
    $commands -notmatch 'Environment\.SpecialFolder\.MyDocuments' -or
    $commands -notmatch 'DiagnosticBundleExportFallbackDirectoryName') {
    throw 'Diagnostic bundle export must use a writable fallback output directory and a localized success summary that reports the real path.'
}

$diagnosticBundleService = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core/Services/DiagnosticBundleService.cs') -Raw
if ($diagnosticBundleService -notmatch 'RedactDiagnosticText' -or
    $diagnosticBundleService -notmatch 'KeyValueSecretPattern' -or
    $diagnosticBundleService -notmatch 'HeaderSecretPattern' -or
    $diagnosticBundleService -notmatch 'MaxBundledLogPayloadBytes' -or
    $diagnosticBundleService -notmatch 'MaxDiagnosticTextEntryBytes' -or
    $diagnosticBundleService -notmatch 'DiagnosticLogReadResult\(bool Succeeded, string\? Content, string Message, long ByteCount\)' -or
    $diagnosticBundleService -notmatch 'FileMode\.CreateNew' -or
    $diagnosticBundleService -notmatch 'Guid\.NewGuid\(\)\.ToString\("N"\)\[\.\.8\]' -or
    $diagnosticBundleService -match 'CreateEntryFromFile' -or
    $diagnosticBundleService -match 'FileMode\.Create, FileAccess\.Write') {
    throw 'Diagnostic bundle export must redact logs/summaries and create unique files without overwriting prior bundles.'
}

if ($commands -notmatch 'public WebViewService\.DevToolsOpenResult OpenDevTools\(\)' -or
    $commands -notmatch 'FormatDevToolsOpenResult' -or
    $commands -notmatch 'SettingsDevToolsUnavailable' -or
    $commands -notmatch 'SettingsDevToolsDisabled' -or
    $commands -notmatch 'SettingsDevToolsOpenFailedFormat' -or
    $settingsDialogActions -notmatch 'OpenDevTools\(\)' -or
    $settingsDialogActions -notmatch 'DevToolsOpenStatus\.Failed') {
    throw 'DevTools commands must surface unavailable, disabled, and failed open results to the main shell and Settings dialog.'
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

if ($commands -notmatch 'private void OnAsyncCommandFailed\(Exception ex\)[\s\S]*StringResources\.AsyncCommandFailedFormat[\s\S]*ErrorMessage = [\s\S]*IsErrorVisible = true') {
    throw 'Async command failures must surface a localized visible error instead of only writing to the log.'
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

    foreach ($heartbeatResource in @(
        'HeartbeatInvalidControlUiUrl',
        'HeartbeatRequestFailedFormat',
        'HeartbeatHostedSessionActive',
        'HeartbeatHostedSessionReconnecting',
        'WebViewInvalidUrlFormat',
        'WebViewNavigationNotReady',
        'WebViewReloadNotInitialized',
        'WebViewReloadNotReady',
        'WebViewRetryNotReady'
    )) {
        if ($resources -notmatch "name=`"$heartbeatResource`"") {
            throw "Missing localized heartbeat/WebView resource '$heartbeatResource': $resourceFile"
        }
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

if ($diagnosticService -notmatch 'SharedGatewayDiagnosticProbe\.ProbeAsync\(gatewayUrl, cancellationToken\)' -or
    $diagnosticService -notmatch 'GatewayDiagnosticProbeErrorKind' -or
    $diagnosticService -notmatch 'CreateNetworkDiagnosticResult' -or
    $diagnosticService -notmatch 'ProbeNetworkAsync\(\s*string\? gatewayUrl,\s*ControlUiProbeSnapshot\? snapshot = null,\s*CancellationToken cancellationToken = default\)' -or
    $diagnosticService -notmatch 'ProbeNetworkAsync\(gatewayUrl, snapshot, cancellationToken\)' -or
    $diagnosticService -notmatch 'AppendHostedControlUiStateDetail' -or
    $diagnosticService -notmatch 'StringResources\.DiagnosticHostedStateDetailFormat' -or
    $diagnosticService -match 'GetAsync\(gatewayUrl' -or
    $diagnosticService -match 'GatewayHttpStatusClassifier\.ClassifyResponseAsync' -or
    $diagnosticService -match 'ControlUiProbeUriFactory\.TryCreateConfigUri\(gatewayUrl\)' -or
    $diagnosticService -match 'ProbeNetworkAsync\(string\? gatewayUrl, ControlUiProbeSnapshot' -or
    $diagnosticService -notmatch 'GatewayHttpStatusKind\.MethodRejected =>[\s\S]*CreateNetworkDiagnosticResult' -or
    $diagnosticService -notmatch 'GatewayHttpStatusKind\.MissingPath =>[\s\S]*CreateNetworkDiagnosticResult') {
    throw 'DiagnosticService must consume the Core GatewayDiagnosticProbe and stay responsible only for localized diagnostic presentation.'
}

if ($diagnosticService -match 'return\s+DiagnosticResult\.Warn\(\s*StringResources\.DiagnosticNonLocalHttp' -or
    $diagnosticService -notmatch 'var nonLocalHttpDetail = GetNonLocalHttpWarningDetail\(probeResult\.IsNonLocalHttp\);' -or
    $diagnosticService -notmatch 'CreateNetworkDiagnosticResult\([\s\S]*nonLocalHttpDetail' -or
    $diagnosticService -notmatch 'AppendDiagnosticDetail') {
    throw 'Diagnostic network probes must append non-local HTTP warnings to the real A2UI probe result instead of returning before the probe.'
}

if ($diagnosticService -match 'ResolveNetworkDiagnosticSeverity' -or
    $diagnosticService -match 'GatewayDiagnosticProbeMapper\.Map') {
    throw 'Diagnostic network probes with non-local HTTP warnings must not report a PASS result even when the A2UI endpoint is reachable.'
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

$settingsSwitches = @(
    @{ Property = 'MinimizeToTray'; Resource = 'SettingsMinimizeToTray' },
    @{ Property = 'CloseToTray'; Resource = 'SettingsCloseToTray' },
    @{ Property = 'AllowMultipleInstances'; Resource = 'SettingsAllowMultipleInstances' },
    @{ Property = 'AlwaysOnTop'; Resource = 'SettingsAlwaysOnTop' },
    @{ Property = 'EnableGlobalHotkey'; Resource = 'SettingsEnableGlobalHotkey' },
    @{ Property = 'EnableDevLog'; Resource = 'SettingsEnableDevLog' },
    @{ Property = 'EditIsDefault'; Resource = 'SetAsDefault' }
)
foreach ($switch in $settingsSwitches) {
    if ($settingsXaml -notmatch "ToggleSwitch[\s\S]*IsOn=""\{x:Bind ViewModel\.$($switch.Property), Mode=TwoWay\}""[\s\S]*AutomationProperties\.Name=""\{x:Bind helpers:StringResources\.$($switch.Resource)\}""") {
        throw "Settings toggle switch must expose a localized automation name: $($switch.Property)"
    }
}

$mainWindowTheme = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.Theme.cs') -Raw
$mainWindowAlwaysOnTop = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.AlwaysOnTop.cs') -Raw
$buttonStyles = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Styles/ButtonStyles.xaml') -Raw
if ($mainWindowXaml -notmatch '<Button x:Name="LatencyBadge"[\s\S]*AutomationProperties\.Name="\{x:Bind helpers:StringResources\.LatencyBadgeAutomationName\}"[\s\S]*AutomationProperties\.HelpText="\{x:Bind ViewModel\.LatencyTooltipText, Mode=OneWay\}"[\s\S]*<Button\.Flyout>[\s\S]*Text="\{x:Bind ViewModel\.LatencyTooltipText, Mode=OneWay\}"') {
    throw 'Latency badge must be keyboard focusable and expose the latency history through automation help text plus a button flyout.'
}

if ($mainWindowXaml -notmatch '<ComboBox x:Name="EnvironmentSelector"[\s\S]*AutomationProperties\.Name="\{x:Bind helpers:StringResources\.SelectEnvironment\}"' -or
    $settingsXaml -notmatch '<ListView x:Name="NavList"[\s\S]*AutomationProperties\.Name="\{x:Bind helpers:StringResources\.SettingsNavigationAutomationName\}"' -or
    $settingsXaml -notmatch '<ComboBox x:Name="LanguageComboBox"[\s\S]*AutomationProperties\.Name="\{x:Bind helpers:StringResources\.SettingsNavLanguage\}"' -or
    $settingsXaml -notmatch '<ListView x:Name="EnvironmentList"[\s\S]*AutomationProperties\.Name="\{x:Bind helpers:StringResources\.SettingsEnvironmentsTitle\}"' -or
    $settingsXaml -notmatch '<ListView x:Name="SessionEnvironmentList"[\s\S]*AutomationProperties\.Name="\{x:Bind helpers:StringResources\.SettingsSessionsTitle\}"') {
    throw 'Focusable selector and list controls must expose localized automation names.'
}

if ($mainWindowXaml -notmatch '<ToggleButton x:Name="PinButton"[\s\S]*Style="\{StaticResource SubtleToggleButtonStyle\}"' -or
    $mainWindowAlwaysOnTop -notmatch 'PinButton\.IsChecked = _isAlwaysOnTop') {
    throw 'The always-on-top pin affordance must expose toggle state instead of a stateless button.'
}

if ($mainWindowXaml -match 'Foreground="\{StaticResource SuccessBrush\}"') {
    throw 'Top status text must use theme resources instead of fixed success brushes.'
}

if ($mainWindowXaml -notmatch '<ToggleButton x:Name="SystemThemeButton"' -or
    $mainWindowXaml -notmatch '<ToggleButton x:Name="LightThemeButton"' -or
    $mainWindowXaml -notmatch '<ToggleButton x:Name="DarkThemeButton"' -or
    $mainWindowXaml -notmatch 'x:Name="ThemeSwitcherContainer"[\s\S]*AutomationProperties\.Name="\{x:Bind helpers:StringResources\.ThemeSelectorAutomationName\}"' -or
    $buttonStyles -notmatch 'x:Key="ThemeSegmentToggleButtonStyle" TargetType="ToggleButton"' -or
    $mainWindowTheme -notmatch 'IEnumerable<ToggleButton>' -or
    $mainWindowTheme -notmatch 'button\.IsChecked = isSelected' -or
    $mainWindowTheme -notmatch 'AccentFillColorDefaultBrush' -or
    $mainWindowTheme -match 'Windows\.UI\.Color\.FromArgb\(255, 230, 240, 255\)|Windows\.UI\.Color\.FromArgb\(255, 37, 99, 235\)') {
    throw 'Theme selector must expose checked ToggleButton state and use theme resources instead of fixed selected colors.'
}

$logViewerXaml = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Views/LogViewerDialog.xaml') -Raw
foreach ($button in @(
    @{ File = 'MainWindow.xaml'; Xaml = $mainWindowXaml; Name = 'PinButton'; Resource = 'SettingsAlwaysOnTop' },
    @{ File = 'MainWindow.xaml'; Xaml = $mainWindowXaml; Name = 'ReloadButton'; Resource = 'Reload' },
    @{ File = 'MainWindow.xaml'; Xaml = $mainWindowXaml; Name = 'StopButton'; Resource = 'Stop' },
    @{ File = 'MainWindow.xaml'; Xaml = $mainWindowXaml; Name = 'SystemThemeButton'; Resource = 'ThemeSystem' },
    @{ File = 'MainWindow.xaml'; Xaml = $mainWindowXaml; Name = 'LightThemeButton'; Resource = 'ThemeLight' },
    @{ File = 'MainWindow.xaml'; Xaml = $mainWindowXaml; Name = 'DarkThemeButton'; Resource = 'ThemeDark' },
    @{ File = 'MainWindow.xaml'; Xaml = $mainWindowXaml; Name = 'SettingsButton'; Resource = 'Settings' },
    @{ File = 'MainWindow.xaml'; Xaml = $mainWindowXaml; Name = 'AboutButton'; Resource = 'AboutTitle' },
    @{ File = 'SettingsDialog.xaml'; Xaml = $settingsXaml; Name = 'AddEnvironmentButton'; Resource = 'SettingsAddTooltip' },
    @{ File = 'SettingsDialog.xaml'; Xaml = $settingsXaml; Name = 'RemoveEnvironmentButton'; Resource = 'SettingsRemoveTooltip' },
    @{ File = 'LogViewerDialog.xaml'; Xaml = $logViewerXaml; Name = 'RefreshButton'; Resource = 'RefreshLogs' },
    @{ File = 'LogViewerDialog.xaml'; Xaml = $logViewerXaml; Name = 'OpenFolderButton'; Resource = 'OpenLogFolder' }
)) {
    if ($button.Xaml -notmatch "x:Name=""$($button.Name)""[\s\S]*AutomationProperties\.Name=""\{x:Bind helpers:StringResources\.$($button.Resource)\}""") {
        throw "Icon-only button must expose a localized automation name: $($button.File)#$($button.Name)"
    }
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
