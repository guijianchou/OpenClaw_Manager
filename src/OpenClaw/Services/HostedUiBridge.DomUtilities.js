const openClawDomUtilities = (() => {
  const isVisible = (el) => {
    if (!el) return false;
    const style = window.getComputedStyle(el);
    if (style.display === 'none' || style.visibility === 'hidden') return false;
    const rect = el.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  };

  const textOf = (el) => {
    return [el?.innerText, el?.textContent]
      .filter(Boolean)
      .join(' ')
      .replace(/\s+/g, ' ')
      .trim();
  };

  const labelOf = (el) => {
    return [
      el?.getAttribute?.('aria-label'),
      el?.getAttribute?.('title'),
      textOf(el)
    ].filter(Boolean).join(' ').trim();
  };

  const isEditableElement = (el) => {
    if (!el) return false;
    if (el instanceof HTMLTextAreaElement) return true;
    if (el instanceof HTMLInputElement) {
      const type = (el.type || 'text').toLowerCase();
      return !['button', 'checkbox', 'color', 'file', 'hidden', 'image', 'radio', 'range', 'reset', 'submit'].includes(type);
    }

    const role = el.getAttribute?.('role') || '';
    return el.isContentEditable || role === 'textbox';
  };

  const compactText = (value) => (value == null ? '' : String(value)).replace(/\s+/g, ' ').trim();

  const readScalarText = (value) => {
    if (value == null) return '';
    return typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean'
      ? compactText(value)
      : '';
  };

  const readPath = (target, path) => {
    return path.reduce((current, key) => current == null ? undefined : current[key], target);
  };

  const readFirstPath = (target, paths) => {
    if (!target || typeof target !== 'object') return '';
    for (const path of paths) {
      const text = readScalarText(readPath(target, path));
      if (text) return text;
    }

    return '';
  };

  const uniqueObjects = (items) => {
    const seen = new Set();
    return items.filter((item) => {
      if (!item || typeof item !== 'object' || seen.has(item)) return false;
      seen.add(item);
      return true;
    });
  };

  return {
    isVisible,
    textOf,
    labelOf,
    isEditableElement,
    compactText,
    readScalarText,
    readPath,
    readFirstPath,
    uniqueObjects
  };
})();
