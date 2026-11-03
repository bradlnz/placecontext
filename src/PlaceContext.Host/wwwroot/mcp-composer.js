const mentions = /(?<![\p{L}\p{N}_@])@(?:"(?:[^"\\\r\n]|\\.)*"|[\p{L}\p{N}_-]+)/gu;

export function mentionParts(text) {
  const parts = [];
  let start = 0;
  for (const match of text.matchAll(mentions)) {
    parts.push({ text: text.slice(start, match.index) });
    const raw = match[0];
    try {
      const name = raw[1] === '"' ? JSON.parse(raw.slice(1)) : raw.slice(1);
      parts.push({ text: '@' + name, raw });
    } catch {
      parts.push({ text: raw });
    }
    start = match.index + raw.length;
  }
  parts.push({ text: text.slice(start) });
  return parts;
}

function rawText(node) {
  if (node.nodeType === 3) {
    return node.textContent;
  }
  if (node.dataset?.mention) {
    return node.dataset.mention;
  }
  if (node.nodeName === 'BR') {
    return '\n';
  }
  return [...node.childNodes].map(rawText).join('');
}

function caretOffset(editor) {
  const selection = window.getSelection();
  if (!selection.rangeCount || !editor.contains(selection.focusNode)) {
    return rawText(editor).length;
  }
  const range = document.createRange();
  range.selectNodeContents(editor);
  range.setEnd(selection.focusNode, selection.focusOffset);
  return rawText(range.cloneContents()).length;
}

function setCaret(editor, offset) {
  const range = document.createRange();
  range.selectNodeContents(editor);
  range.collapse(false);
  let remaining = offset;
  for (const node of editor.childNodes) {
    const length = rawText(node).length;
    if (remaining <= length) {
      if (node.nodeType === 3) {
        range.setStart(node, Math.max(0, remaining));
      }
      if (node.nodeType !== 3) {
        range.setStartAfter(node);
        if (remaining === 0) {
          range.setStartBefore(node);
        }
      }
      range.collapse(true);
      break;
    }
    remaining -= length;
  }
  const selection = window.getSelection();
  selection.removeAllRanges();
  selection.addRange(range);
}

export function mountMcpComposer(root, source) {
  const editor = root?.querySelector('.chat-editor');
  if (!editor) {
    return { element: source, dispose() {} };
  }
  function sync(force = false) {
    if (!force && rawText(editor) === source.value) {
      return;
    }
    const focused = document.activeElement === editor;
    const caret = caretOffset(editor);
    const nodes = mentionParts(source.value).map(part => {
      if (!part.raw) {
        return document.createTextNode(part.text);
      }
      const token = document.createElement('strong');
      token.dataset.mention = part.raw;
      token.contentEditable = 'false';
      token.textContent = part.text;
      return token;
    });
    editor.replaceChildren(...nodes);
    if (focused) {
      setCaret(editor, caret);
    }
  }
  function selection() {
    const caret = caretOffset(editor);
    source.setSelectionRange(caret, caret);
  }
  function input(event) {
    if (source.disabled || event?.isComposing) {
      return;
    }
    const value = rawText(editor);
    if (value.length > source.maxLength) {
      sync();
      return;
    }
    source.value = value;
    selection();
    source.dispatchEvent(new Event('input', { bubbles: true }));
  }
  function insert(text) {
    if (source.disabled) {
      return;
    }
    const selection = window.getSelection();
    if (!selection.rangeCount || !editor.contains(selection.focusNode)) {
      return;
    }
    const range = selection.getRangeAt(0);
    const removed = rawText(range.cloneContents()).length;
    if (rawText(editor).length - removed + text.length > source.maxLength) {
      return;
    }
    range.deleteContents();
    const node = document.createTextNode(text);
    range.insertNode(node);
    range.setStart(node, text.length);
    range.collapse(true);
    selection.removeAllRanges();
    selection.addRange(range);
    input();
  }
  function beforeInput(event) {
    if (event.inputType === 'insertText' && !event.isComposing && typeof event.data === 'string') {
      event.preventDefault();
      insert(event.data);
      return;
    }
    if (event.inputType === 'insertParagraph' || event.inputType === 'insertLineBreak') {
      event.preventDefault();
      insert('\n');
    }
  }
  function paste(event) {
    event.preventDefault();
    insert(event.clipboardData.getData('text/plain'));
  }
  function drop(event) {
    event.preventDefault();
  }
  function click() {
    selection();
    source.dispatchEvent(new Event('click', { bubbles: true }));
  }
  const observer = new MutationObserver(() => sync());
  observer.observe(source, { attributes: true, attributeFilter: ['data-draft'] });
  const events = { input, compositionend: input, beforeinput: beforeInput, paste, drop, click, keydown: selection, keyup: selection };
  for (const [name, handler] of Object.entries(events)) {
    editor.addEventListener(name, handler);
  }
  sync();
  return {
    element: editor,
    focus(caret) { sync(true); editor.focus(); setCaret(editor, caret); },
    dispose() {
      observer.disconnect();
      for (const [name, handler] of Object.entries(events)) {
        editor.removeEventListener(name, handler);
      }
    },
  };
}
