(() => {
  const isVisible = (el) => {
    if (!el) return false;
    const style = window.getComputedStyle(el);
    if (style.display === 'none' || style.visibility === 'hidden') return false;
    const rect = el.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  };

  const isEnabledAction = (el) => {
    return !el.disabled &&
      !el.hasAttribute('disabled') &&
      el.getAttribute('aria-disabled') !== 'true' &&
      isVisible(el);
  };

  const labelOf = (el) => [
    el?.getAttribute?.('aria-label'),
    el?.getAttribute?.('title'),
    el?.innerText,
    el?.textContent
  ].filter(Boolean).join(' ').trim();

  const findChatActionSurface = () => {
    const selectors = [
      '[data-openclaw-chat]',
      '[data-openclaw-chat-surface]',
      '[data-testid="chat"]',
      '[data-testid*="chat"]',
      '[data-testid*="run"]',
      '[data-testid*="conversation"]',
      '[aria-label*="chat" i]'
    ];

    return selectors
      .flatMap((selector) => Array.from(document.querySelectorAll(selector)))
      .find(isVisible) || null;
  };

  const clickAbortButton = () => {
    const surface = findChatActionSurface();
    if (!surface) return false;

    const abortButton = Array.from(surface.querySelectorAll('button, [role="button"]'))
      .find((el) => isEnabledAction(el) && /\b(stop|abort|cancel)\b/i.test(labelOf(el)));

    if (!abortButton) return false;

    abortButton.click();
    return true;
  };

  const abortTargets = [
    window.chat,
    window.__openclaw?.chat,
    window.__OPENCLAW__?.chat,
    window.__APP__?.chat,
    window.app?.chat
  ];

  const tryAbortTarget = (index) => {
    if (index >= abortTargets.length) {
      return clickAbortButton();
    }

    const target = abortTargets[index];
    if (!target || typeof target.abort !== 'function') {
      return tryAbortTarget(index + 1);
    }

    try {
      const result = target.abort();
      if (result && typeof result.then === 'function') {
        result.then(
          (resolved) => {
            if (resolved === false) tryAbortTarget(index + 1);
          },
          () => tryAbortTarget(index + 1));
        return true;
      }

      return result !== false || tryAbortTarget(index + 1);
    } catch {
      return tryAbortTarget(index + 1);
    }
  };

  return tryAbortTarget(0);
})()
