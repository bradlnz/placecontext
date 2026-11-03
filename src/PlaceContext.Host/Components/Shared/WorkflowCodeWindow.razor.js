const windows = new WeakMap();

export function attach(element) {
  if (!element || windows.has(element)) {
    return;
  }
  const handle = element.querySelector('[data-window-handle]');
  const events = new AbortController();
  const options = { signal: events.signal };
  let drag = null;
  const move = (left, top) => {
    element.style.left = `${Math.max(0, Math.min(left, innerWidth - element.offsetWidth))}px`;
    element.style.top = `${Math.max(0, Math.min(top, innerHeight - element.offsetHeight))}px`;
    element.style.right = 'auto';
    element.style.transform = 'none';
  };
  handle.addEventListener('pointerdown', event => {
    if (event.button !== 0) {
      return;
    }
    const rect = element.getBoundingClientRect();
    drag = { x: event.clientX - rect.left, y: event.clientY - rect.top };
    handle.setPointerCapture(event.pointerId);
    handle.focus();
    event.preventDefault();
  }, options);
  handle.addEventListener('pointermove', event => {
    if (drag) {
      move(event.clientX - drag.x, event.clientY - drag.y);
    }
  }, options);
  handle.addEventListener('lostpointercapture', () => { drag = null; }, options);
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
