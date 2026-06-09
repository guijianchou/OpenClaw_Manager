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

  if (window.chat && typeof window.chat.abort === 'function') {
    window.chat.abort();
    return true;
  }

  const abortTargets = [
    window.__openclaw?.chat,
    window.__OPENCLAW__?.chat,
    window.__APP__?.chat,
    window.app?.chat
  ];

  for (const target of abortTargets) {
    if (target && typeof target.abort === 'function') {
      target.abort();
      return true;
    }
  }

  const findChatActionSurface = () => {
    const selectors = [
      'openclaw-app',
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

  const surface = findChatActionSurface();
  if (!surface) return false;

  const abortButton = Array.from(surface.querySelectorAll('button, [role="button"]'))
    .find((el) => isEnabledAction(el) && /\b(stop|abort|cancel)\b/i.test(labelOf(el)));

  if (abortButton) {
    abortButton.click();
    return true;
  }

  return false;
})()
