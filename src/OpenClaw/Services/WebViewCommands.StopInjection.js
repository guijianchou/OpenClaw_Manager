(() => {
  const stopCommand = '/stop';

  const isVisible = (el) => {
    if (!el) return false;
    const style = window.getComputedStyle(el);
    if (style.display === 'none' || style.visibility === 'hidden') return false;
    const rect = el.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  };

  const clearElement = (el) => {
    if (!el) return;

    if ('value' in el) {
      const prototype = Object.getPrototypeOf(el);
      const descriptor = Object.getOwnPropertyDescriptor(prototype, 'value');
      if (descriptor && typeof descriptor.set === 'function') {
        descriptor.set.call(el, '');
      } else {
        el.value = '';
      }
      el.dispatchEvent(new Event('input', { bubbles: true }));
      el.dispatchEvent(new Event('change', { bubbles: true }));
      return;
    }

    el.textContent = '';
    el.dispatchEvent(new InputEvent('input', { bubbles: true, data: '', inputType: 'deleteContentBackward' }));
  };

  const submitElement = (el) => {
    if (!el) return false;
    const form = el.closest('form');
    if (form) {
      const submitEvent = new Event('submit', { bubbles: true, cancelable: true });
      form.dispatchEvent(submitEvent);
      if (typeof form.requestSubmit === 'function') {
        form.requestSubmit();
      } else if (typeof form.submit === 'function') {
        form.submit();
      }
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

  const setNativeValue = (el, value) => {
    const prototype = Object.getPrototypeOf(el);
    const descriptor = Object.getOwnPropertyDescriptor(prototype, 'value');
    if (descriptor && typeof descriptor.set === 'function') {
      descriptor.set.call(el, value);
    } else {
      el.value = value;
    }
  };

  const textInput = Array.from(document.querySelectorAll('textarea, input[type="text"], input:not([type])'))
    .find((el) => !el.disabled && !el.readOnly && isVisible(el));

  if (textInput) {
    textInput.focus();
    setNativeValue(textInput, stopCommand);
    textInput.dispatchEvent(new Event('input', { bubbles: true }));
    textInput.dispatchEvent(new Event('change', { bubbles: true }));
    return submitElement(textInput);
  }

  const editor = Array.from(document.querySelectorAll('[contenteditable="true"], [role="textbox"]'))
    .find((el) => !el.hasAttribute('disabled') && isVisible(el));

  if (editor) {
    editor.focus();
    editor.textContent = stopCommand;
    editor.dispatchEvent(new InputEvent('input', { bubbles: true, data: stopCommand, inputType: 'insertText' }));
    return submitElement(editor);
  }

  return false;
})()
