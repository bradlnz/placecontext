// Keep fullscreen activation in the browser's click gesture, including on Blazor Server.
(() => {
  let active;

  function exit() {
    if (!active) return;
    const { page, trigger, observer, keydown, fullscreenchange } = active;
    active = null;
    delete page.dataset.tvMode;
    observer.disconnect();
    document.removeEventListener('keydown', keydown, true);
    document.removeEventListener('fullscreenchange', fullscreenchange);
    if (document.fullscreenElement === page) {
      document.exitFullscreen().catch(() => {});
    }
    if (trigger.isConnected) trigger.focus();
  }

  function enter(page, trigger) {
    if (!page || active) return;
    page.dataset.tvMode = 'true';
    const state = { page, trigger, nativeEntered: false };
    state.keydown = event => {
      if (event.key === 'Escape') {
        event.preventDefault();
        exit();
      }
      if (event.key === 'Tab') {
        const controls = [...page.querySelectorAll('button, a[href], input, select, textarea, [tabindex="0"]')]
          .filter(element => !element.disabled && element.getClientRects().length > 0);
        const first = controls[0], last = controls.at(-1);
        if (!first) return;
        if (!page.contains(document.activeElement) || (event.shiftKey && document.activeElement === first)) {
          event.preventDefault();
          (event.shiftKey ? last : first).focus();
        } else if (!event.shiftKey && document.activeElement === last) {
          event.preventDefault();
          first.focus();
        }
      }
    };
    state.fullscreenchange = () => {
      if (document.fullscreenElement === page) {
        state.nativeEntered = true;
      } else if (state.nativeEntered) {
        exit();
      }
    };
    state.observer = new MutationObserver(() => {
      if (!page.isConnected) exit();
    });
    active = state;
    document.addEventListener('keydown', state.keydown, true);
    document.addEventListener('fullscreenchange', state.fullscreenchange);
    state.observer.observe(document.documentElement, { childList: true, subtree: true });
    page.querySelector('[data-tv-exit]')?.focus();
    try {
      // The presentation layout still works when browser fullscreen is unavailable or denied.
      page.requestFullscreen?.().catch(() => {});
    } catch { }
  }

  document.addEventListener('click', event => {
    const trigger = event.target.closest?.('[data-tv-enter]');
    if (trigger) enter(trigger.closest('[data-tv-page]'), trigger);
    if (event.target.closest?.('[data-tv-exit]')) exit();
  });
})();
