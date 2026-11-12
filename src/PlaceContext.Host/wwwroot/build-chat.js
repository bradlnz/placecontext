const recognitions = new Map();
export function supportsSpeech() {
  return window.isSecureContext && !!(window.SpeechRecognition || window.webkitSpeechRecognition);
}
export function stopSpeech(viewId) {
  const recognition = recognitions.get(viewId);
  if (recognition) {
    recognition.onresult = recognition.onerror = recognition.onend = null;
    recognition.abort();
    recognitions.delete(viewId);
  }
}
export function startSpeech(receiver, viewId) {
  stopSpeech(viewId);
  const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
  if (!supportsSpeech()) {
    throw new Error('Speech recognition is unavailable.');
  }
  const recognition = new SpeechRecognition();
  recognitions.set(viewId, recognition);
  recognition.lang = navigator.language;
  recognition.continuous = true;
  recognition.interimResults = false;
  const active = recognition;
  active.onresult = (event) => {
    for (let i = event.resultIndex; i < event.results.length; i++) {
      if (event.results[i].isFinal) {
        receiver
          .invokeMethodAsync('SpeechText', event.results[i][0].transcript)
          .catch(() => stopSpeech(viewId));
      }
    }
  };
  active.onerror = (event) => {
    const message =
      event.error === 'not-allowed'
        ? 'Microphone permission was denied.'
        : event.error === 'no-speech'
          ? 'No speech detected. Try dictating again.'
          : 'Dictation is unavailable. Check your browser speech service and microphone.';
    receiver.invokeMethodAsync('SpeechEnded', message).catch(() => {});
    stopSpeech(viewId);
  };
  active.onend = () => {
    recognitions.delete(viewId);
    receiver.invokeMethodAsync('SpeechEnded', null).catch(() => {});
  };
  active.start();
}

const chats = new Map();
export function mountWorkspaceResize(root) {
  const divider = root?.querySelector('.build-divider');
  const body = root?.querySelector('.build-body');
  if (!divider || !body) {
    return () => {};
  }
  let width = Number(divider.getAttribute('aria-valuenow')) || 50;
  let pointer = null;
  const resize = (value) => {
    width = Math.max(30, Math.min(70, value));
    root.style.setProperty('--chat-width', `${width}%`);
    divider.setAttribute('aria-valuenow', String(Math.round(width)));
  };
  const move = (event) => {
    if (event.pointerId !== pointer) {
      return;
    }
    const bounds = body.getBoundingClientRect();
    if (bounds.width) {
      resize(((event.clientX - bounds.left) / bounds.width) * 100);
    }
  };
  const start = (event) => {
    if (event.button !== 0 || pointer !== null) {
      return;
    }
    pointer = event.pointerId;
    root.classList.add('workspace-resizing');
    divider.setPointerCapture(pointer);
    divider.focus();
    event.preventDefault();
  };
  const stop = () => {
    if (pointer !== null && divider.hasPointerCapture(pointer)) {
      divider.releasePointerCapture(pointer);
    }
    pointer = null;
    root.classList.remove('workspace-resizing');
  };
  const key = (event) => {
    const value = { ArrowLeft: width - 2, ArrowRight: width + 2, Home: 30, End: 70 }[event.key];
    if (value === undefined) {
      return;
    }
    event.preventDefault();
    resize(value);
  };
  const handlers = {
    pointerdown: start,
    pointermove: move,
    pointerup: stop,
    pointercancel: stop,
    lostpointercapture: stop,
    keydown: key,
  };
  for (const [name, handler] of Object.entries(handlers)) {
    divider.addEventListener(name, handler);
  }
  return () => {
    for (const [name, handler] of Object.entries(handlers)) {
      divider.removeEventListener(name, handler);
    }
    stop();
  };
}

export function mountDashboardResize(root, receiver) {
  if (!root || !receiver) return () => {};
  let drag = null, saving = false;
  const shape = (handle) => {
    const tile = handle.closest('.dashboard-tile');
    const grid = getComputedStyle(tile.parentElement);
    const bounds = tile.getBoundingClientRect();
    const gapX = parseFloat(grid.columnGap) || 0, gapY = parseFloat(grid.rowGap) || 0;
    const stepX = parseFloat(grid.gridTemplateColumns) + gapX;
    const stepY = parseFloat(grid.gridAutoRows) + gapY;
    return { handle, tile, bounds, gapX, gapY, stepX, stepY,
      maxWidth: Math.min(2, grid.gridTemplateColumns.trim().split(/\s+/).length),
      width: Math.round((bounds.width + gapX) / stepX), height: Math.round((bounds.height + gapY) / stepY) };
  };
  const preview = (state, width, height) => {
    state.width = Math.max(1, Math.min(state.maxWidth, width));
    state.height = Math.max(1, Math.min(2, height));
    state.tile.style.gridColumn = `span ${state.width}`;
    state.tile.style.gridRow = `span ${state.height}`;
  };
  const clear = (state) => {
    state.tile.style.removeProperty('grid-column');
    state.tile.style.removeProperty('grid-row');
  };
  const save = async (state) => {
    const size = state.width === 2 ? (state.height === 2 ? 'large' : 'wide') : (state.height === 2 ? 'tall' : 'square');
    saving = true;
    try {
      if (size !== state.tile.dataset.tileSize)
        await receiver.invokeMethodAsync('ResizeDashboardTileAsync', state.tile.dataset.tile, size);
    } catch { /* The circuit may have closed; discard the local preview. */ }
    finally { saving = false; clear(state); }
  };
  const stop = (commit) => {
    if (!drag) return;
    const state = drag;
    drag = null;
    if (state.handle.hasPointerCapture(state.pointer)) state.handle.releasePointerCapture(state.pointer);
    root.classList.remove('dashboard-resizing');
    if (commit) void save(state); else clear(state);
  };
  const handlers = {
    pointerdown(event) {
      const handle = event.target.closest?.('.dashboard-resize');
      if (!handle || handle.disabled || event.button !== 0 || drag || saving) return;
      drag = { ...shape(handle), pointer: event.pointerId, x: event.clientX, y: event.clientY };
      handle.setPointerCapture(event.pointerId);
      handle.focus();
      root.classList.add('dashboard-resizing');
      event.preventDefault();
    },
    pointermove(event) {
      if (!drag || event.pointerId !== drag.pointer) return;
      preview(drag, Math.round((drag.bounds.width + event.clientX - drag.x + drag.gapX) / drag.stepX),
        Math.round((drag.bounds.height + event.clientY - drag.y + drag.gapY) / drag.stepY));
    },
    pointerup(event) { if (event.pointerId === drag?.pointer) stop(true); },
    pointercancel(event) { if (event.pointerId === drag?.pointer) stop(false); },
    lostpointercapture(event) { if (event.pointerId === drag?.pointer) stop(false); },
    dragstart(event) { if (drag) event.preventDefault(); },
    keydown(event) {
      if (event.key === 'Escape' && drag) { event.preventDefault(); stop(false); return; }
      const handle = event.target.closest?.('.dashboard-resize');
      const delta = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] }[event.key];
      if (!handle || handle.disabled || !delta || drag || saving) return;
      event.preventDefault();
      const state = shape(handle);
      preview(state, state.width + delta[0], state.height + delta[1]);
      void save(state);
    },
  };
  for (const [name, handler] of Object.entries(handlers)) root.addEventListener(name, handler);
  return () => {
    for (const [name, handler] of Object.entries(handlers)) root.removeEventListener(name, handler);
    stop(false);
  };
}

export function mountChat(viewId, receiver) {
  chats.get(viewId)?.();
  const root = viewId ? document.getElementById(viewId) : document;
  const messages = root?.querySelector('.chat-messages');
  const input = root?.querySelector('.chat-input');
  if (!messages || !input) {
    return;
  }
  const unmountResize = mountWorkspaceResize(root);
  const unmountDashboardResize = mountDashboardResize(root, receiver);
  let following = true;
  const onScroll = () => {
    following = messages.scrollHeight - messages.scrollTop - messages.clientHeight < 80;
  };
  const onKey = (event) => {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      if (input.form?.querySelector('button[type="submit"]:not(:disabled)')) {
        input.form.requestSubmit();
      }
    }
  };
  const observer = new MutationObserver((records) => {
    // Monaco repaints while scrolling or selecting; only chat content should advance the thread.
    if (
      following &&
      records.some((record) => {
        const element = record.target.nodeType === 1 ? record.target : record.target.parentElement;
        return !element?.closest('.run-output');
      })
    ) {
      messages.scrollTop = messages.scrollHeight;
    }
  });
  observer.observe(messages, { childList: true, subtree: true, characterData: true });
  messages.addEventListener('scroll', onScroll);
  input.addEventListener('keydown', onKey);
  chats.set(viewId, () => {
    unmountResize();
    unmountDashboardResize();
    observer.disconnect();
    messages.removeEventListener('scroll', onScroll);
    input.removeEventListener('keydown', onKey);
  });
}
export function unmountChat(viewId) {
  stopSpeech(viewId);
  chats.get(viewId)?.();
  chats.delete(viewId);
}
