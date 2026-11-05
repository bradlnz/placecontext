const windows = new WeakMap();
const dialogs = new WeakMap();

export function attachDialog(handle) {
  const dialog = handle?.closest('.dcmodal');
  if (!dialog) return;
  attach(dialog);
  dialogs.set(handle, dialog);
}

export function detachDialog(handle) {
  detach(dialogs.get(handle));
  dialogs.delete(handle);
}

export function attach(element) {
  if (!element || windows.has(element)) {
    return;
  }
  const handle = element.querySelector('[data-window-handle]');
  const header = handle.closest('.dcmodal-head, .code-window-head') ?? handle;
  const events = new AbortController();
  const options = { signal: events.signal };
  let drag = null;
  const move = (left, top) => {
    element.style.left = `${Math.max(0, Math.min(left, innerWidth - element.offsetWidth))}px`;
    element.style.top = `${Math.max(0, Math.min(top, innerHeight - element.offsetHeight))}px`;
    element.style.right = 'auto';
    element.style.transform = 'none';
  };
  header.addEventListener('pointerdown', event => {
    const control = event.target?.closest('button, input, select, textarea, a, summary');
    if (event.button !== 0 || (control && control !== handle)) {
      return;
    }
    const rect = element.getBoundingClientRect();
    drag = { x: event.clientX - rect.left, y: event.clientY - rect.top };
    handle.setPointerCapture(event.pointerId);
    handle.focus();
    event.preventDefault();
  }, options);
  header.addEventListener('pointermove', event => {
    if (drag) {
      move(event.clientX - drag.x, event.clientY - drag.y);
    }
  }, options);
  header.addEventListener('lostpointercapture', () => { drag = null; }, options);
  handle.addEventListener('keydown', event => {
    const delta = { ArrowLeft: [-20, 0], ArrowRight: [20, 0], ArrowUp: [0, -20], ArrowDown: [0, 20] }[event.key];
    if (!delta) {
      return;
    }
    event.preventDefault();
    const rect = element.getBoundingClientRect();
    move(rect.left + delta[0], rect.top + delta[1]);
  }, options);
  const clamp = () => {
    const rect = element.getBoundingClientRect();
    move(rect.left, rect.top);
  };
  window.addEventListener('resize', clamp, options);
  const observer = new ResizeObserver(clamp);
  observer.observe(element);
  windows.set(element, () => { events.abort(); observer.disconnect(); });
}

export function detach(element) {
  windows.get(element)?.();
  windows.delete(element);
}
