import RFB from './vendor/novnc-1.7.0/core/rfb.js';
const desktops = new Map();
export function connect(id, path, control = false) {
  const element = document.getElementById(id);
  if (!element) return;
  const existing = desktops.get(id);
  if (existing?.control === control && existing.path === path) return;
  disconnect(id);
  const url = new URL(path, location.href);
  if (url.origin !== location.origin) throw new Error('Desktop must use the application origin.');
  url.protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
  url.searchParams.set('viewOnly', String(!control));
  const rfb = new RFB(element, url.href);
  rfb.viewOnly = !control;
  rfb.focusOnClick = control;
  rfb.scaleViewport = true;
  rfb.resizeSession = false;
  desktops.set(id, { rfb, control, path });
  rfb.addEventListener('disconnect', () => {
    if (desktops.get(id)?.rfb !== rfb) return;
    element.setAttribute('data-disconnected', 'true');
    element.setAttribute('aria-label', 'Desktop disconnected. Use Reconnect to open it again.');
  });
  rfb.addEventListener('connect', () => {
    element.removeAttribute('data-disconnected');
    element.setAttribute('aria-label', control ? 'Interactive virtual desktop' : 'Live desktop preview, input locked');
  });
}
export function disconnect(id) {
  const desktop = desktops.get(id);
  desktops.delete(id);
  desktop?.rfb.disconnect();
}
