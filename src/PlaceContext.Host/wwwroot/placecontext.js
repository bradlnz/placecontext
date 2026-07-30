// PlaceContext portal: theme persistence + animated meters (bars/rings).
window.placecontext = {
  initTheme() {
    let t = 'dark';
    try { t = localStorage.getItem('placecontext-theme') || 'dark'; } catch (e) {}
    this.applyTheme(t);
    return t;
  },
  applyTheme(t) {
    const sh = document.getElementById('dcshell');
    if (sh) sh.dataset.theme = t;
    const bg = t === 'light' ? '#f5f6f8' : '#0a0b0d';
    document.body.style.background = bg;
    document.documentElement.style.background = bg;
    try { localStorage.setItem('placecontext-theme', t); } catch (e) {}
    return t;
  },
  toggleTheme() {
    const cur = (document.getElementById('dcshell')?.dataset.theme) || 'dark';
    return this.applyTheme(cur === 'dark' ? 'light' : 'dark');
  },
  animateMeters() {
    requestAnimationFrame(() => {
      document.querySelectorAll('.dcbar').forEach(el => {
        const p = Math.max(0, Math.min(100, +el.getAttribute('data-pct') || 0));
        el.style.width = p + '%';
      });
      document.querySelectorAll('.dcring').forEach(el => {
        const p = Math.max(0, Math.min(100, +el.getAttribute('data-pct') || 0));
        el.style.strokeDashoffset = (214 * (1 - p / 100)).toFixed(1);
      });
    });
  },
  scrollToBottom(el) {
    if (el) el.scrollTop = el.scrollHeight;
  },
  scrollToHash() {
    const hash = location.hash;
    if (!hash) return;
    requestAnimationFrame(() => {
      const el = document.getElementById(decodeURIComponent(hash.slice(1)));
      if (!el) return;
      el.scrollIntoView({ behavior: 'smooth', block: 'center' });
      el.classList.add('dcflash');
      setTimeout(() => el.classList.remove('dcflash'), 1600);
    });
  },
  getCheckedOptions(msgId) {
    const picker = document.querySelector(`.option-picker[data-msg-id="${msgId}"]`);
    if (!picker) return [];
    return Array.from(picker.querySelectorAll('input[type="checkbox"]:checked'))
      .map(cb => cb.value);
  },

  isMobile() {
    return window.matchMedia('(max-width: 950px)').matches;
  },

  scrollLogs() {
    document.querySelectorAll('.log-pre').forEach(el => {
      el.scrollTop = el.scrollHeight;
    });
  },

  setupDropZone(el, inputId) {
    if (!(el instanceof Element)) return false;
    if (el.dataset.pcDropZone === '1') return true;
    el.dataset.pcDropZone = '1';
    el.addEventListener('dragover', e => { e.preventDefault(); el.classList.add('drag-over'); });
    el.addEventListener('dragleave', () => el.classList.remove('drag-over'));
    el.addEventListener('drop', e => {
      e.preventDefault();
      el.classList.remove('drag-over');
      if (e.dataTransfer.files.length > 0) {
        const input = document.getElementById(inputId);
        if (!(input instanceof HTMLInputElement)) return;
        const transfer = new DataTransfer();
        transfer.items.add(e.dataTransfer.files[0]);
        input.files = transfer.files;
        input.dispatchEvent(new Event('change', { bubbles: true }));
      }
    });
    return true;
  }
};
