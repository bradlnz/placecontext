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

// Resolve collisions from saved cells, never from rectangles changed by the drag preview.
export function dashboardPositions(items, columns, moving = null) {
  const occupied = new Set(), result = new Map();
  const place = (item, preferred) => {
    const width = Math.min(item.width, columns), height = item.height;
    let column = Math.max(1, Math.min(preferred?.column || 1, columns - width + 1));
    let row = Math.max(1, preferred?.row || 1);
    const fits = () => {
      if (column + width - 1 > columns) return false;
      for (let y = row; y < row + height; y++) for (let x = column; x < column + width; x++)
        if (occupied.has(`${x},${y}`)) return false;
      return true;
    };
    while (!fits()) {
      if (preferred || column + width > columns) { row++; column = preferred ? column : 1; }
      else column++;
    }
    for (let y = row; y < row + height; y++) for (let x = column; x < column + width; x++) occupied.add(`${x},${y}`);
    result.set(item.key, { column, row });
  };
  if (moving) place(items.find(item => item.key === moving.key), moving);
  for (const item of items) if (item.key !== moving?.key && item.column && item.row) place(item, item);
  for (const item of items) if (!result.has(item.key)) {
    if (item.rowStart) place(item, { column: 1, row: Math.max(1, ...[...result.values()].map(p => p.row + 1)) });
    else place(item, null);
  }
  return result;
}

export function mountDashboardMove(root, receiver) {
  if (!root || !receiver) return () => {};
  let drag = null, saving = false, frame = 0;
  const announce = (text) => {
    const status = root.querySelector('.dashboard-layout-status');
    if (status) status.textContent = text;
  };
  const metrics = grid => {
    const style = getComputedStyle(grid), gap = parseFloat(style.columnGap) || 0;
    // Saved desktop positions can create implicit columns after the panel narrows.
    const columns = Math.max(1, Math.floor((grid.clientWidth + gap) / (parseFloat(style.getPropertyValue('--tile-min')) + gap)));
    return { columns, stepX: (grid.clientWidth + gap) / columns, stepY: parseFloat(style.gridAutoRows) + (parseFloat(style.rowGap) || 0) };
  };
  const arrange = (grid, moving = null) => {
    const tiles = [...grid.querySelectorAll('.dashboard-tile')];
    const cells = dashboardPositions(tiles.map(tile => ({ key: tile.dataset.tile,
      column: Number(tile.dataset.tileColumn), row: Number(tile.dataset.tileRow), rowStart: tile.classList.contains('tile-row-start'),
      width: ['wide', 'large'].includes(tile.dataset.tileSize) ? 2 : 1,
      height: ['tall', 'large'].includes(tile.dataset.tileSize) ? 2 : 1 })), metrics(grid).columns, moving);
    for (const tile of tiles) {
      const cell = cells.get(tile.dataset.tile);
      tile.style.gridColumnStart = cell.column;
      tile.style.gridRowStart = cell.row;
    }
    return cells;
  };
  const refresh = () => { if (!drag && !saving) root.querySelectorAll('.dashboard-actions').forEach(grid => arrange(grid)); };
  const observer = new MutationObserver(refresh);
  observer.observe(root, { childList: true, subtree: true, attributes: true, attributeFilter: ['data-tile-size', 'data-tile-column', 'data-tile-row'] });
  const resizeObserver = new ResizeObserver(refresh);
  resizeObserver.observe(root.querySelector('.chat-content') || root);
  refresh();
  const clear = (state) => {
    for (const tile of state.tiles) tile.style.removeProperty('order');
    arrange(state.grid);
    state.tile.classList.remove('tile-dragging');
    state.ghost?.remove();
    root.classList.remove('dashboard-dragging');
  };
  const save = async (tile, position, newRow = false, cleanup = () => {}, cell = null) => {
    saving = true;
    announce('Saving layout…');
    try {
      const positions = cell ? Object.fromEntries(arrange(tile.parentElement, { ...cell, key: tile.dataset.tile })) : null;
      const saved = await receiver.invokeMethodAsync('MoveDashboardTileAsync', tile.dataset.tile, position, newRow, cell?.column ?? null, cell?.row ?? null, positions);
      announce(saved === false ? 'Layout was not saved. Please try again.' : cell ? `Tile moved to column ${cell.column}, row ${cell.row}. Layout saved.` : `Tile moved to position ${position + 1}. Layout saved.`);
    } catch {
      announce('Layout was not saved. Check your connection and try again.');
    } finally {
      saving = false;
      cleanup();
      refresh();
      if (tile.isConnected) tile.querySelector('.dashboard-drag-handle')?.focus({ preventScroll: true });
    }
  };
  const stop = (commit) => {
    if (!drag) return;
    const state = drag;
    drag = null;
    cancelAnimationFrame(frame);
    if (state.handle.hasPointerCapture(state.pointer)) state.handle.releasePointerCapture(state.pointer);
    const index = state.order.indexOf(state.tile);
    if (commit && state.tile.isConnected && state.active && state.valid && state.cell && (state.cell.column !== state.original.column || state.cell.row !== state.original.row)) {
      state.ghost.remove();
      state.tile.classList.remove('tile-dragging');
      void save(state.tile, index, false, () => clear(state), state.cell);
    } else {
      clear(state);
      if (state.active) announce('Layout unchanged.');
    }
  };
  const preview = () => {
    if (!drag?.active) return;
    const state = drag;
    state.ghost.style.transform = `translate(${state.x - state.startX}px, ${state.y - state.startY}px)`;
    const hit = document.elementFromPoint(state.x, state.y);
    state.valid = state.grid.contains(hit);
    if (!state.valid) return;
    const rect = state.grid.getBoundingClientRect();
    const { columns, stepX, stepY } = metrics(state.grid);
    const width = ['wide', 'large'].includes(state.tile.dataset.tileSize) ? Math.min(2, columns) : 1;
    state.cell = { key: state.tile.dataset.tile,
      column: Math.max(1, Math.min(columns - width + 1, Math.round((state.x - state.grabX - rect.left) / stepX) + 1)),
      row: Math.max(1, Math.min(100, Math.round((state.y - state.grabY - rect.top) / stepY) + 1)) };
    const cells = arrange(state.grid, state.cell);
    state.order = [...state.tiles].sort((a, b) => {
      const ac = cells.get(a.dataset.tile), bc = cells.get(b.dataset.tile);
      return ac.row - bc.row || ac.column - bc.column;
    });
    state.order.forEach((tile, position) => { tile.style.order = position; });
  };
  const scroll = () => {
    if (!drag?.active) return;
    if (!drag.tile.isConnected || drag.tile.dataset.moveEnabled !== 'true') { stop(false); return; }
    const viewport = drag.scroller?.getBoundingClientRect();
    if (viewport && drag.x >= viewport.left && drag.x <= viewport.right) {
      const edge = 48;
      const delta = drag.y < viewport.top + edge ? -12 : drag.y > viewport.bottom - edge ? 12 : 0;
      if (delta) { drag.scroller.scrollTop += delta; preview(); }
    }
    frame = requestAnimationFrame(scroll);
  };
  const handlers = {
    pointerdown(event) {
      if (event.button !== 0 || drag || saving || root.classList.contains('dashboard-resizing')) return;
      const tile = event.target.closest?.('.dashboard-tile');
      if (!tile || tile.dataset.moveEnabled !== 'true') return;
      const handle = event.target.closest('.dashboard-drag-handle');
      if (!handle) return;
      const tiles = [...tile.parentElement.querySelectorAll('.dashboard-tile')];
      const original = arrange(tile.parentElement).get(tile.dataset.tile);
      const bounds = tile.getBoundingClientRect();
      drag = { original, grabX: event.clientX - bounds.left, grabY: event.clientY - bounds.top, tile, handle, grid: tile.parentElement, tiles, order: [...tiles], pointer: event.pointerId,
        startX: event.clientX, startY: event.clientY, x: event.clientX, y: event.clientY,
        scroller: tile.closest('.dashboard-landing'), active: false, valid: false, newRow: tile.classList.contains('tile-row-start') };
      drag.handle.setPointerCapture(event.pointerId);
      drag.handle.focus({ preventScroll: true });
      event.preventDefault();
    },
    pointermove(event) {
      if (!drag || event.pointerId !== drag.pointer) return;
      drag.x = event.clientX;
      drag.y = event.clientY;
      if (!drag.active) {
        if (Math.hypot(drag.x - drag.startX, drag.y - drag.startY) < 5) return;
        const rect = drag.tile.getBoundingClientRect();
        drag.ghost = drag.tile.cloneNode(true);
        drag.ghost.removeAttribute('id');
        drag.ghost.querySelectorAll('[id]').forEach(node => node.removeAttribute('id'));
        drag.ghost.classList.add('dashboard-drag-ghost');
        drag.ghost.setAttribute('aria-hidden', 'true');
        drag.ghost.inert = true;
        Object.assign(drag.ghost.style, { left: `${rect.left}px`, top: `${rect.top}px`, width: `${rect.width}px`, height: `${rect.height}px` });
        document.body.append(drag.ghost);
        drag.tile.classList.add('tile-dragging');
        root.classList.add('dashboard-dragging');
        drag.active = true;
        frame = requestAnimationFrame(scroll);
        announce('Release to place · Esc to cancel');
      }
      preview();
    },
    pointerup(event) { if (event.pointerId === drag?.pointer) stop(true); },
    pointercancel(event) { if (event.pointerId === drag?.pointer) stop(false); },
    lostpointercapture(event) { if (event.pointerId === drag?.pointer) stop(false); },
    dragstart(event) { if (drag) event.preventDefault(); },
    keydown(event) {
      if (event.key === 'Escape' && drag) { event.preventDefault(); stop(false); return; }
      const handle = event.target.closest?.('.dashboard-drag-handle');
      if (!handle || handle.disabled || drag || saving) return;
      const tile = handle.closest('.dashboard-tile');
      const tiles = [...tile.parentElement.querySelectorAll('.dashboard-tile')];
      const index = tiles.indexOf(tile);
      const cells = arrange(tile.parentElement), original = cells.get(tile.dataset.tile);
      const delta = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] }[event.key];
      if (delta) {
        event.preventDefault();
        const { columns } = metrics(tile.parentElement);
        const width = ['wide', 'large'].includes(tile.dataset.tileSize) ? Math.min(2, columns) : 1;
        const cell = { column: Math.max(1, Math.min(columns - width + 1, original.column + delta[0])), row: Math.max(1, Math.min(100, original.row + delta[1])) };
        if (cell.column !== original.column || cell.row !== original.row) {
          const previewCells = arrange(tile.parentElement, { ...cell, key: tile.dataset.tile });
          const ordered = [...tiles].sort((a, b) => { const ac = previewCells.get(a.dataset.tile), bc = previewCells.get(b.dataset.tile); return ac.row - bc.row || ac.column - bc.column; });
          void save(tile, ordered.indexOf(tile), false, () => {}, cell);
        }
      } else if (event.key === 'Home' || event.key === 'End') {
        event.preventDefault();
        const first = event.key === 'Home';
        void save(tile, first ? 0 : tiles.length - 1, false, () => {}, { column: 1, row: first ? 1 : Math.max(...[...cells.values()].map(p => p.row)) + 2 });
      }
    },
  };
  for (const [name, handler] of Object.entries(handlers)) root.addEventListener(name, handler);
  return () => {
    for (const [name, handler] of Object.entries(handlers)) root.removeEventListener(name, handler);
    observer.disconnect();
    resizeObserver.disconnect();
    stop(false);
  };
}

export function mountDashboardResize(root, receiver) {
  if (!root || !receiver) return () => {};
  let drag = null, saving = false;
  const announce = text => {
    const status = root.querySelector('.dashboard-layout-status');
    if (status) status.textContent = text;
  };
  const shape = handle => {
    const tile = handle.closest('.dashboard-tile');
    const grid = getComputedStyle(tile.parentElement);
    const bounds = tile.getBoundingClientRect();
    const gapX = parseFloat(grid.columnGap) || 0, gapY = parseFloat(grid.rowGap) || 0;
    const stepX = parseFloat(grid.gridTemplateColumns) + gapX;
    const stepY = parseFloat(grid.gridAutoRows) + gapY;
    const columns = grid.gridTemplateColumns.trim().split(/\s+/).length;
    const column = Math.round((bounds.left - tile.parentElement.getBoundingClientRect().left) / stepX);
    const width = Math.round((bounds.width + gapX) / stepX), height = Math.round((bounds.height + gapY) / stepY);
    return { handle, tile, bounds, gapX, gapY, stepX, stepY, columns,
      maxWidth: Math.max(1, Math.min(2, columns - column)), width, height, initialWidth: width, initialHeight: height };
  };
  const preview = (state, width, height) => {
    const pixelsX = Math.max(state.stepX - state.gapX, Math.min(state.maxWidth * state.stepX - state.gapX, width));
    const pixelsY = Math.max(state.stepY - state.gapY, Math.min(2 * state.stepY - state.gapY, height));
    // Keep the grid footprint unchanged until release so surrounding tiles cannot move the handle.
    state.tile.style.width = `${pixelsX}px`;
    state.tile.style.height = `${pixelsY}px`;
    state.tile.classList.add('tile-resizing');
    state.width = Math.max(1, Math.min(state.maxWidth, Math.round((pixelsX + state.gapX) / state.stepX)));
    state.height = Math.max(1, Math.min(2, Math.round((pixelsY + state.gapY) / state.stepY)));
  };
  const clear = state => {
    state.tile.style.removeProperty('width');
    state.tile.style.removeProperty('height');
    state.tile.classList.remove('tile-resizing');
    root.classList.remove('dashboard-resizing');
  };
  const save = async state => {
    if (state.width === state.initialWidth && state.height === state.initialHeight) { clear(state); return; }
    const wide = state.columns === 1 ? ['wide', 'large'].includes(state.tile.dataset.tileSize) : state.width === 2;
    const size = wide ? (state.height === 2 ? 'large' : 'wide') : (state.height === 2 ? 'tall' : 'square');
    saving = true;
    announce('Saving tile size…');
    try {
      const saved = await receiver.invokeMethodAsync('ResizeDashboardTileAsync', state.tile.dataset.tile, size);
      announce(saved === false ? 'Tile size was not saved. Please try again.' : 'Tile size saved.');
    } catch {
      announce('Tile size was not saved. Check your connection and try again.');
    } finally { saving = false; clear(state); }
  };
  const stop = commit => {
    if (!drag) return;
    const state = drag;
    drag = null;
    if (state.handle.hasPointerCapture(state.pointer)) state.handle.releasePointerCapture(state.pointer);
    if (commit && state.tile.isConnected) void save(state); else clear(state);
  };
  const handlers = {
    pointerdown(event) {
      const handle = event.target.closest?.('.dashboard-resize');
      if (!handle || handle.disabled || event.button !== 0 || drag || saving
        || root.classList.contains('dashboard-dragging')) return;
      drag = { ...shape(handle), pointer: event.pointerId, x: event.clientX, y: event.clientY, active: false };
      handle.setPointerCapture(event.pointerId);
      handle.focus({ preventScroll: true });
      event.preventDefault();
    },
    pointermove(event) {
      if (!drag || event.pointerId !== drag.pointer) return;
      if (!drag.active && Math.hypot(event.clientX - drag.x, event.clientY - drag.y) < 5) return;
      drag.active = true;
      root.classList.add('dashboard-resizing');
      preview(drag, drag.bounds.width + event.clientX - drag.x, drag.bounds.height + event.clientY - drag.y);
    },
    pointerup(event) { if (event.pointerId === drag?.pointer) stop(true); },
    pointercancel(event) { if (event.pointerId === drag?.pointer) stop(false); },
    lostpointercapture(event) { if (event.pointerId === drag?.pointer) stop(false); },
    dragstart(event) { if (drag) event.preventDefault(); },
    keydown(event) {
      if (event.key === 'Escape' && drag) { event.preventDefault(); stop(false); return; }
      const handle = event.target.closest?.('.dashboard-resize');
      const delta = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] }[event.key];
      if (!handle || handle.disabled || !delta || drag || saving || root.classList.contains('dashboard-dragging')) return;
      event.preventDefault();
      const state = shape(handle);
      preview(state, state.bounds.width + delta[0] * state.stepX, state.bounds.height + delta[1] * state.stepY);
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
  const unmountDashboardMove = mountDashboardMove(root, receiver);
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
      following && !messages.closest?.('.dashboard-landing') &&
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
    unmountDashboardMove();
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
