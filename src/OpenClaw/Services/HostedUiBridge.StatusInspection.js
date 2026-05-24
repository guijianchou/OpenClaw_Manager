const openClawStatusInspection = (() => {
  const createInspector = ({ strings, mutationFilter, modelResolver, statusKind }) => {
  const isVisible = (el) => {
    if (!el) return false;
    const style = window.getComputedStyle(el);
    if (style.display === 'none' || style.visibility === 'hidden') return false;
    const rect = el.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  };

  const textOf = (el) => {
    return [el?.innerText, el?.textContent]
      .filter(Boolean)
      .join(' ')
      .replace(/\s+/g, ' ')
      .trim();
  };

  const labelOf = (el) => {
    return [
      el?.getAttribute?.('aria-label'),
      el?.getAttribute?.('title'),
      textOf(el)
    ].filter(Boolean).join(' ').trim();
  };

  const isEditableElement = (el) => {
    if (!el) return false;
    if (el instanceof HTMLTextAreaElement) return true;
    if (el instanceof HTMLInputElement) {
      const type = (el.type || 'text').toLowerCase();
      return !['button', 'checkbox', 'color', 'file', 'hidden', 'image', 'radio', 'range', 'reset', 'submit'].includes(type);
    }

    const role = el.getAttribute?.('role') || '';
    return el.isContentEditable || role === 'textbox';
  };

  const compactText = (value) => (value == null ? '' : String(value)).replace(/\s+/g, ' ').trim();
  const isStatusProbeExcludedElement = mutationFilter.isStatusProbeExcludedElement;

  const collectSignalText = () => {
    const selectors = [
      '[role="alert"]', '[role="status"]', '[aria-live]',
      '[data-status]', '[data-state]', '[data-busy]',
      '[class*="auth"]', '[class*="login"]', '[class*="signin"]',
      '[class*="error"]', '[class*="pair"]', '[class*="origin"]',
      '[class*="proxy"]', '[class*="connect"]', '[class*="disconnect"]'
    ];
    const fragments = [];
    const seen = new Set();
    let totalLength = 0;

    for (const selector of selectors) {
      for (const element of document.querySelectorAll(selector)) {
        if (isStatusProbeExcludedElement(element) || !isVisible(element)) continue;
        const text = compactText(textOf(element)).toLowerCase();
        if (!text) continue;
        const normalized = text.length > 240 ? `${text.slice(0, 240)}...` : text;
        if (seen.has(normalized)) continue;
        seen.add(normalized);
        fragments.push(normalized);
        totalLength += normalized.length;
        if (fragments.length >= 6 || totalLength >= 900) break;
      }
      if (fragments.length >= 6 || totalLength >= 900) break;
    }

    return fragments.join(' ');
  };

  const matchAny = (haystack, needles) => needles.find((needle) => haystack.includes(needle)) || '';

  const modelPattern = /\b(?:gpt|o\d|claude|gemini|qwen|deepseek|llama|mistral|glm|yi|command|grok|codex|kimi|moonshot)[a-z0-9._:+/-]*\b/i;

  const sanitizeModelLabel = (text) => {
    const normalized = compactText(text)
      .replace(/\b(?:current|selected|default)\s+model\b[:\s-]*/ig, '')
      .replace(/\bmodel\b[:\s-]*/ig, '')
      .replace(/\bprovider\b[:\s-]*/ig, '')
      .replace(/\s+\|\s+/g, ' | ')
      .trim();

    if (!normalized) return '';

    const defaultWrappedModel = normalized.match(/^(?:default|selected|current)(?:\s+model)?\s*\(([^)]+)\)$/i);
    if (defaultWrappedModel) {
      return sanitizeModelLabel(defaultWrappedModel[1]);
    }

    const prefixedModel = normalized
      .replace(/^(?:default|selected|current)(?:\s+model)?\s*[:\s-]+/i, '')
      .replace(/^\(([^)]+)\)$/, '$1')
      .trim();
    if (prefixedModel && prefixedModel !== normalized) {
      return sanitizeModelLabel(prefixedModel);
    }

    if (normalized.length <= 32 && modelPattern.test(normalized)) return normalized;

    const segment = normalized
      .split(/(?:\s{3,}|\n|\||,)/)
      .map((part) => compactText(part))
      .find((part) => modelPattern.test(part));

    if (segment) return segment.length <= 32 ? segment : segment.slice(0, 31).trimEnd();
    const match = normalized.match(modelPattern);
    return match ? match[0] : '';
  };

  const cleanTrustedModelValue = (value) => compactText(value).slice(0, 96);
  const readScalarText = (value) => {
    if (value == null) return '';
    return typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean'
      ? compactText(value)
      : '';
  };

  const emptyModelResult = () => ({ value: '', source: '' });
  const modelResult = (value, source) => {
    return { value: cleanTrustedModelValue(value), source };
  };
  const SESSION_KEY_PATHS = [
    ['sessionKey'],
    ['activeSessionKey'],
    ['currentSessionKey'],
    ['chatSessionKey'],
    ['session', 'key'],
    ['settings', 'sessionKey']
  ];
  const APP_STATE_PATHS = [
    ['state'],
    ['appState'],
    ['store', 'state'],
    ['controller', 'state'],
    ['chatState'],
    ['sessionState']
  ];

  const readPath = (target, path) => {
    return path.reduce((current, key) => current == null ? undefined : current[key], target);
  };

  const readFirstPath = (target, paths) => {
    if (!target || typeof target !== 'object') return '';
    for (const path of paths) {
      const text = readScalarText(readPath(target, path));
      if (text) return text;
    }

    return '';
  };

  const uniqueObjects = (items) => {
    const seen = new Set();
    return items.filter((item) => {
      if (!item || typeof item !== 'object' || seen.has(item)) return false;
      seen.add(item);
      return true;
    });
  };

  const readSessionKeyFromUrl = () => {
    try {
      return compactText(new URL(window.location.href).searchParams.get('session') || '');
    } catch {
      return '';
    }
  };

  const readOpenClawStateCandidates = () => {
    const app = document.querySelector('openclaw-app');
    if (!app) return [];
    return uniqueObjects([
      app,
      ...APP_STATE_PATHS
        .map((path) => readPath(app, path))
        .filter((value) => value && typeof value === 'object')
    ]);
  };

  const readCurrentSessionKey = (states) => {
    for (const state of states) {
      const key = readFirstPath(state, SESSION_KEY_PATHS);
      if (key) return key;
    }

    return readSessionKeyFromUrl();
  };

  const readOpenClawAppStateModel = () => {
    const states = readOpenClawStateCandidates();
    if (states.length === 0) return emptyModelResult();

    return modelResolver.resolveOpenClawAppStateModel(states, readCurrentSessionKey(states));
  };

  const readOpenClawModelSelect = () => {
    const select = document.querySelector('select[data-chat-model-select="true"], select[data-chat-model-select]');
    if (!(select instanceof HTMLSelectElement) || !isVisible(select)) return emptyModelResult();

    const selectedOption = select.selectedOptions?.[0] || null;
    const selectedModelOptionValue = compactText(selectedOption?.value || '');
    const selectedModelValue = compactText(select.value || '');
    const selectedModelTitle = compactText(select.getAttribute('title') || '');
    const selectedModelText = compactText(selectedOption?.textContent || '');

    for (const value of [
      selectedModelOptionValue,
      selectedModelValue,
      selectedModelTitle,
      selectedModelText
    ]) {
      const label = sanitizeModelLabel(value);
      if (label) return modelResult(label, 'model-select');
    }

    return emptyModelResult();
  };

  const readModelFromDomCandidates = () => {
    const candidates = [];
    const selectionBoostOf = (el) => {
      const selected = [
        el?.getAttribute?.('aria-selected'),
        el?.getAttribute?.('aria-checked'),
        el?.getAttribute?.('aria-pressed'),
        el?.getAttribute?.('data-selected'),
        el?.getAttribute?.('data-state')
      ].filter(Boolean).join(' ').toLowerCase();
      return /true|selected|checked|active|current/.test(selected) ? 18 : 0;
    };

    const viewportBoostOf = (el) => {
      if (!el || typeof el.getBoundingClientRect !== 'function') return 0;
      const top = el.getBoundingClientRect().top;
      return Number.isFinite(top) && top >= 0 && top <= 260 ? 8 : 0;
    };

    const pushCandidate = (text, score, el, source) => {
      const label = sanitizeModelLabel(text);
      if (!label) return;
      candidates.push({ label, source, score: score + selectionBoostOf(el) + viewportBoostOf(el) });
    };

    Array.from(document.querySelectorAll('[data-current-model], [data-selected-model], [data-model-name]'))
      .filter((el) => !isStatusProbeExcludedElement(el) && isVisible(el))
      .forEach((el) => pushCandidate(textOf(el), 120, el, 'dom:data-model'));

    Array.from(document.querySelectorAll('select'))
      .filter((el) => !isStatusProbeExcludedElement(el) && isVisible(el))
      .forEach((el) => {
        const selectedText = Array.from(el.selectedOptions || [])
          .map((option) => option.textContent || '')
          .join(' ');
        const combined = `${labelOf(el)} ${selectedText}`.trim();
        if (/\bmodel\b/i.test(combined) || modelPattern.test(selectedText)) {
          pushCandidate(selectedText || combined, /\bmodel\b/i.test(combined) ? 115 : 90, el, 'dom:select');
        }
      });

    Array.from(document.querySelectorAll('[role="combobox"], button[aria-haspopup="listbox"], button, [role="button"], input[type="text"], input:not([type])'))
      .filter((el) => !isStatusProbeExcludedElement(el) && isVisible(el))
      .forEach((el) => {
        const rawValue = 'value' in el && typeof el.value === 'string' ? el.value : '';
        const combined = [labelOf(el), rawValue, el.getAttribute?.('placeholder')].filter(Boolean).join(' ').trim();
        if (!/\bmodel\b/i.test(combined) && !modelPattern.test(rawValue) && !modelPattern.test(textOf(el))) return;
        const score = /\bmodel\b/i.test(combined) ? 105 : 80;
        pushCandidate(rawValue || textOf(el) || combined, score, el, 'dom:control');
      });

    if (candidates.length === 0) return emptyModelResult();
    candidates.sort((left, right) => {
      if (right.score !== left.score) return right.score - left.score;
      return left.label.length - right.label.length;
    });
    return modelResult(candidates[0].label, candidates[0].source);
  };

  const MODEL_SOURCE_READERS = [
    readOpenClawAppStateModel,
    readOpenClawModelSelect,
    readModelFromDomCandidates
  ];

  const readCurrentModel = () => {
    for (const readModel of MODEL_SOURCE_READERS) {
      const result = readModel();
      if (result?.value) return result;
    }

    return emptyModelResult();
  };

  const readMessageText = (message) => {
    if (!message || typeof message !== 'object') return '';
    const rawParts = Array.isArray(message.parts) ? message.parts : [];
    const partText = rawParts
      .map((part) => typeof part === 'string'
        ? part
        : compactText(part?.text || part?.content || part?.value || ''))
      .join(' ');
    return compactText(message.text || message.content || message.message || partText || '');
  };

  const readChatActivitySignature = (app) => {
    if (!app) return '';
    const sessionKey = compactText(app.sessionKey || app.settings?.sessionKey || '');
    const runId = compactText(app.chatRunId || app.currentRunId || app.activeRunId || '');
    const seq = compactText(app.eventSeq || app.lastEventSeq || app.chatEventSeq || app.gatewayEventSeq || '');
    const stateVersion = compactText(app.stateVersion || app.lastStateVersion || app.chatStateVersion || app.gatewayStateVersion || '');
    const messages = Array.isArray(app.chatMessages)
      ? app.chatMessages
      : Array.isArray(app.messages)
        ? app.messages
        : Array.isArray(app.sessionMessages)
          ? app.sessionMessages
          : [];
    const lastMessage = messages.length > 0 ? messages[messages.length - 1] : null;
    const lastMessageId = compactText(lastMessage?.id || lastMessage?.key || lastMessage?.uuid || '');
    const lastText = readMessageText(lastMessage);
    const lastTextTail = lastText.slice(Math.max(0, lastText.length - 96));
    return [
      sessionKey,
      runId,
      seq,
      stateVersion,
      String(messages.length),
      lastMessageId,
      String(lastText.length),
      lastTextTail
    ].filter(Boolean).join('|').slice(0, 512);
  };

  const readOpenClawAppStateStatus = () => {
    const app = document.querySelector('openclaw-app');
    if (!app) return null;

    const tab = compactText(app.tab || '');
    const lastError = compactText(app.lastError || app.lastErrorCode || '');
    const isBusy = Boolean(
      app.chatLoading ||
      app.chatSending ||
      app.chatRunId ||
      app.chatStream != null ||
      app.chatManualRefreshInFlight ||
      app.configLoading ||
      app.configSaving ||
      app.configApplying ||
      app.updateRunning ||
      app.channelsLoading ||
      app.whatsappBusy ||
      app.cronLoading ||
      app.cronBusy ||
      app.jobsLoadingMore ||
      app.runsLoadingMore);

    return {
      connected: app.connected === true,
      tab,
      lastError,
      shellDetected: app.connected === true || Boolean(tab),
      isBusy,
      activitySignature: readChatActivitySignature(app)
    };
  };

  const detectBusyFromApi = () => {
    const candidates = [
      window.chat, window.__openclaw?.chat, window.__OPENCLAW__?.chat,
      window.__APP__?.chat, window.app?.chat
    ];
    const busyKeys = ['isRunning', 'running', 'isBusy', 'busy', 'isStreaming', 'streaming', 'isGenerating', 'generating'];
    return candidates.some((candidate) =>
      candidate && busyKeys.some((key) => typeof candidate[key] === 'boolean' && candidate[key]));
  };

  const collectDomActivitySignature = () => {
    const selectors = [
      '[data-message-id]', '[data-run-id]', '[data-chat-message]',
      '[class*="message"]', '[class*="response"]', '[class*="assistant"]',
      '[role="log"]', '[aria-live]'
    ];
    const fragments = [];
    const seen = new Set();

    for (const selector of selectors) {
      for (const element of document.querySelectorAll(selector)) {
        if (isStatusProbeExcludedElement(element) || !isVisible(element)) continue;
        const text = compactText(textOf(element));
        const id = compactText(
          element.getAttribute?.('data-message-id') ||
          element.getAttribute?.('data-run-id') ||
          element.id ||
          '');
        const tail = text.slice(Math.max(0, text.length - 96));
        const fragment = [selector, id, String(text.length), tail].filter(Boolean).join(':');
        if (!fragment || seen.has(fragment)) continue;
        seen.add(fragment);
        fragments.push(fragment);
        if (fragments.length >= 4) break;
      }
      if (fragments.length >= 4) break;
    }

    return fragments.join('|').slice(0, 512);
  };

  const BUSY_STALE_THRESHOLD_MS = 30000;
  let lastBusyActivitySignature = '';
  let lastBusyActivityChangedAt = Date.now();

  const applyBusyStaleness = (snapshot) => {
    if (!snapshot.isBusy) {
      lastBusyActivitySignature = '';
      lastBusyActivityChangedAt = Date.now();
      return {
        ...snapshot,
        isBusyStale: false,
        busyStaleSeconds: 0
      };
    }

    const now = Date.now();
    const activitySignature = snapshot.activitySignature || `${snapshot.phase}:${snapshot.workState}`;
    if (activitySignature !== lastBusyActivitySignature) {
      lastBusyActivitySignature = activitySignature;
      lastBusyActivityChangedAt = now;
    }

    const staleMs = now - lastBusyActivityChangedAt;
    return {
      ...snapshot,
      isBusyStale: staleMs >= BUSY_STALE_THRESHOLD_MS,
      busyStaleSeconds: Math.floor(staleMs / 1000)
    };
  };

  const inspectControlUi = () => {
    const url = window.location ? window.location.href : '';
    const lowerUrl = url.toLowerCase();
    const appState = readOpenClawAppStateStatus();
    const needsDomSignals = !appState || !appState.connected || Boolean(appState.lastError);
    const text = needsDomSignals ? collectSignalText() : '';
    const activeElement = document.activeElement;
    const inputFocused = isEditableElement(activeElement) && isVisible(activeElement);
    const focusedInputHasText = inputFocused && compactText(
      activeElement instanceof HTMLInputElement || activeElement instanceof HTMLTextAreaElement
        ? activeElement.value
        : activeElement?.textContent || '').length > 0;

    const authMatch = matchAny(text, [
      'authentication required', 'authorization failed', 'unauthorized',
      'access denied', 'token required', 'password required',
      'session expired', 'sign in', 'log in', 'login required'
    ]);
    const tokenMissingMatch = matchAny(text, [
      'auth_token_missing', 'token missing', 'missing shared token'
    ]);
    const tokenMismatchMatch = matchAny(text, [
      'auth_token_mismatch', 'token mismatch', 'shared token did not match',
      'canretrywithdevicetoken'
    ]);
    const deviceTokenMismatchMatch = matchAny(text, [
      'auth_device_token_mismatch', 'device token mismatch',
      'cached per-device token is stale', 'stale or revoked device token'
    ]);
    const pairingMatch = matchAny(text, [
      'pairing required', 'pair this device', 'device approval required',
      'device not paired', 'disconnected (1008)'
    ]);
    const originMatch = matchAny(text, [
      'origin not allowed', 'origin rejected', 'allowed origins',
      'forbidden origin', 'trusted proxy'
    ]);
    const trustedProxyLoopbackMatch = matchAny(text, [
      'trusted_proxy_loopback_source', 'loopback-source trusted-proxy',
      'same-host loopback reverse proxies do not satisfy trusted-proxy auth',
      'same-host loopback reverse proxy', 'trusted-proxy auth rejects loopback-source requests'
    ]);
    const mixedTrustedProxyTokenMatch = matchAny(text, [
      'mixed_trusted_proxy_token', 'mixed token config',
      'both a gateway.auth.token', 'trusted-proxy mode are active at the same time',
      'remove the shared token when using trusted-proxy mode'
    ]);
    const trustedProxyIdentityHeaderMatch = matchAny(text, [
      'trusted_proxy_user_missing', 'trusted_proxy_user_not_allowed',
      'trustedproxy_missing_header', 'missing_header',
      'identity headers', 'required header wasn\'t present'
    ]);
    const trustedProxyOriginRejectedMatch = matchAny(text, [
      'trusted_proxy_origin_not_allowed', 'origin did not pass control ui origin checks'
    ]);
    const rateLimitMatch = matchAny(text, [
      'retry later', 'too many failed auth attempts', 'retry-after',
      'rate limited', 'rate limit'
    ]);
    const gatewayErrorMatch = matchAny(text, [
      'unable to connect', 'connection lost', 'gateway unavailable',
      'failed to connect', 'websocket closed', 'disconnect code'
    ]);
    const connectingMatch = matchAny(text, [
      'connecting to gateway', 'waiting for gateway',
      'reconnecting', 'establishing connection'
    ]);
    const isNonLocalHttp =
      lowerUrl.startsWith('http://') &&
      !/\/\/(?:127\.0\.0\.1|localhost|\[::1\])(?::|\/|$)/.test(lowerUrl);
    const insecureHttpMatch = matchAny(text, [
      'non-secure context', 'webcrypto', 'allowinsecureauth',
      'dangerouslydisabledeviceauth', 'device identity checks',
      'use https', 'tailscale serve'
    ]);

    const shellDetected = appState?.shellDetected ||
      (needsDomSignals && (
        hasVisibleElement('textarea, input:not([type]), input[type="text"], [contenteditable="true"], [role="textbox"]') ||
        hasVisibleElement('button, [role="button"], nav, aside, [role="navigation"]', (el) => {
          const label = labelOf(el).toLowerCase();
          return /stop|abort|dashboard|settings|sessions|workers|models|new chat|history/.test(label);
        })));

    const busyByButton = needsDomSignals && hasVisibleElement('button, [role="button"], [aria-label], [title]', (el) => {
      const label = labelOf(el).toLowerCase();
      return /\b(stop|abort|cancel)\b/.test(label);
    });
    const busyBySignals = needsDomSignals && hasVisibleElement(
      '[aria-busy="true"], [role="progressbar"], [data-busy="true"], [data-running="true"], [data-state="running"], [data-state="streaming"], [data-status="running"], [data-status="streaming"]');
    const isBusy = Boolean(appState?.isBusy) || detectBusyFromApi() || busyByButton || busyBySignals;
    const workState = isBusy ? 'busy' : shellDetected ? 'idle' : 'unknown';
    const activitySignature = [
      appState?.activitySignature,
      isBusy ? collectDomActivitySignature() : ''
    ].filter(Boolean).join('|').slice(0, 512);
    const modelSnapshot = readCurrentModel();

    let phase = 'page_loaded';
    let summary = strings.bridgeGatewayUiLoaded;
    let detail = '';

    if (!document.body || document.readyState === 'loading') {
      phase = 'loading';
      summary = strings.bridgePageLoading;
    } else if (tokenMissingMatch) {
      phase = 'auth_required';
      summary = strings.bridgeTokenMissingSummary;
      detail = strings.bridgeTokenMissingDetail;
    } else if (tokenMismatchMatch) {
      phase = 'auth_required';
      summary = strings.bridgeTokenMismatchSummary;
      detail = strings.bridgeTokenMismatchDetail;
    } else if (deviceTokenMismatchMatch) {
      phase = 'auth_required';
      summary = strings.bridgeDeviceTokenMismatchSummary;
      detail = strings.bridgeDeviceTokenMismatchDetail;
    } else if (originMatch) {
      phase = 'origin_rejected';
      summary = strings.bridgeOriginRejectedSummary;
      detail = strings.bridgeOriginRejectedDetail;
    } else if (trustedProxyLoopbackMatch) {
      phase = 'auth_required';
      summary = strings.bridgeTrustedProxyLoopbackSummary;
      detail = strings.bridgeTrustedProxyLoopbackDetail;
    } else if (mixedTrustedProxyTokenMatch) {
      phase = 'auth_required';
      summary = strings.bridgeMixedAuthSummary;
      detail = strings.bridgeMixedAuthDetail;
    } else if (trustedProxyIdentityHeaderMatch) {
      phase = 'auth_required';
      summary = strings.bridgeTrustedProxyHeaderSummary;
      detail = strings.bridgeTrustedProxyHeaderDetail;
    } else if (trustedProxyOriginRejectedMatch) {
      phase = 'origin_rejected';
      summary = strings.bridgeTrustedProxyOriginSummary;
      detail = strings.bridgeTrustedProxyOriginDetail;
    } else if (rateLimitMatch) {
      phase = 'auth_required';
      summary = strings.bridgeRateLimitedSummary;
      detail = strings.bridgeRateLimitedDetail;
    } else if (isNonLocalHttp && insecureHttpMatch) {
      phase = 'gateway_error';
      summary = strings.bridgeInsecureHttpSummary;
      detail = strings.bridgeInsecureHttpDetail;
    } else if (pairingMatch) {
      phase = 'pairing_required';
      summary = strings.bridgePairingSummary;
      detail = strings.bridgePairingDetail;
    } else if (authMatch || /\/(login|signin|auth)(\/|$|\?)/.test(lowerUrl)) {
      phase = 'auth_required';
      summary = strings.bridgeAuthRequiredSummary;
      detail = strings.bridgeAuthRequiredDetail;
    } else if (gatewayErrorMatch) {
      phase = 'gateway_error';
      summary = strings.bridgeGatewaySessionNotConnectedSummary;
      detail = strings.bridgeGatewaySessionNotConnectedDetail;
    } else if (connectingMatch) {
      phase = 'gateway_connecting';
      summary = strings.bridgeConnectingSummary;
      detail = strings.bridgeConnectingDetail;
    } else if (shellDetected) {
      phase = 'connected';
      summary = strings.bridgeConnectedSummary;
    }

    return applyBusyStaleness({
      kind: statusKind, phase, summary, detail, url, shellDetected, isBusy, inputFocused, focusedInputHasText, workState,
      activitySignature,
      currentModel: modelSnapshot.value,
      currentModelSource: modelSnapshot.source
    });
  };

  const hasVisibleElement = (selector, predicate) => {
    return Array.from(document.querySelectorAll(selector))
      .some((el) => !isStatusProbeExcludedElement(el) && isVisible(el) && (!predicate || predicate(el)));
  };

    return { inspectControlUi, isEditableElement, compactText };
  };

  return { createInspector };
})();
