const openClawActivityState = (() => {
  const BUSY_STALE_THRESHOLD_MS = 30000;

  const createActivityTracker = ({ dom, mutationFilter }) => {
    const isStatusProbeExcludedElement = mutationFilter.isStatusProbeExcludedElement;
    let lastBusyActivitySignature = '';
    let lastBusyActivityChangedAt = Date.now();

    const readMessageText = (message) => {
      if (!message || typeof message !== 'object') return '';
      const rawParts = Array.isArray(message.parts) ? message.parts : [];
      const partText = rawParts
        .map((part) => typeof part === 'string'
          ? part
          : dom.compactText(part?.text || part?.content || part?.value || ''))
        .join(' ');
      return dom.compactText(message.text || message.content || message.message || partText || '');
    };

    const readChatActivitySignature = (app) => {
      if (!app) return '';
      const sessionKey = dom.compactText(app.sessionKey || app.settings?.sessionKey || '');
      const runId = dom.compactText(app.chatRunId || app.currentRunId || app.activeRunId || '');
      const seq = dom.compactText(app.eventSeq || app.lastEventSeq || app.chatEventSeq || app.gatewayEventSeq || '');
      const stateVersion = dom.compactText(app.stateVersion || app.lastStateVersion || app.chatStateVersion || app.gatewayStateVersion || '');
      const messages = Array.isArray(app.chatMessages)
        ? app.chatMessages
        : Array.isArray(app.messages)
          ? app.messages
          : Array.isArray(app.sessionMessages)
            ? app.sessionMessages
            : [];
      const lastMessage = messages.length > 0 ? messages[messages.length - 1] : null;
      const lastMessageId = dom.compactText(lastMessage?.id || lastMessage?.key || lastMessage?.uuid || '');
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

      const tab = dom.compactText(app.tab || '');
      const lastError = dom.compactText(app.lastError || app.lastErrorCode || '');
      const isChatBusy = Boolean(
        app.chatLoading ||
        app.chatSending ||
        app.chatRunId ||
        app.chatStream != null ||
        app.chatManualRefreshInFlight);
      const isShellBusy = Boolean(
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
        isBusy: isChatBusy || isShellBusy,
        isChatBusy,
        activitySignature: isChatBusy ? readChatActivitySignature(app) : ''
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
          if (isStatusProbeExcludedElement(element) || !dom.isVisible(element)) continue;
          const text = dom.compactText(dom.textOf(element));
          const id = dom.compactText(
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

    const applyBusyStaleness = (snapshot) => {
      if (!snapshot.isBusy || snapshot.isBusyStaleCandidate === false) {
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

    return {
      readChatActivitySignature,
      readOpenClawAppStateStatus,
      detectBusyFromApi,
      collectDomActivitySignature,
      applyBusyStaleness
    };
  };

  return { createActivityTracker };
})();
