const openClawCommandDispatch = (() => {
  const bridgeTargets = () => [
    window.chat,
    window.__openclaw?.chat,
    window.__OPENCLAW__?.chat,
    window.__APP__?.chat,
    window.app?.chat,
    window.__openclaw,
    window.__OPENCLAW__,
    window.__APP__,
    window.app
  ].filter(Boolean);

  const invokeBridgeMethod = async (methodNames, payload) => {
    for (const target of bridgeTargets()) {
      for (const methodName of methodNames) {
        const method = target?.[methodName];
        if (typeof method !== 'function') continue;

        try {
          const result = method.call(target, payload);
          if (result && typeof result.then === 'function') {
            await result;
          }
          return true;
        } catch {
        }
      }
    }

    return false;
  };

  const dispatchBridgeEvent = (command, payload) => {
    const detail = { command, payload };
    let dispatched = false;

    for (const target of [window, document]) {
      if (!target?.dispatchEvent) continue;
      target.dispatchEvent(new CustomEvent('openclaw:host-command', { detail }));
      target.dispatchEvent(new CustomEvent(`openclaw:${command}`, { detail }));
      dispatched = true;
    }

    return dispatched;
  };

  const runCommand = async (command, payload, methodNames, { inspectControlUi, postStatus, checkSessionReady }) => {
    const handled = await invokeBridgeMethod(methodNames, payload);
    if (!handled) {
      dispatchBridgeEvent(command, payload);
    }

    const snapshot = inspectControlUi();
    postStatus(snapshot);
    if (checkSessionReady) {
      checkSessionReady(snapshot);
    }

    return handled;
  };

  const createCommandHandler = ({ inspectControlUi, postStatus, checkSessionReady }) => {
    return async (message) => {
      const command = message?.command || '';
      const payload = message?.payload;

      switch (command) {
        case 'refresh_session':
          return await runCommand(
            command,
            payload,
            ['refreshSession', 'reloadSession', 'reconnect', 'connect', 'resume'],
            { inspectControlUi, postStatus, checkSessionReady });
        case 'fetch_recent_messages':
          return await runCommand(
            command,
            payload,
            ['fetchRecentMessages', 'loadRecentMessages', 'syncMessages', 'sync'],
            { inspectControlUi, postStatus });
        case 'lightweight_sync':
          return await runCommand(
            command,
            payload,
            ['sync', 'refresh', 'refreshSession', 'fetchRecentMessages', 'loadRecentMessages'],
            { inspectControlUi, postStatus, checkSessionReady });
        case 'reconnect_intent':
          return await runCommand(
            command,
            payload,
            ['reconnect', 'connect', 'resume', 'refreshSession'],
            { inspectControlUi, postStatus });
        default:
          dispatchBridgeEvent(command, payload);
          return false;
      }
    };
  };

  return { createCommandHandler, dispatchBridgeEvent, invokeBridgeMethod };
})();
