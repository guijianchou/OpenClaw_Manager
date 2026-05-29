(() => {
  const STRINGS = __OPENCLAW_BRIDGE_STRINGS_JSON__;
  const OWNER_TOKEN = __OPENCLAW_OWNER_TOKEN_JSON__;

__OPENCLAW_HOST_MESSAGING_SCRIPT__

__OPENCLAW_MUTATION_FILTER_SCRIPT__

__OPENCLAW_MODEL_RESOLVER_SCRIPT__

__OPENCLAW_DOM_UTILITIES_SCRIPT__

__OPENCLAW_MODEL_DOM_FALLBACK_SCRIPT__

__OPENCLAW_ACTIVITY_STATE_SCRIPT__

__OPENCLAW_PHASE_CLASSIFIER_SCRIPT__

__OPENCLAW_STATUS_INSPECTION_SCRIPT__

__OPENCLAW_COMMAND_DISPATCH_SCRIPT__

  const { KIND, SESSION_READY_KIND, GAP_KIND, postHostMessage } = openClawHostMessaging;
  openClawHostMessaging.setOwnerToken(OWNER_TOKEN);
  const { inspectControlUi } = openClawStatusInspection.createInspector({
    strings: STRINGS,
    mutationFilter: openClawMutationFilter,
    modelResolver: openClawModelResolver,
    statusKind: KIND
  });

  let lastSeq = null;
  let lastStateVersion = null;
  let sessionReadyEmitted = false;
  let sessionReadyModelEmitted = false;
  let lastSerialized = '';

  const postStatus = (snapshot = inspectControlUi()) => {
    const serialized = JSON.stringify(snapshot);
    if (serialized === lastSerialized) return;
    if (!postHostMessage(snapshot)) return;
    lastSerialized = serialized;
  };

  const postSessionReady = (snapshot = inspectControlUi()) => {
    if (snapshot.phase !== 'connected' || !snapshot.shellDetected) {
      return false;
    }

    const posted = postHostMessage({
      kind: SESSION_READY_KIND,
      detectedAt: new Date().toISOString(),
      model: snapshot.currentModel,
      modelSource: snapshot.currentModelSource,
      uri: snapshot.url
    });

    if (posted) {
      sessionReadyEmitted = true;
      sessionReadyModelEmitted = sessionReadyModelEmitted || Boolean(String(snapshot.currentModel || '').trim());
    }

    return posted;
  };

  const checkSessionReady = (snapshot = inspectControlUi()) => {
    const hasModel = Boolean(String(snapshot.currentModel || '').trim());
    if (sessionReadyEmitted && (sessionReadyModelEmitted || !hasModel)) return;

    if (snapshot.phase === 'connected' && snapshot.shellDetected) {
      postSessionReady(snapshot);
    }
  };

  const checkForGap = (currentSeq, stateVersion) => {
    if (lastSeq === null) {
      lastSeq = currentSeq;
      lastStateVersion = stateVersion;
      return null;
    }

    if (currentSeq !== lastSeq + 1) {
      const gap = {
        kind: GAP_KIND,
        expectedSeq: lastSeq + 1,
        gotSeq: currentSeq,
        lastStateVersion: lastStateVersion,
        currentStateVersion: stateVersion,
        detectedAt: new Date().toISOString()
      };
      lastSeq = currentSeq;
      lastStateVersion = stateVersion;
      return gap;
    }

    lastSeq = currentSeq;
    lastStateVersion = stateVersion;
    return null;
  };

  const onCommand = openClawCommandDispatch.createCommandHandler({
    inspectControlUi,
    postStatus,
    checkSessionReady
  });

  window.__openClawHostBridge = {
    ownerToken: OWNER_TOKEN,
    pageToken: openClawHostMessaging.pageToken,
    inspect: inspectControlUi,
    sendStatus: postStatus,
    onCommand,
    reportSeq: (seq, stateVersion) => {
      const gap = checkForGap(seq, stateVersion);
      if (gap) {
        postHostMessage(gap);
      }
      postStatus();
    },
    reportSessionReady: () => {
      return postSessionReady();
    }
  };

  let scheduledPost = 0;
  let scheduledPostDelay = 0;
  const scheduleAfter = (delay) => {
    if (scheduledPost && delay >= scheduledPostDelay) return;
    if (scheduledPost) {
      window.clearTimeout(scheduledPost);
    }

    scheduledPostDelay = delay;
    scheduledPost = window.setTimeout(() => {
      scheduledPost = 0;
      scheduledPostDelay = 0;
      const snapshot = inspectControlUi();
      postStatus(snapshot);
      checkSessionReady(snapshot);
    }, delay);
  };

  const schedule = () => {
    scheduleAfter(document.visibilityState === 'visible' ? 220 : 1200);
  };

  const scheduleFromInteraction = (event) => {
    if (!openClawMutationFilter.isStatusRelevantEventTarget(event.target)) return;
    schedule();
  };

  const observer = new MutationObserver((mutations) => {
    if (mutations.length > 0 && !mutations.some(openClawMutationFilter.isStatusRelevantMutation)) {
      return;
    }

    schedule();
  });
  if (document.documentElement) {
    observer.observe(document.documentElement, {
      childList: true, subtree: true,
      attributes: true,
      attributeFilter: ['aria-busy', 'data-busy', 'data-running', 'data-state', 'data-status', 'aria-label', 'title']
    });
  }

  const wrapHistory = (methodName) => {
    const original = history[methodName];
    if (typeof original !== 'function') return;
    history[methodName] = function (...args) {
      const result = original.apply(this, args);
      schedule();
      return result;
    };
  };
  wrapHistory('pushState');
  wrapHistory('replaceState');

  window.addEventListener('popstate', schedule);
  window.addEventListener('load', schedule);
  document.addEventListener('readystatechange', schedule);
  document.addEventListener('change', scheduleFromInteraction, true);

  let pollInterval = 8000;
  let pollTimer = 0;
  let nextPollAt = 0;

  const getPollInterval = (snapshot) => {
    if (snapshot.phase === 'connected' && snapshot.isBusy) return 4000;
    if (snapshot.phase === 'gateway_connecting' || snapshot.phase === 'page_loaded') return 4000;
    if (snapshot.phase !== 'connected') return 4000;
    return 15000;
  };

  const scheduleNextPoll = (interval, now = Date.now()) => {
    pollInterval = interval;
    nextPollAt = nextPollAt > now ? nextPollAt + interval : now + interval;
    const delay = Math.max(0, nextPollAt - Date.now());
    pollTimer = window.setTimeout(tick, delay);
  };

  const tick = () => {
    const snapshot = inspectControlUi();
    postStatus(snapshot);
    checkSessionReady(snapshot);
    scheduleNextPoll(getPollInterval(snapshot));
  };

  const restartPolling = (interval = pollInterval) => {
    if (pollTimer) {
      window.clearTimeout(pollTimer);
    }

    nextPollAt = 0;
    scheduleNextPoll(interval);
  };

  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible') {
      schedule();
      restartPolling(1200);
      return;
    }

    restartPolling(15000);
  });

  window.addEventListener('focus', () => {
    schedule();
    restartPolling(1200);
  });

  window.addEventListener('blur', () => {
    restartPolling(12000);
  });

  restartPolling();
  schedule();
})();
