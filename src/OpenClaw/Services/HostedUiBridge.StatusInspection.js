const openClawStatusInspection = (() => {
  const createInspector = ({ strings, mutationFilter, modelResolver, statusKind }) => {
    const dom = openClawDomUtilities;
    const modelReader = openClawModelDomFallback.createReader({ dom, mutationFilter, modelResolver });
    const activity = openClawActivityState.createActivityTracker({ dom, mutationFilter });
    const phaseClassifier = openClawPhaseClassifier.createClassifier({ dom, mutationFilter, strings });
    const isStatusProbeExcludedElement = mutationFilter.isStatusProbeExcludedElement;

    const hasVisibleElement = (selector, predicate) => {
      return Array.from(document.querySelectorAll(selector))
        .some((el) => !isStatusProbeExcludedElement(el) && dom.isVisible(el) && (!predicate || predicate(el)));
    };

    const readFocusedInputState = () => {
      const activeElement = document.activeElement;
      const inputFocused = dom.isEditableElement(activeElement) && dom.isVisible(activeElement);
      const focusedInputHasText = inputFocused && dom.compactText(
        activeElement instanceof HTMLInputElement || activeElement instanceof HTMLTextAreaElement
          ? activeElement.value
          : activeElement?.textContent || '').length > 0;

      return { inputFocused, focusedInputHasText };
    };

    const detectShellFromDom = () => {
      return (
        hasVisibleElement('textarea, input:not([type]), input[type="text"], [contenteditable="true"], [role="textbox"]') ||
        hasVisibleElement('button, [role="button"], nav, aside, [role="navigation"]', (el) => {
          const label = dom.labelOf(el).toLowerCase();
          return /stop|abort|dashboard|settings|sessions|workers|models|new chat|history/.test(label);
        }));
    };

    const detectBusyFromDom = (needsDomSignals) => {
      const busyByButton = needsDomSignals && hasVisibleElement('button, [role="button"], [aria-label], [title]', (el) => {
        const label = dom.labelOf(el).toLowerCase();
        return /\b(stop|abort|cancel)\b/.test(label);
      });
      const busyBySignals = needsDomSignals && hasVisibleElement(
        '[aria-busy="true"], [role="progressbar"], [data-busy="true"], [data-running="true"], [data-state="running"], [data-state="streaming"], [data-status="running"], [data-status="streaming"]');
      return busyByButton || busyBySignals;
    };

    const inspectControlUi = () => {
      const url = window.location ? window.location.href : '';
      const lowerUrl = url.toLowerCase();
      const appState = activity.readOpenClawAppStateStatus();
      const needsDomSignals = !appState || !appState.connected || Boolean(appState.lastError);
      const text = needsDomSignals ? phaseClassifier.collectSignalText() : '';
      const { inputFocused, focusedInputHasText } = readFocusedInputState();
      const shellDetected = appState?.shellDetected || (needsDomSignals && detectShellFromDom());
      const apiBusy = activity.detectBusyFromApi();
      const domBusy = detectBusyFromDom(needsDomSignals);
      const isBusy = Boolean(appState?.isBusy) || apiBusy || domBusy;
      const isBusyStaleCandidate = Boolean(appState?.isChatBusy) || apiBusy || domBusy;
      const workState = isBusy ? 'busy' : shellDetected ? 'idle' : 'unknown';
      const activitySignature = [
        appState?.activitySignature,
        isBusy ? activity.collectDomActivitySignature() : ''
      ].filter(Boolean).join('|').slice(0, 512);
      const modelSnapshot = modelReader.readCurrentModel();
      const phaseSnapshot = phaseClassifier.classify({
        documentReadyState: document.readyState,
        hasBody: Boolean(document.body),
        text,
        lowerUrl,
        shellDetected
      });

      return activity.applyBusyStaleness({
        kind: statusKind,
        ...phaseSnapshot,
        url,
        shellDetected,
        isBusy,
        isBusyStaleCandidate,
        inputFocused,
        focusedInputHasText,
        workState,
        activitySignature,
        currentModel: modelSnapshot.value,
        currentModelSource: modelSnapshot.source
      });
    };

    return {
      inspectControlUi,
      isEditableElement: dom.isEditableElement,
      compactText: dom.compactText
    };
  };

  return { createInspector };
})();
