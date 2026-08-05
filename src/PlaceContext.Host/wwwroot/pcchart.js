// PlaceContext chart renderer — draws stored chart specs ({type,title,labels,series}) with the
// bundled Chart.js, themed from the design tokens. Charts re-render when the portal theme flips.
//
// Palette: the 8-slot categorical set validated (CVD ΔE, lightness band, chroma, contrast) against
// this portal's light (#ffffff) and dark (#111418) card surfaces — slot ORDER is the colorblind-
// safety mechanism, so series take slots in order and never cycle.
window.pcchart = (function () {
  const LIGHT = ['#2a78d6', '#1baf7a', '#eda100', '#008300', '#4a3aa7', '#e34948', '#e87ba4', '#eb6834'];
  const DARK  = ['#3987e5', '#199e70', '#c98500', '#008300', '#9085e9', '#e66767', '#d55181', '#d95926'];

  const charts = {};   // canvas id → Chart instance
  const specs = {};    // canvas id → spec (for theme re-render)

  function tokens() {
    const shell = document.getElementById('dcshell');
    const css = shell ? getComputedStyle(shell) : null;
    const v = (name, fallback) => (css && css.getPropertyValue(name).trim()) || fallback;
    const dark = !shell || shell.getAttribute('data-theme') !== 'light';
    return {
      dark,
      palette: dark ? DARK : LIGHT,
      surface: v('--card', dark ? '#111418' : '#ffffff'),
      ink: v('--text-2', dark ? '#98a2ad' : '#5a6571'),      // labels/legend: text tokens, never series color
      muted: v('--text-3', dark ? '#5e6873' : '#8b95a1'),    // axis ticks
      grid: v('--border', dark ? '#1d232b' : '#e5e9ed'),     // recessive hairlines
      tooltipBg: v('--card-2', dark ? '#161a1f' : '#f4f6f8'),
      tooltipInk: v('--text', dark ? '#e8ebef' : '#101620'),
    };
  }

  function datasets(spec, t) {
    if (spec.type === 'pie')
      return [{
        data: spec.series[0].values,
        backgroundColor: spec.labels.map((_, i) => t.palette[i % t.palette.length]),
        borderColor: t.surface,   // the 2px surface gap between slices
        borderWidth: 2,
      }];
    return spec.series.map((s, i) => spec.type === 'line'
      ? {
          label: s.name, data: s.values,
          borderColor: t.palette[i], backgroundColor: t.palette[i],
          borderWidth: 2, pointRadius: 0, pointHoverRadius: 4, pointHitRadius: 12,
          tension: 0.25,
        }
      : {
          label: s.name, data: s.values,
          backgroundColor: t.palette[i],
          borderRadius: { topLeft: 4, topRight: 4 }, borderSkipped: 'bottom', // rounded data-end, flat baseline
          maxBarThickness: 36, barPercentage: 0.7, categoryPercentage: 0.8,
        });
  }

  function config(spec, t) {
    const font = { family: "var(--font-sans)", size: 11 };
    const many = spec.type === 'pie' || spec.series.length > 1; // one series needs no legend — the title names it
    return {
      type: spec.type === 'pie' ? 'doughnut' : spec.type,
      data: { labels: spec.labels, datasets: datasets(spec, t) },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: { duration: 200 },
        cutout: spec.type === 'pie' ? '55%' : undefined,
        plugins: {
          legend: {
            display: many,
            position: 'bottom',
            labels: { color: t.ink, font, usePointStyle: true, pointStyle: 'circle', boxWidth: 8, boxHeight: 8, padding: 14 },
          },
          tooltip: {
            backgroundColor: t.tooltipBg, titleColor: t.tooltipInk, bodyColor: t.ink,
            borderColor: t.grid, borderWidth: 1, cornerRadius: 7, padding: 10,
            titleFont: font, bodyFont: font, displayColors: many, boxWidth: 8, boxHeight: 8, usePointStyle: true,
          },
        },
        scales: spec.type === 'pie' ? undefined : {
          x: { grid: { display: false }, border: { color: t.grid }, ticks: { color: t.muted, font, maxRotation: 45, autoSkip: true } },
          y: { beginAtZero: true, grid: { color: t.grid }, border: { display: false }, ticks: { color: t.muted, font, maxTicksLimit: 6 } },
        },
      },
    };
  }

  function draw(id) {
    const el = document.getElementById(id);
    const spec = specs[id];
    if (!el || !spec || typeof Chart === 'undefined') return;
    if (charts[id]) { charts[id].destroy(); delete charts[id]; }
    charts[id] = new Chart(el, config(spec, tokens()));
  }

  // Lazy mount: render() may fire before the canvas is committed to the DOM (Blazor applies its
  // diff after the interop call) or before Chart.js finishes loading. Retry until the element
  // exists, then defer the actual draw until it scrolls into view — a hidden or zero-size canvas
  // would otherwise paint blank and never recover.
  const pending = {}; // canvas id → retry timer
  const io = window.IntersectionObserver
    ? new IntersectionObserver(entries => {
        for (const e of entries) {
          if (!e.isIntersecting) continue;
          io.unobserve(e.target);
          draw(e.target.id);
        }
      }, { rootMargin: '200px' })
    : null;

  function mount(id, attempt) {
    delete pending[id];
    if (!specs[id]) return; // destroyed while waiting
    const el = document.getElementById(id);
    if (!el || typeof Chart === 'undefined') {
      if (attempt < 40) // ~1s of fast polls, then 500ms — gives CDN-slow Chart.js ~16s
        pending[id] = setTimeout(() => mount(id, attempt + 1), attempt < 10 ? 100 : 500);
      return;
    }
    if (io) io.observe(el);
    else draw(id);
  }

  // Theme flips re-render every live chart with the new tokens (dark is its own selected palette,
  // not an automatic flip of the light one).
  const shell = document.getElementById('dcshell');
  if (shell && window.MutationObserver) {
    new MutationObserver(() => Object.keys(charts).forEach(draw))
      .observe(shell, { attributes: true, attributeFilter: ['data-theme'] });
  }

  function renderCore(id, specJson) {
    try { specs[id] = typeof specJson === 'string' ? JSON.parse(specJson) : specJson; }
    catch { return; }
    if (pending[id]) { clearTimeout(pending[id]); delete pending[id]; }
    mount(id, 0);
  }

  return {
    render(id, specJson) {
      renderCore(id, specJson);
    },
    // Batch entry point: one interop round-trip for many charts. entries = [{id, spec}].
    renderAll(entries) {
      for (let i = 0; i < entries.length; i++) {
        const e = entries[i];
        if (e) renderCore(e.id, e.spec);
      }
    },
    destroy(id) {
      if (pending[id]) { clearTimeout(pending[id]); delete pending[id]; }
      if (charts[id]) { charts[id].destroy(); delete charts[id]; }
      if (io) { const el = document.getElementById(id); if (el) io.unobserve(el); }
      delete specs[id];
    },
  };
})();
