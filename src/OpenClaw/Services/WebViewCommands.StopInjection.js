(async () => {
  const stopCommand = '/stop';

  const invokeMaybeAsync = async (target, methodName, ...args) => {
    const method = target?.[methodName];
    if (typeof method !== 'function') return false;

    const result = method.call(target, ...args);
    if (result && typeof result.then === 'function') {
      await result;
    }

    return result !== false;
  };

  const chatTargets = () => [
    window.chat,
    window.__openclaw?.chat,
    window.__OPENCLAW__?.chat,
    window.__APP__?.chat,
    window.app?.chat
  ].filter(Boolean);

  for (const chat of chatTargets()) {
    if (await invokeMaybeAsync(chat, 'inject', stopCommand)) return true;
    if (await invokeMaybeAsync(chat, 'send', stopCommand)) return true;
    if (await invokeMaybeAsync(chat, 'sendMessage', stopCommand)) return true;
  }

  const isVisible = (el) => {
    if (!el) return false;
    const style = window.getComputedStyle(el);
    if (style.display === 'none' || style.visibility === 'hidden') return false;
    const rect = el.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  };

  const setNativeValue = (el, value) => {
    const prototype = Object.getPrototypeOf(el);
    const descriptor = Object.getOwnPropertyDescriptor(prototype, 'value');
    if (descriptor && typeof descriptor.set === 'function') {
      descriptor.set.call(el, value);
    } else {
      el.value = value;
    }
  };

  const clearElement = (el) => {
    if (!el) return;

    if ('value' in el) {
      setNativeValue(el, '');
      el.dispatchEvent(new Event('input', { bubbles: true }));
      el.dispatchEvent(new Event('change', { bubbles: true }));
      return;
    }

    el.textContent = '';
    el.dispatchEvent(new InputEvent('input', { bubbles: true, data: '', inputType: 'deleteContentBackward' }));
  };

  const findChatSurface = () => {
    const selectors = [
      'openclaw-app',
      '[data-openclaw-chat]',
      '[data-openclaw-chat-surface]',
      '[data-testid="chat"]',
      '[data-testid*="chat"]',
      '[data-testid*="conversation"]',
      '[aria-label*="chat" i]'
    ];

    return selectors
      .flatMap((selector) => Array.from(document.querySelectorAll(selector)))
      .find(isVisible) || null;
  };

  const findChatComposer = () => {
    const surface = findChatSurface();
    if (!surface) return null;

    const selectors = [
      '[data-openclaw-chat-composer]',
      '[data-testid*="composer"]',
      '[data-testid*="prompt"]',
      '[aria-label*="message" i]',
      '[aria-label*="prompt" i]',
      'form'
    ];
    const scopes = selectors
      .flatMap((selector) => Array.from(surface.querySelectorAll(selector)))
      .filter(isVisible);
    scopes.unshift(surface);

    const inputSelectors = [
      'textarea',
      'input[type="text"]',
      'input:not([type])',
      '[contenteditable="true"]',
      '[role="textbox"]'
    ];

    for (const scope of scopes) {
      const input = inputSelectors
        .flatMap((selector) => Array.from(scope.querySelectorAll(selector)))
        .find((el) =>
          !el.disabled &&
          !el.readOnly &&
          !el.hasAttribute('disabled') &&
          isVisible(el));
      if (input) return input;
    }

    return null;
  };

  const submitElement = (el) => {
    if (!el) return false;
    const form = el.closest('form');
    if (form) {
      if (typeof form.requestSubmit !== 'function') return false;

      const submitter = Array.from(form.querySelectorAll('button, input[type="submit"]'))
        .find((button) => !button.disabled && !button.hasAttribute('disabled') && isVisible(button));
      form.requestSubmit(submitter || undefined);
      window.setTimeout(() => clearElement(el), 0);
      return true;
    }

    const keyboardEventInit = {
      key: 'Enter',
      code: 'Enter',
      keyCode: 13,
      which: 13,
      bubbles: true,
      cancelable: true
    };

    el.dispatchEvent(new KeyboardEvent('keydown', keyboardEventInit));
    el.dispatchEvent(new KeyboardEvent('keypress', keyboardEventInit));
    el.dispatchEvent(new KeyboardEvent('keyup', keyboardEventInit));
    window.setTimeout(() => clearElement(el), 0);
    return true;
  };

  const composer = findChatComposer();
  if (!composer) return false;

  composer.focus();
  if ('value' in composer) {
    setNativeValue(composer, stopCommand);
    composer.dispatchEvent(new Event('input', { bubbles: true }));
    composer.dispatchEvent(new Event('change', { bubbles: true }));
  } else {
    composer.textContent = stopCommand;
    composer.dispatchEvent(new InputEvent('input', { bubbles: true, data: stopCommand, inputType: 'insertText' }));
  }

  return submitElement(composer);
})()
