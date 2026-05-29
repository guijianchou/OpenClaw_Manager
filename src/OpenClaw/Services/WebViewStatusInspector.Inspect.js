(() => {
  if (!window.__openClawHostBridge || typeof window.__openClawHostBridge.inspect !== 'function') {
    return JSON.stringify({
      kind: 'openclaw-control-ui-status',
      phase: 'unavailable',
      summary: 'Control UI bridge unavailable.',
      detail: '',
      url: window.location ? window.location.href : '',
      shellDetected: false
    });
  }

  return JSON.stringify(window.__openClawHostBridge.inspect());
})()
