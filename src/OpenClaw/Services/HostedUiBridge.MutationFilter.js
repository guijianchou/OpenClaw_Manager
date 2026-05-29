const openClawMutationFilter = (() => {
  const STATUS_PROBE_EXCLUDED_SELECTOR = '.chat-sidebar, .sidebar-panel, .sidebar-content, .chat-tool-card__preview-frame, .settings-workspace__body, .config-content, .config-form, .config-section-card, .cron-summary-strip, .cron-workspace';

  const asElement = (node) => {
    if (!node) return null;
    if (node.nodeType === 1) return node;
    return node.parentElement || null;
  };

  const isStatusProbeExcludedElement = (el) => Boolean(el?.closest?.(STATUS_PROBE_EXCLUDED_SELECTOR));

  const isStatusRelevantMutation = (mutation) => {
    const target = asElement(mutation.target);
    if (!target || isStatusProbeExcludedElement(target)) return false;
    if (mutation.type === 'childList') return true;
    if (mutation.type !== 'attributes') return false;
    return ['aria-busy', 'data-busy', 'data-running', 'data-state', 'data-status', 'aria-label', 'title']
      .includes(mutation.attributeName || '');
  };

  const isStatusRelevantEventTarget = (target) => {
    const element = asElement(target);
    return Boolean(element) && !isStatusProbeExcludedElement(element);
  };

  return { asElement, isStatusProbeExcludedElement, isStatusRelevantMutation, isStatusRelevantEventTarget };
})();
