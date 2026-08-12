// PlaceContext dependency-graph: an animated force-directed graph on <canvas>.
// Physics: pairwise repulsion + link springs + gentle centering, cooled over time and
// re-heated on interaction. Supports drag, hover-highlight, wheel-zoom and background pan.
(function () {
  const instances = new Map();

  function cssVar(name, fallback) {
    const el = document.getElementById('dcshell') || document.documentElement;
    const v = getComputedStyle(el).getPropertyValue(name).trim();
    return v || fallback;
  }

  function colors() {
    return {
      brand: cssVar('--brand', '#0f766e'),
      brand2: cssVar('--brand-2', '#2dd4bf'),
      node: cssVar('--text-2', '#98a2ad'),
      label: cssVar('--text-2', '#98a2ad'),
      link: cssVar('--border-2', '#2a323c'),
      amb: cssVar('--warn', '#e0a458'),
      card: cssVar('--card', '#111418'),
      good: cssVar('--good', '#43d675'),
      human: cssVar('--human', '#58a6ff'),
    };
  }

  function init(id, data, dotnetRef) {
    destroy(id);
    const canvas = document.getElementById(id);
    if (!canvas || !data || !data.nodes || data.nodes.length === 0) return;

    const byId = new Map();
    const nodes = data.nodes.map((n, i) => {
      const angle = (i / data.nodes.length) * Math.PI * 2;
      const node = {
        id: n.id, label: n.label || n.id, degree: n.degree | 0, god: !!n.god,
        kind: n.kind || null, labeled: !!n.labeled,
        x: Math.cos(angle) * 120 + (Math.random() - 0.5) * 40,
        y: Math.sin(angle) * 120 + (Math.random() - 0.5) * 40,
        vx: 0, vy: 0, fixed: false,
        r: n.god ? 7 : 3 + Math.min(6, (n.degree | 0) * 0.8),
      };
      byId.set(n.id, node);
      return node;
    });
    const links = (data.links || [])
      .map(l => ({ s: byId.get(l.source), t: byId.get(l.target), amb: l.confidence === 'Ambiguous' }))
      .filter(l => l.s && l.t);

    const neighbors = new Map(nodes.map(n => [n, new Set()]));
    for (const l of links) { neighbors.get(l.s).add(l.t); neighbors.get(l.t).add(l.s); }

    const st = {
      canvas, ctx: canvas.getContext('2d'), nodes, links, neighbors, byId, dotnetRef,
      pan: { x: 0, y: 0 }, scale: 1, alpha: 0.45, col: colors(),
      hover: null, selected: null, drag: null, panning: null, raf: 0, w: 0, h: 0, dpr: 1,
    };
    instances.set(id, st);

    // A click (mousedown+up without real movement) selects the node and tells Blazor, which
    // shows the details overlay; clicking empty space clears the selection.
    function notifySelected() {
      if (!st.dotnetRef) return;
      try { st.dotnetRef.invokeMethodAsync('OnNodeClick', st.selected ? st.selected.id : null); } catch (e) {}
    }

    function resize() {
      const rect = canvas.getBoundingClientRect();
      st.dpr = window.devicePixelRatio || 1;
      st.w = rect.width; st.h = rect.height;
      canvas.width = Math.max(1, Math.floor(rect.width * st.dpr));
      canvas.height = Math.max(1, Math.floor(rect.height * st.dpr));
      if (st.pan.x === 0 && st.pan.y === 0) { st.pan.x = st.w / 2; st.pan.y = st.h / 2; }
    }
    resize();
    st.ro = new ResizeObserver(resize); st.ro.observe(canvas);

    const toWorld = (sx, sy) => ({ x: (sx - st.pan.x) / st.scale, y: (sy - st.pan.y) / st.scale });
    const requestFrame = () => {
      if (st.raf) return;
      st.raf = requestAnimationFrame(step);
    };
    st.requestFrame = requestFrame;
    const mouse = e => { const r = canvas.getBoundingClientRect(); return { x: e.clientX - r.left, y: e.clientY - r.top }; };
    const pick = (wx, wy) => {
      let best = null, bd = Infinity;
      for (const n of nodes) { const d = Math.hypot(n.x - wx, n.y - wy); if (d < n.r + 6 / st.scale && d < bd) { bd = d; best = n; } }
      return best;
    };

    st.onDown = e => {
      const m = mouse(e), w = toWorld(m.x, m.y), n = pick(w.x, w.y);
      st.downAt = m;
      if (n) { st.drag = n; n.fixed = true; st.alpha = Math.max(st.alpha, 0.6); }
      else { st.panning = { x: m.x - st.pan.x, y: m.y - st.pan.y }; }
      requestFrame();
    };
    st.onMove = e => {
      const m = mouse(e);
      if (st.drag) { const w = toWorld(m.x, m.y); st.drag.x = w.x; st.drag.y = w.y; st.drag.vx = st.drag.vy = 0; st.alpha = Math.max(st.alpha, 0.5); }
      else if (st.panning) { st.pan.x = m.x - st.panning.x; st.pan.y = m.y - st.panning.y; }
      else
      {
        const w = toWorld(m.x, m.y);
        const hover = pick(w.x, w.y);
        if (hover !== st.hover)
        {
          st.hover = hover;
          requestFrame();
        }
        canvas.style.cursor = st.hover ? 'pointer' : 'default';
      }
      if (st.drag || st.panning)
        requestFrame();
    };
    st.onUp = e => {
      const moved = st.downAt && e && (() => { const m = mouse(e); return Math.hypot(m.x - st.downAt.x, m.y - st.downAt.y) > 4; })();
      if (!moved && st.downAt) {
        st.selected = st.drag || null; // clicked a node → select it; clicked empty space → clear
        notifySelected();
      }
      if (st.drag) st.drag.fixed = false;
      st.drag = null; st.panning = null; st.downAt = null;
    };
    st.onWheel = e => {
      e.preventDefault();
      const m = mouse(e), w = toWorld(m.x, m.y);
      st.scale = Math.min(4, Math.max(0.25, st.scale * (e.deltaY < 0 ? 1.1 : 0.9)));
      st.pan.x = m.x - w.x * st.scale; st.pan.y = m.y - w.y * st.scale;
      requestFrame();
    };
    canvas.addEventListener('mousedown', st.onDown);
    window.addEventListener('mousemove', st.onMove);
    window.addEventListener('mouseup', st.onUp);
    canvas.addEventListener('wheel', st.onWheel, { passive: false });

    function step() {
      const a = st.alpha;
      if (a > 0.01) {
        for (let i = 0; i < nodes.length; i++) {
          const n = nodes[i];
          for (let j = i + 1; j < nodes.length; j++) {
            const o = nodes[j];
            let dx = n.x - o.x, dy = n.y - o.y, d2 = dx * dx + dy * dy || 0.01;
            const f = (2200 * a) / d2, d = Math.sqrt(d2);
            const fx = Math.max(-0.9, Math.min(0.9, (dx / d) * f));
            const fy = Math.max(-0.9, Math.min(0.9, (dy / d) * f));
            n.vx += fx; n.vy += fy; o.vx -= fx; o.vy -= fy;
          }
          n.vx -= n.x * 0.006 * a; n.vy -= n.y * 0.006 * a; // centering
        }
        for (const l of links) {
          const dx = l.t.x - l.s.x, dy = l.t.y - l.s.y, d = Math.hypot(dx, dy) || 0.01;
          const f = (d - 64) * 0.01 * a;
          const fx = Math.max(-1.2, Math.min(1.2, (dx / d) * f));
          const fy = Math.max(-1.2, Math.min(1.2, (dy / d) * f));
          l.s.vx += fx; l.s.vy += fy; l.t.vx -= fx; l.t.vy -= fy;
        }
        for (const n of nodes) {
          if (n.fixed) { n.vx = n.vy = 0; continue; }
          n.vx *= 0.9; n.vy *= 0.9;
          n.x += n.vx; n.y += n.vy;
        }
        st.alpha *= 0.992;
      }
      render();

      if (a > 0.01 || st.drag || st.panning)
      {
        st.raf = requestAnimationFrame(step);
      }
      else
      {
        st.raf = 0;
      }
    }

    function render() {
      const { ctx, col } = st;
      ctx.setTransform(st.dpr, 0, 0, st.dpr, 0, 0);
      ctx.clearRect(0, 0, st.w, st.h);
      ctx.translate(st.pan.x, st.pan.y); ctx.scale(st.scale, st.scale);
      const hv = st.hover || st.selected, nb = hv ? st.neighbors.get(hv) : null;

      for (const l of links) {
        const lit = hv && (l.s === hv || l.t === hv);
        ctx.beginPath(); ctx.moveTo(l.s.x, l.s.y); ctx.lineTo(l.t.x, l.t.y);
        ctx.strokeStyle = lit ? col.brand2 : (l.amb ? col.amb : col.link);
        ctx.globalAlpha = hv ? (lit ? 0.95 : 0.15) : (l.amb ? 0.7 : 0.5);
        ctx.lineWidth = (lit ? 1.6 : 1) / st.scale;
        if (l.amb) ctx.setLineDash([4 / st.scale, 3 / st.scale]); else ctx.setLineDash([]);
        ctx.stroke();
      }
      ctx.setLineDash([]); ctx.globalAlpha = 1;

      for (const n of nodes) {
        const dim = hv && n !== hv && !(nb && nb.has(n));
        ctx.globalAlpha = dim ? 0.25 : 1;
        ctx.beginPath(); ctx.arc(n.x, n.y, n.r, 0, Math.PI * 2);
        ctx.fillStyle = n.kind === 'good' ? col.good : n.kind === 'human' ? col.human : (n.god ? col.brand : col.node); ctx.fill();
        if (n.god || n === hv || n === st.selected) { ctx.lineWidth = 1.5 / st.scale; ctx.strokeStyle = col.brand2; ctx.stroke(); }
      }
      ctx.globalAlpha = 1;

      ctx.font = `${11 / st.scale}px ui-monospace, monospace`;
      ctx.fillStyle = col.label; ctx.textAlign = 'left'; ctx.textBaseline = 'middle';
      for (const n of nodes) {
        if (!n.god && !n.labeled && n !== hv && !(nb && nb.has(n))) continue;
        ctx.globalAlpha = n === hv ? 1 : 0.85;
        ctx.fillText(short(n.label), n.x + n.r + 3 / st.scale, n.y);
      }
      ctx.globalAlpha = 1;
    }

    function short(s) { s = String(s); const i = s.lastIndexOf('/'); s = i >= 0 ? s.slice(i + 1) : s; return s.length > 28 ? s.slice(0, 27) + '…' : s; }

    step();
  }

  // Programmatic selection (the details overlay's neighbor links): highlight the node, nudge the
  // sim so it settles visibly, and notify Blazor like a real click.
  function select(id, nodeId) {
      const st = instances.get(id);
      if (!st) return;
      st.selected = nodeId ? (st.byId.get(nodeId) || null) : null;
      if (st.selected) { // centre the view on the node — search/card jumps land where you look
        st.pan.x = st.w / 2 - st.selected.x * st.scale;
        st.pan.y = st.h / 2 - st.selected.y * st.scale;
      }
      st.alpha = Math.max(st.alpha, 0.2);
    if (st.dotnetRef) { try { st.dotnetRef.invokeMethodAsync('OnNodeClick', st.selected ? st.selected.id : null); } catch (e) {} }
    st.requestFrame && st.requestFrame();
  }

  function destroy(id) {
    const st = instances.get(id);
    if (!st) return;
    cancelAnimationFrame(st.raf);
    try { st.ro && st.ro.disconnect(); } catch (e) {}
    window.removeEventListener('mousemove', st.onMove);
    window.removeEventListener('mouseup', st.onUp);
    try { st.canvas.removeEventListener('mousedown', st.onDown); st.canvas.removeEventListener('wheel', st.onWheel); } catch (e) {}
    instances.delete(id);
  }

  window.pcgraph = { init, select, destroy };
})();
