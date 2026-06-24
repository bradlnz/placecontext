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
  }
};
