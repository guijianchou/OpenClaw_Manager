const openClawHostMessaging = (() => {
  const KIND = 'openclaw-control-ui-status';
  const SESSION_READY_KIND = 'openclaw-session-ready';
  const GAP_KIND = 'openclaw-event-gap';

  const postHostMessage = (message) => {
    try {
      if (!window.chrome?.webview?.postMessage) return false;
      window.chrome.webview.postMessage(message);
      return true;
    } catch {
      return false;
    }
  };

  return { KIND, SESSION_READY_KIND, GAP_KIND, postHostMessage };
})();
