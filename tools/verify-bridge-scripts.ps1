Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$servicesRoot = Join-Path $repoRoot 'src/OpenClaw/Services'
$hostMessagingPath = Join-Path $servicesRoot 'HostedUiBridge.HostMessaging.js'
$mutationFilterPath = Join-Path $servicesRoot 'HostedUiBridge.MutationFilter.js'
$resolverPath = Join-Path $servicesRoot 'HostedUiBridge.ModelResolver.js'
$commandDispatchPath = Join-Path $servicesRoot 'HostedUiBridge.CommandDispatch.js'
$bridgeScriptPath = Join-Path $servicesRoot 'HostedUiBridge.Script.js'

$bridgeScript = Get-Content -LiteralPath $bridgeScriptPath -Raw
if ($bridgeScript -notmatch 'nextPollAt' -or $bridgeScript -notmatch 'scheduleNextPoll') {
    throw 'Bridge polling must use drift-aware scheduling.'
}
Write-Host 'PASS: polling uses nextPollAt drift correction'

if ($bridgeScript -notmatch 'getPollInterval' -or $bridgeScript -notmatch 'getPollInterval\(snapshot\)') {
    throw 'Bridge polling interval must be snapshot-driven.'
}
Write-Host 'PASS: polling interval is snapshot-driven'

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
    Write-Host 'SKIP: node is not available; bridge script verification skipped.'
    exit 0
}

try {
    & $nodeCommand --version | Out-Null
} catch {
    Write-Host "SKIP: node is not executable; bridge script verification skipped. $($_.Exception.Message)"
    exit 0
}

$runner = @(
    Get-Content -LiteralPath $hostMessagingPath -Raw
    Get-Content -LiteralPath $mutationFilterPath -Raw
    Get-Content -LiteralPath $resolverPath -Raw
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
assertTrue('command dispatch falls back to CustomEvent when method missing',
  handled === true &&
  dispatchedEvents.includes('openclaw:host-command') &&
  dispatchedEvents.includes('openclaw:refresh_session'));

globalThis.window = {};
assertEqual('host messaging returns false without chrome.webview', openClawHostMessaging.postHostMessage({ kind: 'test' }), false);

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

$tempFile = Join-Path ([System.IO.Path]::GetTempPath()) ('openclaw-bridge-scripts-' + [System.Guid]::NewGuid() + '.js')
try {
    Set-Content -LiteralPath $tempFile -Value $runner -Encoding UTF8
    & $nodeCommand $tempFile
} finally {
    Remove-Item -LiteralPath $tempFile -ErrorAction SilentlyContinue
}
