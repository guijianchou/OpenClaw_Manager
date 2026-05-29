const openClawModelDomFallback = (() => {
  const modelPattern = /\b(?:gpt|o\d|claude|gemini|qwen|deepseek|llama|mistral|glm|yi|command|grok|codex|kimi|moonshot)[a-z0-9._:+/-]*\b/i;

  const createReader = ({ dom, mutationFilter, modelResolver }) => {
    const isStatusProbeExcludedElement = mutationFilter.isStatusProbeExcludedElement;

    const sanitizeModelLabel = (text) => {
      const normalized = dom.compactText(text)
        .replace(/\b(?:current|selected|default)\s+model\b[:\s-]*/ig, '')
        .replace(/\bmodel\b[:\s-]*/ig, '')
        .replace(/\bprovider\b[:\s-]*/ig, '')
        .replace(/\s+\|\s+/g, ' | ')
        .trim();

      if (!normalized) return '';

      const defaultWrappedModel = normalized.match(/^(?:default|selected|current)(?:\s+model)?\s*\(([^)]+)\)$/i);
      if (defaultWrappedModel) {
        return sanitizeModelLabel(defaultWrappedModel[1]);
      }

      const prefixedModel = normalized
        .replace(/^(?:default|selected|current)(?:\s+model)?\s*[:\s-]+/i, '')
        .replace(/^\(([^)]+)\)$/, '$1')
        .trim();
      if (prefixedModel && prefixedModel !== normalized) {
        return sanitizeModelLabel(prefixedModel);
      }

      if (normalized.length <= 32 && modelPattern.test(normalized)) return normalized;

      const segment = normalized
        .split(/(?:\s{3,}|\n|\||,)/)
        .map((part) => dom.compactText(part))
        .find((part) => modelPattern.test(part));

      if (segment) return segment.length <= 32 ? segment : segment.slice(0, 31).trimEnd();
      const match = normalized.match(modelPattern);
      return match ? match[0] : '';
    };

    const cleanTrustedModelValue = (value) => dom.compactText(value).slice(0, 96);
    const emptyModelResult = () => ({ value: '', source: '' });
    const modelResult = (value, source) => {
      return { value: cleanTrustedModelValue(value), source };
    };
    const SESSION_KEY_PATHS = [
      ['sessionKey'],
      ['activeSessionKey'],
      ['currentSessionKey'],
      ['chatSessionKey'],
      ['session', 'key'],
      ['settings', 'sessionKey']
    ];
    const APP_STATE_PATHS = [
      ['state'],
      ['appState'],
      ['store', 'state'],
      ['controller', 'state'],
      ['chatState'],
      ['sessionState']
    ];

    const readSessionKeyFromUrl = () => {
      try {
        return dom.compactText(new URL(window.location.href).searchParams.get('session') || '');
      } catch {
        return '';
      }
    };

    const readOpenClawStateCandidates = () => {
      const app = document.querySelector('openclaw-app');
      if (!app) return [];
      return dom.uniqueObjects([
        app,
        ...APP_STATE_PATHS
          .map((path) => dom.readPath(app, path))
          .filter((value) => value && typeof value === 'object')
      ]);
    };

    const readCurrentSessionKey = (states) => {
      for (const state of states) {
        const key = dom.readFirstPath(state, SESSION_KEY_PATHS);
        if (key) return key;
      }

      return readSessionKeyFromUrl();
    };

    const readOpenClawAppStateModel = () => {
      const states = readOpenClawStateCandidates();
      if (states.length === 0) return emptyModelResult();

      return modelResolver.resolveOpenClawAppStateModel(states, readCurrentSessionKey(states));
    };

    const readOpenClawModelSelect = () => {
      const select = document.querySelector('select[data-chat-model-select="true"], select[data-chat-model-select]');
      if (!(select instanceof HTMLSelectElement) || !dom.isVisible(select)) return emptyModelResult();

      const selectedOption = select.selectedOptions?.[0] || null;
      const selectedModelOptionValue = dom.compactText(selectedOption?.value || '');
      const selectedModelValue = dom.compactText(select.value || '');
      const selectedModelTitle = dom.compactText(select.getAttribute('title') || '');
      const selectedModelText = dom.compactText(selectedOption?.textContent || '');

      for (const value of [
        selectedModelOptionValue,
        selectedModelValue,
        selectedModelTitle,
        selectedModelText
      ]) {
        const label = sanitizeModelLabel(value);
        if (label) return modelResult(label, 'model-select');
      }

      return emptyModelResult();
    };

    const readModelFromDomCandidates = () => {
      const candidates = [];
      const selectionBoostOf = (el) => {
        const selected = [
          el?.getAttribute?.('aria-selected'),
          el?.getAttribute?.('aria-checked'),
          el?.getAttribute?.('aria-pressed'),
          el?.getAttribute?.('data-selected'),
          el?.getAttribute?.('data-state')
        ].filter(Boolean).join(' ').toLowerCase();
        return /true|selected|checked|active|current/.test(selected) ? 18 : 0;
      };

      const viewportBoostOf = (el) => {
        if (!el || typeof el.getBoundingClientRect !== 'function') return 0;
        const top = el.getBoundingClientRect().top;
        return Number.isFinite(top) && top >= 0 && top <= 260 ? 8 : 0;
      };

      const pushCandidate = (text, score, el, source) => {
        const label = sanitizeModelLabel(text);
        if (!label) return;
        candidates.push({ label, source, score: score + selectionBoostOf(el) + viewportBoostOf(el) });
      };

      Array.from(document.querySelectorAll('[data-current-model], [data-selected-model], [data-model-name]'))
        .filter((el) => !isStatusProbeExcludedElement(el) && dom.isVisible(el))
        .forEach((el) => pushCandidate(dom.textOf(el), 120, el, 'dom:data-model'));

      Array.from(document.querySelectorAll('select'))
        .filter((el) => !isStatusProbeExcludedElement(el) && dom.isVisible(el))
        .forEach((el) => {
          const selectedText = Array.from(el.selectedOptions || [])
            .map((option) => option.textContent || '')
            .join(' ');
          const combined = `${dom.labelOf(el)} ${selectedText}`.trim();
          if (/\bmodel\b/i.test(combined) || modelPattern.test(selectedText)) {
            pushCandidate(selectedText || combined, /\bmodel\b/i.test(combined) ? 115 : 90, el, 'dom:select');
          }
        });

      Array.from(document.querySelectorAll('[role="combobox"], button[aria-haspopup="listbox"], button, [role="button"], input[type="text"], input:not([type])'))
        .filter((el) => !isStatusProbeExcludedElement(el) && dom.isVisible(el))
        .forEach((el) => {
          const rawValue = 'value' in el && typeof el.value === 'string' ? el.value : '';
          const combined = [dom.labelOf(el), rawValue, el.getAttribute?.('placeholder')].filter(Boolean).join(' ').trim();
          if (!/\bmodel\b/i.test(combined) && !modelPattern.test(rawValue) && !modelPattern.test(dom.textOf(el))) return;
          const score = /\bmodel\b/i.test(combined) ? 105 : 80;
          pushCandidate(rawValue || dom.textOf(el) || combined, score, el, 'dom:control');
        });

      if (candidates.length === 0) return emptyModelResult();
      candidates.sort((left, right) => {
        if (right.score !== left.score) return right.score - left.score;
        return left.label.length - right.label.length;
      });
      return modelResult(candidates[0].label, candidates[0].source);
    };

    const MODEL_SOURCE_READERS = [
      readOpenClawAppStateModel,
      readOpenClawModelSelect,
      readModelFromDomCandidates
    ];

    const readCurrentModel = () => {
      for (const readModel of MODEL_SOURCE_READERS) {
        const result = readModel();
        if (result?.value) return result;
      }

      return emptyModelResult();
    };

    return {
      readCurrentModel,
      readOpenClawAppStateModel,
      readOpenClawModelSelect,
      readModelFromDomCandidates,
      sanitizeModelLabel
    };
  };

  return { createReader };
})();
