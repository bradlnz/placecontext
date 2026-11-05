const terminals = new WeakMap();

export function attach(element) {
  if (!element || terminals.has(element)) return;
  let following = true;
  const pin = () => { if (following) element.scrollTop = element.scrollHeight; };
  const onScroll = () => { following = element.scrollHeight - element.scrollTop - element.clientHeight <= 8; };
  element.addEventListener('scroll', onScroll, { passive: true });
  const changes = new MutationObserver(pin);
  changes.observe(element, { childList: true, subtree: true, characterData: true });
  const size = new ResizeObserver(pin);
  size.observe(element);
  terminals.set(element, {
    jump() { following = true; pin(); },
    dispose() { element.removeEventListener('scroll', onScroll); changes.disconnect(); size.disconnect(); }
  });
  pin();
}

export function jumpToLatest(element) { terminals.get(element)?.jump(); }
export function detach(element) { terminals.get(element)?.dispose(); terminals.delete(element); }
