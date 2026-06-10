(() => {
  const isVisible = (el) => {
    if (!el) return false;
    const style = window.getComputedStyle(el);
    if (style.display === 'none' || style.visibility === 'hidden') return false;
    const rect = el.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  };

  const labelOf = (el) => [
    el?.getAttribute?.('aria-label'),
    el?.getAttribute?.('title'),
    el?.innerText,
    el?.textContent
  ].filter(Boolean).join(' ').trim();

  const abortTargets = [
    window.chat,
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

  const abortButton = Array.from(document.querySelectorAll('button, [role="button"], [aria-label], [title]'))
    .find((el) => isVisible(el) && /\b(stop|abort)\b/i.test(labelOf(el)));

  if (abortButton) {
    abortButton.click();
    return true;
  }

  return false;
})()
