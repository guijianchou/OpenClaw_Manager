using System.Net;
using System.Reflection;
using System.Text.Json;
using Jint;
using OpenClaw.Helpers;
using OpenClaw.Models;
using OpenClaw.Services;

internal static partial class Tests
{
    public static Task HostedUiBridgeReadsCurrentModelFromOpenClawModelSelect()
    {
        var source = ReadHostedBridgeScriptSource();

        Assert.Contains("data-chat-model-select", source, "Bridge should read OpenClaw Web UI's explicit model select before generic heuristics.");
        Assert.Contains("selectedModelOptionValue", source, "Bridge should consider the selected option value when the visible label is localized or default-only.");
        Assert.Contains("selectedModelTitle", source, "Bridge should consider the select title that OpenClaw uses for the displayed model label.");
        return Task.CompletedTask;
    }

    public static Task HostedUiBridgeExecutableScriptReadsCurrentModelFromSelect()
    {
        var result = InspectHostedBridgeScript("""
            const selectedOption = {
              value: 'gpt-4.1-mini',
              textContent: 'gpt-4.1-mini'
            };
            const modelSelect = new HTMLSelectElement();
            modelSelect.value = 'gpt-4.1-mini';
            modelSelect.selectedOptions = [selectedOption];
            modelSelect.textContent = 'gpt-4.1-mini';
            modelSelect.innerText = 'gpt-4.1-mini';
            modelSelect.getAttribute = (name) => name === 'title' ? 'gpt-4.1-mini' : '';
            modelSelect.getBoundingClientRect = () => ({ width: 220, height: 32, top: 12 });
            modelSelect.closest = () => null;
            document.__querySelector = (selector) =>
              selector === 'select[data-chat-model-select="true"], select[data-chat-model-select]' ? modelSelect : null;
            document.__querySelectorAll = (selector) => {
              if (selector === 'select[data-chat-model-select="true"], select[data-chat-model-select]') return [modelSelect];
              if (selector === 'select') return [modelSelect];
              return [];
            };
            """);

        Assert.Equal("gpt-4.1-mini", result.CurrentModel, "Executable bridge script should inspect the selected OpenClaw model value.");
        Assert.Equal("model-select", result.CurrentModelSource, "Executable bridge script should report the model-select source.");
        return Task.CompletedTask;
    }

    public static Task HostedUiBridgeReadsCurrentModelFromOpenClawAppState()
    {
        var modelResolverPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "HostedUiBridge.ModelResolver.js");
        var source = ReadHostedBridgeScriptSource();
        var modelResolverSource = File.ReadAllText(modelResolverPath);

        Assert.Contains("readOpenClawAppStateModel", source, "Bridge should read OpenClaw's app state before falling back to visible DOM controls.");
        Assert.Contains("resolveOpenClawAppStateModel(states, readCurrentSessionKey(states))", source, "Bridge app-state reads should delegate to the executable model resolver asset.");
        Assert.Contains("openclaw-app", source, "Bridge should locate the OpenClaw Lit app host.");
        Assert.Contains("chatModelOverrides", modelResolverSource, "Bridge should consider the local model override cache used by OpenClaw Web UI.");
        Assert.Contains("sessionsResult?.defaults", modelResolverSource, "Bridge should resolve the inherited default model when a session has no override.");
        Assert.Contains("modelOverride", modelResolverSource, "Bridge should read OpenClaw session modelOverride fields when no plain model field is present.");
        Assert.Contains("providerOverride", modelResolverSource, "Bridge should read OpenClaw session providerOverride fields when no plain modelProvider field is present.");
        Assert.Contains("searchParams.get('session')", source, "Bridge should fall back to the hosted chat session query string when app.sessionKey is absent.");
        Assert.Contains("overrides instanceof Map", modelResolverSource, "Bridge should support Map-backed chatModelOverrides from Lit app state.");
        Assert.Contains("value == null ? '' : String(value)", source, "Bridge text normalization should not throw when app-state message parts contain object payloads.");
        return Task.CompletedTask;
    }

    public static Task HostedUiBridgeExecutableScriptReadsCurrentModelFromAppState()
    {
        var result = InspectHostedBridgeScript("""
            const app = {
              connected: true,
              tab: 'chat',
              sessionKey: 'chat-1',
              chatLoading: false,
              sessionsResult: {
                defaults: { provider: 'openai', model: 'gpt-default' },
                sessions: [
                  { id: 'chat-1', providerOverride: 'anthropic', modelOverride: 'claude-3-5-sonnet' }
                ]
              },
              chatModelOverrides: new Map([
                ['chat-1', null]
              ])
            };
            document.__querySelector = (selector) => selector === 'openclaw-app' ? app : null;
            """);

        Assert.Equal("anthropic/claude-3-5-sonnet", result.CurrentModel, "Executable bridge script should combine app-state session provider/model fields.");
        Assert.Equal("app-state:session", result.CurrentModelSource, "Executable bridge script should report app-state session model source.");
        return Task.CompletedTask;
    }

    public static Task HostedUiBridgeUsesStructuredModelSourcePipeline()
    {
        var modelResolverPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "HostedUiBridge.ModelResolver.js");
        var source = ReadHostedBridgeScriptSource();
        var modelResolverSource = File.ReadAllText(modelResolverPath);

        Assert.Contains("MODEL_FIELD_KEYS", modelResolverSource, "Model field aliases should be centralized instead of repeated in ad hoc OR chains.");
        Assert.Contains("PROVIDER_FIELD_KEYS", modelResolverSource, "Provider field aliases should be centralized next to model aliases.");
        Assert.Contains("SESSION_KEY_PATHS", source, "Session-key discovery should be data-driven across root and nested app-state variants.");
        Assert.Contains("APP_STATE_PATHS", source, "OpenClaw app-state lookup should cover root and nested state containers without another patch per field move.");
        Assert.Contains("MODEL_SOURCE_READERS", source, "Current model detection should run through an explicit ordered source pipeline.");
        Assert.Contains("readFirstPath", source, "Nested app-state values should be resolved through one path reader instead of repeated optional chains.");
        Assert.Contains("return { value:", modelResolverSource, "Model readers should return both the detected value and its source.");
        Assert.DoesNotContain("entry.model || entry.modelOverride || entry.selectedModel || entry.chatModel || entry.modelId", modelResolverSource, "Model field fallback should no longer be a hand-written OR chain.");
        return Task.CompletedTask;
    }

    public static Task HostedUiBridgeDefersAppStateDefaults()
    {
        var result = ResolveHostedUiModelFromAppState("""
            const sessionKey = 'chat-1';
            const states = [
              {
                sessionKey,
                sessionsResult: {
                  defaults: { provider: 'openai', model: 'gpt-default-root' }
                },
                chatModelOverrides: { 'chat-1': null }
              },
              {
                sessionKey,
                sessionsResult: {
                  defaults: { provider: 'openai', model: 'gpt-default-nested' },
                  sessions: [
                    { id: 'chat-1', providerOverride: 'anthropic', modelOverride: 'claude-3-5-sonnet' }
                  ]
                }
              }
            ];
            """);

        Assert.Equal("anthropic/claude-3-5-sonnet", result.Value, "A root default or null override must not mask a later active session model.");
        Assert.Equal("app-state:session", result.Source, "The model source should identify that the active session supplied the value.");
        return Task.CompletedTask;
    }

    public static Task HostedUiBridgeKeepsNullOverrideDefaultSemantics()
    {
        var result = ResolveHostedUiModelFromAppState("""
            const sessionKey = 'chat-1';
            const states = [
              {
                sessionKey,
                sessionsResult: {
                  defaults: { provider: 'openai', model: 'gpt-default-root' },
                  sessions: []
                },
                chatModelOverrides: { 'chat-1': null }
              }
            ];
            """);

        Assert.Equal("openai/gpt-default-root", result.Value, "A null override should inherit the current candidate default when no later session model wins.");
        Assert.Equal("app-state:default", result.Source, "The model source should identify inherited defaults.");
        return Task.CompletedTask;
    }

    public static Task HostedUiBridgeAvoidsObjectShapedModelStrings()
    {
        var explicitObjectResult = ResolveHostedUiModelFromAppState("""
            const sessionKey = 'chat-1';
            const states = [
              {
                sessionKey,
                sessionsResult: {
                  defaults: { provider: 'openai', model: 'gpt-default' }
                },
                chatModelOverrides: new Map([
                  ['chat-1', { value: { id: 'gpt-4.1' }, provider: { id: 'openai' } }]
                ])
              }
            ];
            """);

        Assert.Equal("openai/gpt-4.1", explicitObjectResult.Value, "Explicit object-shaped override payloads should resolve through id/value/name fields.");
        Assert.Equal("app-state:override", explicitObjectResult.Source, "Map-backed overrides should keep override priority.");

        var opaqueObjectResult = ResolveHostedUiModelFromAppState("""
            const sessionKey = 'chat-1';
            const states = [
              {
                sessionKey,
                sessionsResult: {
                  defaults: { provider: 'openai', model: 'gpt-default' },
                  sessions: [
                    { id: 'chat-1', provider: 'anthropic', model: 'claude-3-haiku' }
                  ]
                },
                chatModelOverrides: {
                  'chat-1': { value: { payload: 'do-not-stringify' }, provider: 'badprovider' }
                }
              }
            ];
            """);

        Assert.Equal("anthropic/claude-3-haiku", opaqueObjectResult.Value, "Opaque object-shaped values should not be converted into [object Object] model labels.");
        Assert.Equal("app-state:session", opaqueObjectResult.Source, "An invalid override should fall through to the active session value.");
        return Task.CompletedTask;
    }

    public static Task HostedUiSnapshotsCarryModelSourceInstrumentation()
    {
        var modelsPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw.Core",
            "Services",
            "SessionProbeModels.cs");
        var stateEffectsPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw.Core",
            "Services",
            "ShellSessionCoordinator.StateEffects.cs");

        var modelsSource = File.ReadAllText(modelsPath);
        var webViewSource = ReadWebViewServiceSource();
        var bridgeSource = ReadHostedBridgeScriptSource();
        var stateEffectsSource = File.ReadAllText(stateEffectsPath);

        Assert.Contains("string ModelSource", modelsSource, "Snapshots should carry where the MODEL value came from for future diagnostics.");
        Assert.Contains("currentModelSource", bridgeSource, "The injected bridge should emit model-source instrumentation.");
        Assert.Contains("GetString(root, \"currentModelSource\")", webViewSource, "Native parsing should retain the bridge-reported model source.");
        Assert.Contains("modelSource = string.IsNullOrWhiteSpace(snapshot.ModelSource)", stateEffectsSource, "Hosted UI state logs should expose the model source or null.");
        return Task.CompletedTask;
    }

    public static Task HostedUiSessionReadyCarriesModelSource()
    {
        var modelsPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw.Core",
            "Services",
            "SessionProbeModels.cs");
        var nativeBridgePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "HostedUiBridge.cs");
        var eventHandlersPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw.Core",
            "Services",
            "ShellSessionCoordinator.EventHandlers.cs");

        var modelsSource = File.ReadAllText(modelsPath);
        var bridgeSource = ReadHostedBridgeScriptSource();
        var nativeBridgeSource = File.ReadAllText(nativeBridgePath);
        var eventHandlersSource = File.ReadAllText(eventHandlersPath);

        Assert.Contains("string ModelSource", modelsSource, "Session ready event args should carry the model source.");
        Assert.Contains("modelSource: snapshot.currentModelSource", bridgeSource, "Session ready bridge messages should preserve the detected model source.");
        Assert.Contains("GetString(root, \"modelSource\")", nativeBridgeSource, "Native session-ready parsing should read the bridge-reported model source.");
        Assert.Contains("args.ModelSource", eventHandlersSource, "Session-ready logging should include the model source for diagnostics.");
        return Task.CompletedTask;
    }

    public static Task HostedUiBridgeIgnoresSidebarOnlyMutationsDuringStatusPolling()
    {
        var source = ReadHostedBridgeScriptSource();

        Assert.Contains("isStatusProbeExcludedElement", source, "Bridge should exclude status-irrelevant content before scanning the DOM.");
        Assert.Contains(".chat-sidebar, .sidebar-panel, .sidebar-content, .chat-tool-card__preview-frame", source, "Bridge should recognize OpenClaw Web UI's right sidebar and hosted canvas frame containers.");
        Assert.Contains("if (mutations.length > 0 && mutations.every(isSidebarOnlyMutation))", source, "Sidebar-only mutation storms should be classified before scheduling status work.");
        Assert.Contains("return;\n    }\n\n    schedule();", source, "Sidebar-only mutations should not schedule any status inspection.");
        Assert.DoesNotContain("scheduleSlow", source, "Sidebar-only changes should be ignored, not converted into periodic expensive DOM scans.");
        return Task.CompletedTask;
    }

    public static Task HostedUiBridgeIgnoresSettingsAndCronMutationStorms()
    {
        var source = ReadHostedBridgeScriptSource();

        Assert.Contains("readOpenClawAppStateStatus", source, "Bridge should use OpenClaw Lit app state as the connected-page status source before scanning DOM.");
        Assert.Contains("needsDomSignals", source, "Connected OpenClaw pages should not scan rendered settings/cron text for every status probe.");
        Assert.Contains("settings-workspace__body", source, "Settings category bodies such as Communications and Automation should be excluded from status mutation probes.");
        Assert.Contains("config-content", source, "Config form renders should be excluded from status mutation probes.");
        Assert.Contains("cron-workspace", source, "Cron/automation job tables and run logs should be excluded from status mutation probes.");
        Assert.Contains("cron-summary-strip", source, "Cron summary rerenders should not drive high-frequency native status probes.");
        Assert.DoesNotContain("'class']", source, "Bridge should not subscribe to high-volume class attribute mutations from Lit rerenders.");
        Assert.Contains("document.addEventListener('change'", source, "Form and model select changes should use explicit low-cost events instead of observing all class changes.");
        return Task.CompletedTask;
    }

    public static Task HostedUiBridgeReportsStaleBusyAndInputTextState()
    {
        var source = ReadHostedBridgeScriptSource();

        Assert.Contains("focusedInputHasText", source, "Bridge should distinguish focused empty editors from unsent user text.");
        Assert.Contains("activitySignature", source, "Bridge should emit a compact activity signature for stale stream detection.");
        Assert.Contains("isBusyStale", source, "Bridge should flag busy chat sessions that stop making visible or state progress.");
        Assert.Contains("snapshot.phase === 'connected' && snapshot.isBusy ? 4000 : 15000", source, "Busy connected pages should be polled faster than idle connected pages.");
        return Task.CompletedTask;
    }

    public static Task MainViewModelPreservesKnownModelOnEmptySnapshots()
    {
        var statusPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "ViewModels",
            "MainViewModel.Status.cs");
        var fieldsPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "ViewModels",
            "MainViewModel.Fields.cs");
        var stateEffectsPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw.Core",
            "Services",
            "ShellSessionCoordinator.StateEffects.cs");

        var statusSource = File.ReadAllText(statusPath);
        var fieldsSource = File.ReadAllText(fieldsPath);
        var stateEffectsSource = File.ReadAllText(stateEffectsPath);

        Assert.DoesNotContain("ModelSummaryText = FormatModelSummary(snapshot.CurrentModel);", statusSource, "Empty snapshots should not directly clear the last known model.");
        Assert.Contains("_lastKnownModelSummaryText", fieldsSource, "ViewModel should track the last non-empty model summary.");
        Assert.Contains("ApplyModelSummary(snapshot)", statusSource, "Model update behavior should be centralized so empty snapshots preserve the last known model.");
        Assert.Contains("currentModel = string.IsNullOrWhiteSpace(snapshot.CurrentModel)", stateEffectsSource, "Hosted UI state logs should expose whether model detection reached the native layer.");
        return Task.CompletedTask;
    }

    private static string ReadHostedBridgeScriptSource()
    {
        var scriptPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "HostedUiBridge.Script.js");

        Assert.True(File.Exists(scriptPath), "The hosted bridge browser script should live in a runnable JS asset.");
        return File.ReadAllText(scriptPath);
    }

    private static (string CurrentModel, string CurrentModelSource) InspectHostedBridgeScript(string setupScript)
    {
        var engine = new Engine(options => options.TimeoutInterval(TimeSpan.FromSeconds(3)));
        engine.Execute(CreateHostedBridgeDomHarnessScript());
        engine.Execute(setupScript);
        engine.Execute(CreateExecutableHostedBridgeScript());

        var json = engine.Evaluate("JSON.stringify(window.__openClawHostBridge.inspect())").AsString();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return (
            root.GetProperty("currentModel").GetString() ?? string.Empty,
            root.GetProperty("currentModelSource").GetString() ?? string.Empty);
    }

    private static string CreateExecutableHostedBridgeScript()
    {
        var strings = new Dictionary<string, string>
        {
            ["bridgeGatewayUiLoaded"] = "Gateway UI loaded",
            ["bridgePageLoading"] = "Page loading",
            ["bridgeTokenMissingSummary"] = "Token missing",
            ["bridgeTokenMissingDetail"] = "Token missing detail",
            ["bridgeTokenMismatchSummary"] = "Token mismatch",
            ["bridgeTokenMismatchDetail"] = "Token mismatch detail",
            ["bridgeDeviceTokenMismatchSummary"] = "Device token mismatch",
            ["bridgeDeviceTokenMismatchDetail"] = "Device token mismatch detail",
            ["bridgeOriginRejectedSummary"] = "Origin rejected",
            ["bridgeOriginRejectedDetail"] = "Origin rejected detail",
            ["bridgeTrustedProxyLoopbackSummary"] = "Trusted proxy loopback",
            ["bridgeTrustedProxyLoopbackDetail"] = "Trusted proxy loopback detail",
            ["bridgeMixedAuthSummary"] = "Mixed auth",
            ["bridgeMixedAuthDetail"] = "Mixed auth detail",
            ["bridgeTrustedProxyHeaderSummary"] = "Trusted proxy header",
            ["bridgeTrustedProxyHeaderDetail"] = "Trusted proxy header detail",
            ["bridgeTrustedProxyOriginSummary"] = "Trusted proxy origin",
            ["bridgeTrustedProxyOriginDetail"] = "Trusted proxy origin detail",
            ["bridgeRateLimitedSummary"] = "Rate limited",
            ["bridgeRateLimitedDetail"] = "Rate limited detail",
            ["bridgeInsecureHttpSummary"] = "Insecure HTTP",
            ["bridgeInsecureHttpDetail"] = "Insecure HTTP detail",
            ["bridgePairingSummary"] = "Pairing required",
            ["bridgePairingDetail"] = "Pairing required detail",
            ["bridgeAuthRequiredSummary"] = "Auth required",
            ["bridgeAuthRequiredDetail"] = "Auth required detail",
            ["bridgeGatewaySessionNotConnectedSummary"] = "Gateway session not connected",
            ["bridgeGatewaySessionNotConnectedDetail"] = "Gateway session not connected detail",
            ["bridgeConnectingSummary"] = "Connecting",
            ["bridgeConnectingDetail"] = "Connecting detail",
            ["bridgeConnectedSummary"] = "Connected",
        };

        var modelResolverPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "HostedUiBridge.ModelResolver.js");

        return ReadHostedBridgeScriptSource()
            .Replace("__OPENCLAW_BRIDGE_STRINGS_JSON__", JsonSerializer.Serialize(strings), StringComparison.Ordinal)
            .Replace("__OPENCLAW_MODEL_RESOLVER_SCRIPT__", File.ReadAllText(modelResolverPath), StringComparison.Ordinal);
    }

    private static string CreateHostedBridgeDomHarnessScript()
    {
        return """
            const __openClawPostedMessages = [];
            function HTMLSelectElement() {}
            function HTMLInputElement() {}
            function HTMLTextAreaElement() {}
            const Node = { ELEMENT_NODE: 1 };
            function MutationObserver(callback) {
              this.observe = () => {};
              this.disconnect = () => {};
            }
            function CustomEvent(type, options) {
              this.type = type;
              this.detail = options?.detail;
            }
            const location = { href: 'https://gateway.example/control?session=chat-1' };
            const history = {
              pushState: () => {},
              replaceState: () => {}
            };
            const window = {
              location,
              chrome: {
                webview: {
                  postMessage: (message) => __openClawPostedMessages.push(message)
                }
              },
              getComputedStyle: () => ({ display: 'block', visibility: 'visible' }),
              setTimeout: () => 1,
              clearTimeout: () => {},
              addEventListener: () => {},
              dispatchEvent: () => true
            };
            const document = {
              body: {},
              readyState: 'complete',
              visibilityState: 'visible',
              activeElement: null,
              documentElement: {},
              querySelector: (selector) => document.__querySelector(selector),
              querySelectorAll: (selector) => document.__querySelectorAll(selector),
              __querySelector: () => null,
              __querySelectorAll: () => [],
              addEventListener: () => {},
              dispatchEvent: () => true
            };
            globalThis.window = window;
            globalThis.document = document;
            globalThis.location = location;
            globalThis.history = history;
            globalThis.Node = Node;
            globalThis.HTMLSelectElement = HTMLSelectElement;
            globalThis.HTMLInputElement = HTMLInputElement;
            globalThis.HTMLTextAreaElement = HTMLTextAreaElement;
            globalThis.MutationObserver = MutationObserver;
            globalThis.CustomEvent = CustomEvent;
            """;
    }

    private static (string Value, string Source) ResolveHostedUiModelFromAppState(string setupScript)
    {
        var scriptPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "HostedUiBridge.ModelResolver.js");

        Assert.True(File.Exists(scriptPath), "Hosted bridge model resolution should live in a runnable JS asset.");

        var engine = new Engine(options => options.TimeoutInterval(TimeSpan.FromSeconds(3)));
        engine.Execute(File.ReadAllText(scriptPath));
        engine.Execute(setupScript);

        var json = engine.Evaluate("JSON.stringify(resolveOpenClawAppStateModel(states, sessionKey))").AsString();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var value = root.GetProperty("value").GetString() ?? string.Empty;
        var source = root.GetProperty("source").GetString() ?? string.Empty;
        return (value, source);
    }
}
