Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$resolverPath = Join-Path $repoRoot 'src/OpenClaw/Services/HostedUiBridge.ModelResolver.js'

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

$resolver = Get-Content -LiteralPath $resolverPath -Raw
$runner = $resolver + @'

const cases = [
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

let failed = 0;
for (const item of cases) {
  const result = resolveOpenClawAppStateModel(item.states, item.sessionKey);
  if (!result || result.value !== item.expected) {
    console.error(`FAIL: ${item.name}: expected ${item.expected}, got ${result && result.value}`);
    failed += 1;
  } else {
    console.log(`PASS: ${item.name}`);
  }
}

process.exit(failed === 0 ? 0 : 1);
'@

$tempFile = Join-Path ([System.IO.Path]::GetTempPath()) ('openclaw-bridge-scripts-' + [System.Guid]::NewGuid() + '.js')
try {
    Set-Content -LiteralPath $tempFile -Value $runner -Encoding UTF8
    & $nodeCommand $tempFile
} finally {
    Remove-Item -LiteralPath $tempFile -ErrorAction SilentlyContinue
}
