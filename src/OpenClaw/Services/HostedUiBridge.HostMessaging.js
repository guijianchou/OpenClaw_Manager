const openClawHostMessaging = (() => {
  const KIND = 'openclaw-control-ui-status';
  const SESSION_READY_KIND = 'openclaw-session-ready';
  const GAP_KIND = 'openclaw-event-gap';
  let ownerToken = '';
  const pageToken = (globalThis.crypto && typeof globalThis.crypto.randomUUID === 'function')
    ? globalThis.crypto.randomUUID()
    : `${Date.now()}-${Math.random().toString(16).slice(2)}`;

  const setOwnerToken = (value) => {
    ownerToken = typeof value === 'string' ? value : '';
  };

  const attachOwnership = (message) => ({
    ...message,
    nativeOwnerToken: ownerToken,
    nativePageToken: pageToken
  });

  const postHostMessage = (message) => {
    try {
      if (!window.chrome?.webview?.postMessage) return false;
      window.chrome.webview.postMessage(attachOwnership(message));
      return true;
    } catch {
      return false;
    }
  };

  return { KIND, SESSION_READY_KIND, GAP_KIND, pageToken, setOwnerToken, postHostMessage };
})();
