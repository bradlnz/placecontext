// Render the existing data-only chart contract as responsive, accessible SVG.
window.pcchart = (() => {
  const NS = 'http://www.w3.org/2000/svg';
  const LIGHT = ['#2a78d6', '#1baf7a', '#eda100', '#008300', '#4a3aa7', '#e34948', '#e87ba4', '#eb6834'];
  const DARK = ['#3987e5', '#199e70', '#c98500', '#008300', '#9085e9', '#e66767', '#d55181', '#d95926'];
  const charts = new Map();
  const format = new Intl.NumberFormat(undefined, { maximumSignificantDigits: 4 });
  let queued = false;
  let theme;

  function node(tag, attributes = {}, text) {
    const element = document.createElementNS(NS, tag);
    for (const [name, value] of Object.entries(attributes)) {
      element.setAttribute(name, value);
    }
    if (text !== undefined) {
      element.textContent = text;
    }
    return element;
  }

  function label(svg, x, y, text, attributes = {}) {
    const element = node('text', { x, y, fill: 'var(--text-2, #64748b)', ...attributes }, text);
    svg.appendChild(element);
    return element;
  }

  function number(value) {
    if (value !== 0 && (Math.abs(value) < 0.001 || Math.abs(value) >= 1e9)) {
      return value.toExponential(2);
    }
    return format.format(value);
  }

  function shorten(text, length) {
    return text.length > length ? text.slice(0, Math.max(1, length - 1)) + '…' : text;
  }

  function parse(raw) {
    try {
      const spec = typeof raw === 'string' ? JSON.parse(raw) : raw;
      if (!spec || !['bar', 'line', 'pie'].includes(spec.type)
        || !Array.isArray(spec.labels) || !Array.isArray(spec.series)) {
        return null;
      }
      if (spec.series.some(series => !series || !Array.isArray(series.values)
        || series.values.length !== spec.labels.length
        || series.values.some(value => typeof value !== 'number' || !Number.isFinite(value)))) {
        return null;
      }
      return {
        type: spec.type, title: String(spec.title || 'Chart'),
        labels: spec.labels.map(value => String(value ?? '')),
        series: spec.series.map(series => ({ name: String(series.name || 'Value'), values: [...series.values] })),
      };
    } catch {
      return null;
    }
  }

  function mark(svg, tag, attributes, description) {
    const element = node(tag, { ...attributes, tabindex: 0, role: 'img', 'aria-label': description });
    element.appendChild(node('title', {}, description));
    svg.appendChild(element);
  }

  function legend(svg, names, palette, width, top) {
    const columns = Math.max(1, Math.floor((width - 24) / 160));
    const cellWidth = (width - 24) / columns;
    names.forEach((name, index) => {
      const x = 12 + (index % columns) * cellWidth;
      const y = top + Math.floor(index / columns) * 20;
      svg.appendChild(node('circle', { cx: x + 4, cy: y - 4, r: 4, fill: palette[index % palette.length] }));
      const text = label(svg, x + 15, y, shorten(name, Math.floor((cellWidth - 20) / 6)));
      text.appendChild(node('title', {}, name));
    });
  }

  function cartesian(svg, spec, palette, width, bottom) {
    const left = 64, right = width - 16, top = 16;
    const values = spec.series.flatMap(series => series.values);
    const magnitude = values.reduce((max, value) => Math.max(max, Math.abs(value)), 0) || 1;
    const low = values.reduce((min, value) => Math.min(min, value / magnitude), 0);
    const high = values.reduce((max, value) => Math.max(max, value / magnitude), 0) || (low === 0 ? 1 : 0);
    const y = value => bottom - ((value / magnitude - low) / (high - low)) * (bottom - top);
    const zero = y(0);
    for (let tick = 0; tick <= 4; tick++) {
      const fraction = tick / 4;
      const value = (low * (1 - fraction) + high * fraction) * magnitude;
      const position = bottom - fraction * (bottom - top);
      svg.appendChild(node('line', { x1: left, x2: right, y1: position, y2: position, stroke: 'var(--border, #dbe2ea)' }));
      label(svg, left - 8, position + 4, number(value), { 'text-anchor': 'end' });
    }
    svg.appendChild(node('line', { x1: left, x2: right, y1: zero, y2: zero, stroke: 'var(--text-3, #94a3b8)' }));
    const slot = (right - left) / spec.labels.length;
    const x = index => left + (index + 0.5) * slot;
    const stride = Math.max(1, Math.ceil(spec.labels.length / Math.max(1, Math.floor((right - left) / 80))));
    spec.labels.forEach((name, index) => {
      if (index % stride !== 0) {
        return;
      }
      const text = label(svg, x(index), bottom + 20, shorten(name, 12), { 'text-anchor': 'middle' });
      text.appendChild(node('title', {}, name));
    });
    spec.series.forEach((series, seriesIndex) => {
      const color = palette[seriesIndex % palette.length];
      if (spec.type === 'line') {
        svg.appendChild(node('polyline', {
          points: series.values.map((value, index) => `${x(index)},${y(value)}`).join(' '),
          fill: 'none', stroke: color, 'stroke-width': 2, 'stroke-linejoin': 'round',
        }));
        series.values.forEach((value, index) => mark(svg, 'circle',
          { cx: x(index), cy: y(value), r: 3, fill: color }, `${series.name} · ${spec.labels[index]}: ${number(value)}`));
        return;
      }
      const barWidth = Math.min(36, slot * 0.75 / spec.series.length);
      series.values.forEach((value, index) => mark(svg, 'rect', {
        x: x(index) - barWidth * spec.series.length / 2 + seriesIndex * barWidth,
        y: Math.min(zero, y(value)), width: Math.max(0.1, barWidth - Math.min(2, barWidth / 10)),
        height: Math.max(1, Math.abs(zero - y(value))), rx: 2, fill: color,
      }, `${series.name} · ${spec.labels[index]}: ${number(value)}`));
    });
  }

  function pie(svg, spec, palette, width, bottom) {
    const values = spec.series[0].values;
    if (values.some(value => value < 0)) {
      label(svg, width / 2, bottom / 2, 'Pie charts require non-negative values.', { 'text-anchor': 'middle' });
      return;
    }
    const maximum = values.reduce((max, value) => Math.max(max, value), 0);
    const total = values.reduce((sum, value) => sum + (maximum ? value / maximum : 0), 0);
    const radius = Math.max(20, Math.min(width / 2 - 20, bottom / 2 - 10));
    const cx = width / 2, cy = bottom / 2;
    const point = angle => [cx + radius * Math.cos(angle), cy + radius * Math.sin(angle)];
    if (total === 0) {
      svg.appendChild(node('circle', { cx, cy, r: radius, fill: 'none', stroke: 'var(--border, #dbe2ea)', 'stroke-width': 12 }));
      label(svg, cx, cy + 4, 'No non-zero values', { 'text-anchor': 'middle' });
      return;
    }
    let start = -Math.PI / 2;
    values.forEach((value, index) => {
      if (value === 0) {
        return;
      }
      const fraction = value / maximum / total;
      const end = start + fraction * Math.PI * 2;
      const description = `${spec.labels[index]}: ${number(value)} (${number(fraction * 100)}%)`;
      const fill = palette[index % palette.length];
      if (fraction >= 1) {
        mark(svg, 'circle', { cx, cy, r: radius, fill }, description);
        return;
      }
      mark(svg, 'path', {
        d: `M ${cx} ${cy} L ${point(start).join(' ')} A ${radius} ${radius} 0 ${fraction > 0.5 ? 1 : 0} 1 ${point(end).join(' ')} Z`,
        fill, stroke: 'var(--card, #fff)', 'stroke-width': 1,
      }, description);
      start = end;
    });
  }

  function draw(state) {
    const { element, spec } = state;
    const width = Math.max(240, element.clientWidth || 640);
    const names = spec?.type === 'pie' ? spec.labels : (spec?.series.length > 1 ? spec.series.map(series => series.name) : []);
    const legendHeight = Math.ceil(names.length / Math.max(1, Math.floor((width - 24) / 160))) * 20;
    const height = Math.max(180 + legendHeight, element.clientHeight || 280);
    const svg = node('svg', { viewBox: `0 0 ${width} ${height}`, width: '100%', height, role: 'img',
      'aria-label': spec?.title || 'Chart unavailable', style: 'display:block;font:11px var(--font-sans, sans-serif)' });
    svg.appendChild(node('title', {}, spec?.title || 'Chart unavailable'));
    if (!spec || !spec.labels.length || !spec.series.length) {
      label(svg, width / 2, height / 2, spec ? 'No chart data' : 'Chart data is invalid or unsupported.', { 'text-anchor': 'middle' });
    }
    if (spec?.labels.length && spec.series.length) {
      svg.appendChild(node('desc', {}, spec.series.map(series => `${series.name}: ${series.values.map((value, index) => `${spec.labels[index]} ${number(value)}`).join('; ')}`).join('. ')));
      const palette = theme === 'dark' ? DARK : LIGHT;
      const bottom = height - legendHeight - 32;
      if (spec.type === 'pie') {
        pie(svg, spec, palette, width, bottom);
      }
      if (spec.type !== 'pie') {
        cartesian(svg, spec, palette, width, bottom);
      }
      legend(svg, names, palette, width, height - legendHeight + 12);
    }
    state.width = element.clientWidth;
    state.height = element.clientHeight;
    state.svg = svg;
    element.replaceChildren(svg);
  }

  const resize = window.ResizeObserver ? new ResizeObserver(entries => {
    for (const entry of entries) {
      const state = charts.get(entry.target.id);
      if (state?.element === entry.target && (state.width !== entry.target.clientWidth || state.height !== entry.target.clientHeight)) {
        draw(state);
      }
    }
  }) : null;

  function detach(state) {
    if (state.element) {
      resize?.unobserve(state.element);
      state.svg?.remove();
      state.element = null;
    }
  }

  function reconcileMounts() {
    const currentTheme = document.getElementById('dcshell')?.getAttribute('data-theme')
      || document.documentElement.dataset.pcTheme || 'light';
    const changedTheme = currentTheme !== theme;
    theme = currentTheme;
    for (const [id, state] of charts) {
      if (state.element && state.element !== document.getElementById(id)) {
        detach(state);
      }
    }
    for (const [id, state] of charts) {
      const element = document.getElementById(id);
      if (!element) {
        continue;
      }
      if (state.element !== element) {
        state.element = element;
        resize?.observe(element);
        draw(state);
        continue;
      }
      if (changedTheme || state.dirty || state.svg?.parentElement !== element) {
        state.dirty = false;
        draw(state);
      }
    }
  }

  function schedule() {
    if (queued) {
      return;
    }
    queued = true;
    queueMicrotask(() => {
      queued = false;
      reconcileMounts();
    });
  }

  new MutationObserver(schedule).observe(document.documentElement, {
    childList: true, subtree: true, attributes: true, attributeFilter: ['id', 'data-theme', 'data-pc-theme'],
  });

  function set(id, raw) {
    const state = charts.get(id) || {};
    state.spec = parse(raw);
    state.dirty = true;
    charts.set(id, state);
  }

  return {
    render(id, raw) {
      set(id, raw);
      reconcileMounts();
    },
    renderAll(entries) {
      for (const entry of entries) {
        if (entry) {
          set(entry.id, entry.spec);
        }
      }
      reconcileMounts();
    },
    destroy(id) {
      const state = charts.get(id);
      if (state) {
        detach(state);
        charts.delete(id);
      }
    },
  };
})();
