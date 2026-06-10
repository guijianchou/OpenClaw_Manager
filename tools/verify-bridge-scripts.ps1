Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$servicesRoot = Join-Path $repoRoot 'src/OpenClaw/Services'
$hostMessagingPath = Join-Path $servicesRoot 'HostedUiBridge.HostMessaging.js'
$mutationFilterPath = Join-Path $servicesRoot 'HostedUiBridge.MutationFilter.js'
$resolverPath = Join-Path $servicesRoot 'HostedUiBridge.ModelResolver.js'
$domUtilitiesPath = Join-Path $servicesRoot 'HostedUiBridge.DomUtilities.js'
$modelDomFallbackPath = Join-Path $servicesRoot 'HostedUiBridge.ModelDomFallback.js'
$activityStatePath = Join-Path $servicesRoot 'HostedUiBridge.ActivityState.js'
$phaseClassifierPath = Join-Path $servicesRoot 'HostedUiBridge.PhaseClassifier.js'
$statusInspectionPath = Join-Path $servicesRoot 'HostedUiBridge.StatusInspection.js'
$commandDispatchPath = Join-Path $servicesRoot 'HostedUiBridge.CommandDispatch.js'
$bridgeScriptPath = Join-Path $servicesRoot 'HostedUiBridge.Script.js'

$bridgeScriptTemplate = Get-Content -LiteralPath $bridgeScriptPath -Raw
if ($bridgeScriptTemplate -notmatch 'nextPollAt' -or $bridgeScriptTemplate -notmatch 'scheduleNextPoll') {
    throw 'Bridge polling must use drift-aware scheduling.'
}
Write-Host 'PASS: polling uses nextPollAt drift correction'

if ($bridgeScriptTemplate -notmatch 'getPollInterval' -or $bridgeScriptTemplate -notmatch 'getPollInterval\(snapshot\)') {
    throw 'Bridge polling interval must be snapshot-driven.'
}
Write-Host 'PASS: polling interval is snapshot-driven'

if ($bridgeScriptTemplate -notmatch 'const postSessionReady' -or
    $bridgeScriptTemplate -notmatch "snapshot\.phase !== 'connected'" -or
    $bridgeScriptTemplate -notmatch 'reportSessionReady:\s*\(\) => \{[\s\S]*return postSessionReady\(\)') {
    throw 'Bridge session-ready replay must wait for connected shell state and be callable by native code.'
}
Write-Host 'PASS: session-ready replay is native-callable'

function Resolve-NodeCommand {
    if (-not [string]::IsNullOrWhiteSpace($env:OPENCLAW_NODE)) {
        return $env:OPENCLAW_NODE
    }

    $command = Get-Command node -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    return $null
}

$nodeCommand = Resolve-NodeCommand
if (-not $nodeCommand) {
    if ($env:OPENCLAW_ALLOW_NODE_SKIP -eq '1') {
        Write-Host 'SKIP: node is not available and OPENCLAW_ALLOW_NODE_SKIP=1; bridge script verification skipped.'
        exit 0
    }

    throw 'Node.js is required for bridge script verification. Set OPENCLAW_NODE to a Node executable, or set OPENCLAW_ALLOW_NODE_SKIP=1 only for an explicit local skip.'
}

try {
    & $nodeCommand --version | Out-Null
} catch {
    if ($env:OPENCLAW_ALLOW_NODE_SKIP -eq '1') {
        Write-Host "SKIP: node is not executable and OPENCLAW_ALLOW_NODE_SKIP=1; bridge script verification skipped. $($_.Exception.Message)"
        exit 0
    }

    throw "Node.js is required for bridge script verification, but '$nodeCommand' is not executable. Set OPENCLAW_NODE to a working Node executable, or set OPENCLAW_ALLOW_NODE_SKIP=1 only for an explicit local skip. $($_.Exception.Message)"
}

function Invoke-NodeRunner {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $tempFile = Join-Path ([System.IO.Path]::GetTempPath()) ('openclaw-' + $Name + '-' + [System.Guid]::NewGuid() + '.mjs')
    try {
        Set-Content -LiteralPath $tempFile -Value $Source -Encoding UTF8
        & $nodeCommand $tempFile
        if ($LASTEXITCODE -ne 0) {
            throw "Bridge script verification failed in $Name with exit code $LASTEXITCODE."
        }
    } finally {
        Remove-Item -LiteralPath $tempFile -ErrorAction SilentlyContinue
    }
}

$bridgeStrings = @{
    bridgeGatewayUiLoaded = 'loaded'
    bridgePageLoading = 'loading'
    bridgeConnectedSummary = 'connected summary'
} | ConvertTo-Json -Compress

$assembledBridgeScript = $bridgeScriptTemplate
$scriptReplacements = [ordered]@{
    '__OPENCLAW_BRIDGE_STRINGS_JSON__' = $bridgeStrings
    '__OPENCLAW_OWNER_TOKEN_JSON__' = '"owner-token-test"'
    '__OPENCLAW_HOST_MESSAGING_SCRIPT__' = Get-Content -LiteralPath $hostMessagingPath -Raw
    '__OPENCLAW_MUTATION_FILTER_SCRIPT__' = Get-Content -LiteralPath $mutationFilterPath -Raw
    '__OPENCLAW_MODEL_RESOLVER_SCRIPT__' = Get-Content -LiteralPath $resolverPath -Raw
    '__OPENCLAW_DOM_UTILITIES_SCRIPT__' = Get-Content -LiteralPath $domUtilitiesPath -Raw
    '__OPENCLAW_MODEL_DOM_FALLBACK_SCRIPT__' = Get-Content -LiteralPath $modelDomFallbackPath -Raw
    '__OPENCLAW_ACTIVITY_STATE_SCRIPT__' = Get-Content -LiteralPath $activityStatePath -Raw
    '__OPENCLAW_PHASE_CLASSIFIER_SCRIPT__' = Get-Content -LiteralPath $phaseClassifierPath -Raw
    '__OPENCLAW_STATUS_INSPECTION_SCRIPT__' = Get-Content -LiteralPath $statusInspectionPath -Raw
    '__OPENCLAW_COMMAND_DISPATCH_SCRIPT__' = Get-Content -LiteralPath $commandDispatchPath -Raw
}

foreach ($entry in $scriptReplacements.GetEnumerator()) {
    $assembledBridgeScript = $assembledBridgeScript.Replace($entry.Key, $entry.Value)
}

$assembledBridgeScriptLiteral = $assembledBridgeScript | ConvertTo-Json -Compress

$runner = @(
    Get-Content -LiteralPath $hostMessagingPath -Raw
    Get-Content -LiteralPath $mutationFilterPath -Raw
    Get-Content -LiteralPath $resolverPath -Raw
    Get-Content -LiteralPath $domUtilitiesPath -Raw
    Get-Content -LiteralPath $modelDomFallbackPath -Raw
    Get-Content -LiteralPath $activityStatePath -Raw
    Get-Content -LiteralPath $phaseClassifierPath -Raw
    Get-Content -LiteralPath $statusInspectionPath -Raw
    Get-Content -LiteralPath $commandDispatchPath -Raw
) -join "`n"

$runner += @'

let failed = 0;

const assertEqual = (name, actual, expected) => {
  if (actual !== expected) {
    console.error(`FAIL: ${name}: expected ${expected}, got ${actual}`);
    failed += 1;
    return;
  }

  console.log(`PASS: ${name}`);
};

const assertTrue = (name, condition, detail = '') => {
  if (!condition) {
    console.error(`FAIL: ${name}${detail ? `: ${detail}` : ''}`);
    failed += 1;
    return;
  }

  console.log(`PASS: ${name}`);
};

const modelCases = [
  {
    name: 'MODEL default model',
    states: [{
      sessionsResult: {
        defaults: { model: 'gpt-5.5', modelProvider: 'openai' },
        sessions: []
      }
    }],
    sessionKey: 's1',
    expected: 'openai/gpt-5.5'
  },
  {
    name: 'MODEL null override session precedence',
    states: [{
      sessionKey: 's1',
      chatModelOverrides: { s1: null },
      sessionsResult: {
        defaults: { model: 'gpt-5.4', modelProvider: 'openai' },
        sessions: [{ key: 's1', model: 'claude-sonnet-4.5', modelProvider: 'anthropic' }]
      }
    }],
    sessionKey: 's1',
    expected: 'anthropic/claude-sonnet-4.5'
  },
  {
    name: 'MODEL object override',
    states: [{
      chatModelOverrides: {
        s1: {
          model: { id: 'qwen3-coder' },
          provider: { id: 'dashscope' }
        }
      },
      sessionsResult: {
        defaults: { model: 'gpt-5.4', modelProvider: 'openai' },
        sessions: []
      }
    }],
    sessionKey: 's1',
    expected: 'dashscope/qwen3-coder'
  }
];

for (const item of modelCases) {
  const result = resolveOpenClawAppStateModel(item.states, item.sessionKey);
  assertEqual(item.name, result && result.value, item.expected);
}

assertEqual('DOM utilities compact whitespace', openClawDomUtilities.compactText('  a \n\t b  '), 'a b');

const fakeMutationFilter = { isStatusProbeExcludedElement: () => false };
const modelDomReader = openClawModelDomFallback.createReader({
  dom: openClawDomUtilities,
  mutationFilter: fakeMutationFilter,
  modelResolver: openClawModelResolver
});
assertEqual('MODEL DOM fallback sanitizes control labels',
  modelDomReader.sanitizeModelLabel('Current model: openai/gpt-5.5'),
  'openai/gpt-5.5');

globalThis.window = {};
globalThis.document = { querySelector: () => null, querySelectorAll: () => [] };
const activityTracker = openClawActivityState.createActivityTracker({
  dom: openClawDomUtilities,
  mutationFilter: fakeMutationFilter
});
const idleSnapshot = activityTracker.applyBusyStaleness({ phase: 'connected', workState: 'idle', isBusy: false });
assertTrue('activity state marks idle snapshot non-stale',
  idleSnapshot.isBusyStale === false && idleSnapshot.busyStaleSeconds === 0);

const phaseStrings = {
  bridgeGatewayUiLoaded: 'loaded',
  bridgePageLoading: 'loading',
  bridgeTokenMissingSummary: 'token missing summary',
  bridgeTokenMissingDetail: 'token missing detail',
  bridgeTokenMismatchSummary: 'token mismatch summary',
  bridgeTokenMismatchDetail: 'token mismatch detail',
  bridgeDeviceTokenMismatchSummary: 'device mismatch summary',
  bridgeDeviceTokenMismatchDetail: 'device mismatch detail',
  bridgeOriginRejectedSummary: 'origin summary',
  bridgeOriginRejectedDetail: 'origin detail',
  bridgeTrustedProxyLoopbackSummary: 'loopback summary',
  bridgeTrustedProxyLoopbackDetail: 'loopback detail',
  bridgeMixedAuthSummary: 'mixed summary',
  bridgeMixedAuthDetail: 'mixed detail',
  bridgeTrustedProxyHeaderSummary: 'header summary',
  bridgeTrustedProxyHeaderDetail: 'header detail',
  bridgeTrustedProxyOriginSummary: 'proxy origin summary',
  bridgeTrustedProxyOriginDetail: 'proxy origin detail',
  bridgeRateLimitedSummary: 'rate summary',
  bridgeRateLimitedDetail: 'rate detail',
  bridgeInsecureHttpSummary: 'http summary',
  bridgeInsecureHttpDetail: 'http detail',
  bridgePairingSummary: 'pairing summary',
  bridgePairingDetail: 'pairing detail',
  bridgeAuthRequiredSummary: 'auth summary',
  bridgeAuthRequiredDetail: 'auth detail',
  bridgeGatewaySessionNotConnectedSummary: 'gateway summary',
  bridgeGatewaySessionNotConnectedDetail: 'gateway detail',
  bridgeConnectingSummary: 'connecting summary',
  bridgeConnectingDetail: 'connecting detail',
  bridgeConnectedSummary: 'connected summary'
};
const authPhase = openClawPhaseClassifier.classify({
  documentReadyState: 'complete',
  hasBody: true,
  text: 'auth_token_missing',
  lowerUrl: 'https://example.test/',
  strings: phaseStrings,
  shellDetected: false
});
assertTrue('phase classifier maps token missing to auth_required',
  authPhase.phase === 'auth_required' && authPhase.summary === 'token missing summary');

globalThis.HTMLSelectElement = class HTMLSelectElement {};
globalThis.HTMLInputElement = class HTMLInputElement {};
globalThis.HTMLTextAreaElement = class HTMLTextAreaElement {};
const appElement = {
  connected: true,
  tab: 'chat',
  chatMessages: [],
  sessionsResult: {
    defaults: { model: 'gpt-5.5', modelProvider: 'openai' },
    sessions: []
  }
};
globalThis.window = {
  location: { href: 'https://example.test/?session=s1' }
};
globalThis.document = {
  readyState: 'complete',
  body: {},
  activeElement: null,
  querySelector: (selector) => selector === 'openclaw-app' ? appElement : null,
  querySelectorAll: () => []
};
const statusInspector = openClawStatusInspection.createInspector({
  strings: phaseStrings,
  mutationFilter: fakeMutationFilter,
  modelResolver: openClawModelResolver,
  statusKind: 'openclaw-control-ui-status'
});
const statusSnapshot = statusInspector.inspectControlUi();
assertTrue('status inspection composes split bridge modules',
  statusSnapshot.phase === 'connected' &&
  statusSnapshot.currentModel === 'openai/gpt-5.5' &&
  statusSnapshot.currentModelSource === 'app-state:default');

appElement.chatLoading = false;
appElement.configSaving = true;
appElement.cronBusy = true;
const shellBusySnapshot = statusInspector.inspectControlUi();
assertTrue('status inspection keeps settings/cron busy out of stale-chat recovery',
  shellBusySnapshot.isBusy === true &&
  shellBusySnapshot.isBusyStaleCandidate === false &&
  shellBusySnapshot.isBusyStale === false);

appElement.configSaving = false;
appElement.cronBusy = false;
appElement.chatLoading = true;
appElement.chatMessages = [{ id: 'm1', text: 'hello' }];
const chatBusySnapshot = statusInspector.inspectControlUi();
assertTrue('status inspection marks chat busy as stale-busy candidate',
  chatBusySnapshot.isBusy === true &&
  chatBusySnapshot.isBusyStaleCandidate === true &&
  chatBusySnapshot.activitySignature.includes('hello'));
appElement.chatLoading = false;

globalThis.CustomEvent = class CustomEvent {
  constructor(type, options) {
    this.type = type;
    this.detail = options?.detail;
  }
};

let invokedPayload = null;
let postedSnapshot = null;
let readySnapshot = null;
globalThis.window = {
  chat: {
    refreshSession: async (payload) => {
      invokedPayload = payload;
    }
  },
  dispatchEvent: () => false
};
globalThis.document = { dispatchEvent: () => false };

let commandHandler = openClawCommandDispatch.createCommandHandler({
  inspectControlUi: () => ({ phase: 'connected', shellDetected: true }),
  postStatus: (snapshot) => { postedSnapshot = snapshot; },
  checkSessionReady: (snapshot) => { readySnapshot = snapshot; }
});

let handled = await commandHandler({ command: 'refresh_session', payload: { id: 42 } });
assertTrue('command dispatch returns handled true when bridge method exists',
  handled === true && invokedPayload?.id === 42 && postedSnapshot?.phase === 'connected' && readySnapshot?.shellDetected === true);

const dispatchedEvents = [];
globalThis.window = { dispatchEvent: (event) => { dispatchedEvents.push(event.type); return true; } };
globalThis.document = { dispatchEvent: (event) => { dispatchedEvents.push(event.type); return true; } };
commandHandler = openClawCommandDispatch.createCommandHandler({
  inspectControlUi: () => ({ phase: 'page_loaded', shellDetected: false }),
  postStatus: () => {},
  checkSessionReady: () => {}
});
handled = await commandHandler({ command: 'refresh_session', payload: { fallback: true } });
assertTrue('command dispatch dispatches CustomEvent but returns unhandled when method missing',
  handled === false &&
  dispatchedEvents.includes('openclaw:host-command') &&
  dispatchedEvents.includes('openclaw:refresh_session'));

dispatchedEvents.length = 0;
handled = await commandHandler({ command: 'future_unknown_command', payload: { fallback: true } });
assertTrue('command dispatch returns unhandled for unknown CustomEvent fallback',
  handled === false &&
  dispatchedEvents.includes('openclaw:host-command') &&
  dispatchedEvents.includes('openclaw:future_unknown_command'));

globalThis.window = {};
assertEqual('host messaging returns false without chrome.webview', openClawHostMessaging.postHostMessage({ kind: 'test' }), false);

let postedHostMessage = null;
globalThis.window = {
  chrome: {
    webview: {
      postMessage: (message) => {
        postedHostMessage = message;
      }
    }
  }
};
openClawHostMessaging.setOwnerToken('owner-token-test');
assertTrue('host messaging attaches native ownership tokens',
  openClawHostMessaging.postHostMessage({ kind: 'test' }) === true &&
  postedHostMessage?.nativeOwnerToken === 'owner-token-test' &&
  typeof postedHostMessage?.nativePageToken === 'string' &&
  postedHostMessage.nativePageToken.length > 0);

const excludedTarget = {
  nodeType: 1,
  parentElement: null,
  closest: () => ({})
};
const includedTarget = {
  nodeType: 1,
  parentElement: null,
  closest: () => null
};
assertTrue('mutation filter ignores settings/config/cron/sidebar mutations',
  openClawMutationFilter.isStatusRelevantMutation({ target: excludedTarget, type: 'childList' }) === false &&
  openClawMutationFilter.isStatusRelevantMutation({ target: includedTarget, type: 'attributes', attributeName: 'data-state' }) === true);

process.exit(failed === 0 ? 0 : 1);
'@

$composedRunner = @'
let failed = 0;

const assertTrue = (name, condition, detail = '') => {
  if (!condition) {
    console.error(`FAIL: ${name}${detail ? `: ${detail}` : ''}`);
    failed += 1;
    return;
  }

  console.log(`PASS: ${name}`);
};

const appElement = {
  connected: true,
  tab: 'chat',
  chatMessages: [],
  sessionsResult: {
    defaults: { model: 'gpt-5.5', modelProvider: 'openai' },
    sessions: []
  }
};
const bridgePostedMessages = [];
Object.defineProperty(globalThis, 'crypto', {
  value: { randomUUID: () => 'page-token-test' },
  configurable: true
});
globalThis.MutationObserver = class MutationObserver {
  constructor(callback) {
    this.callback = callback;
  }

  observe() {}
  disconnect() {}
};
globalThis.history = {
  pushState() {},
  replaceState() {}
};
globalThis.window = {
  chrome: {
    webview: {
      postMessage: (message) => {
        bridgePostedMessages.push(message);
      }
    }
  },
  location: { href: 'https://example.test/?session=s1' },
  setTimeout: () => 1,
  clearTimeout: () => {},
  addEventListener: () => {},
  dispatchEvent: () => false
};
globalThis.document = {
  readyState: 'complete',
  visibilityState: 'visible',
  documentElement: {},
  body: {},
  activeElement: null,
  addEventListener: () => {},
  querySelector: (selector) => selector === 'openclaw-app' ? appElement : null,
  querySelectorAll: () => []
};

// The full composed bridge script is executed by the PowerShell harness here.
'@

$composedRunner += "`ntry {`n"
$composedRunner += "  new Function($assembledBridgeScriptLiteral)();`n"
$composedRunner += "} catch (error) {`n"
$composedRunner += "  console.error('FAIL: composed bridge script threw during install');`n"
$composedRunner += "  console.error(error && error.stack || error);`n"
$composedRunner += "  failed += 1;`n"
$composedRunner += "}`n"

$composedRunner += @'

assertTrue('composed bridge exposes native host bridge',
  typeof window.__openClawHostBridge?.reportSessionReady === 'function',
  `window keys: ${Object.keys(window).join(',')}`);
const firstReadyReplay = window.__openClawHostBridge?.reportSessionReady();
const secondReadyReplay = window.__openClawHostBridge?.reportSessionReady();
const readyMessages = bridgePostedMessages.filter((message) => message.kind === 'openclaw-session-ready');
assertTrue('composed bridge allows native session-ready replay after an earlier ready post',
  firstReadyReplay === true &&
  secondReadyReplay === true &&
  readyMessages.length === 2 &&
  readyMessages.every((message) =>
    message.nativeOwnerToken === 'owner-token-test' &&
    message.nativePageToken === 'page-token-test'));

process.exit(failed === 0 ? 0 : 1);
'@

Invoke-NodeRunner -Source $runner -Name 'bridge-scripts'
Invoke-NodeRunner -Source $composedRunner -Name 'composed-bridge-script'
