// PlaceContext dependency-graph: an animated force-directed graph on <canvas>.
// Physics: pairwise repulsion + link springs + gentle centering, cooled over time and
// re-heated on interaction. Supports drag, hover-highlight, wheel-zoom and background pan.
(function () {
  const instances = new Map();
  const layoutCache = new Map();
  const CACHE_SIZE = 6;
  const LARGE_GRAPH = 2500;
  const NODE_BATCH = 250;
  const LINK_BATCH = 1200;

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

  function nodeColorForKind(kind, col) {
    const kindColorMap = {
      root: '#ff6b6b',
      hub: '#ff6b6b',
      decision: '#4ecdc4',
      change: '#ffe66d',
      run: '#95e1d3',
      job: '#f59e0b',
      table: '#a8d8ea',
      good: '#96ceb4',
      artifact: col.good,
      jobrun: '#e0a458',
      jobrunoutput: '#e0a458',
      address: '#58a6ff',
      location: '#c084fc',
      entity: '#f97316',
      file: '#2dd4bf',
      chain: '#22d3ee',
      tool: '#67e8f9',
      activity: '#f43f5e',
      human: col.human,
    };

    if (!kind) return col.node;
    const normalized = String(kind).toLowerCase().trim().replace(/[^a-z0-9]/g, '');
    return kindColorMap[normalized] || col.node;
  }

  function init(id, data, dotnetRef) {
    destroy(id);
    const canvas = document.getElementById(id);
    if (!canvas || !data || !data.nodes || data.nodes.length === 0) return;
    const totalNodes = data.totalNodes || data.nodes.length || 0;
    const shouldStreamRender = totalNodes >= LARGE_GRAPH;
    const cached = data.graphKey ? layoutCache.get(data.graphKey) : null;
    const cachePoints = cached?.points || null;

    const byId = new Map();
    const nodes = data.nodes.map((n, i) => {
      const angle = (i / data.nodes.length) * Math.PI * 2;
      const spread = Math.max(16, Math.ceil(Math.sqrt(data.nodes.length)));
      const gx = ((i % spread) - spread / 2) * 14;
      const gy = (Math.floor(i / spread) - spread / 2) * 14;
      const cachedPoint = cachePoints ? cachePoints[n.id] : null;
      const radius = shouldStreamRender ? 6 : 120;
      const jitter = shouldStreamRender ? 16 : 40;
      const node = {
        id: n.id, label: n.label || n.id, degree: n.degree | 0, god: !!n.god,
        kind: n.kind || null, labeled: !!n.labeled,
        x: cachedPoint ? cachedPoint.x : (Math.cos(angle) * radius) + gx + (Math.random() - 0.5) * jitter,
        y: cachedPoint ? cachedPoint.y : (Math.sin(angle) * radius) + gy + (Math.random() - 0.5) * jitter,
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
      renderState: {
        shouldStreamRender,
        nodeBudget: shouldStreamRender ? Math.min(NODE_BATCH, nodes.length) : nodes.length,
        linkBudget: shouldStreamRender ? Math.min(LINK_BATCH, links.length) : links.length,
      },
      hoverDirty: false,
      hoverPos: { x: 0, y: 0 },
      cursor: 'default',
      graphKey: data.graphKey || null,
      linkKeys: new Set(),
      totalNodes,
    };
    for (const l of links)
      st.linkKeys.add(`${l.s.id}|${l.t.id}`);
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
      const max = st.renderState.shouldStreamRender ? st.renderState.nodeBudget : nodes.length;
      for (let i = 0; i < max; i++) {
        const n = nodes[i];
        const d = Math.hypot(n.x - wx, n.y - wy);
        if (d < n.r + 6 / st.scale && d < bd) { bd = d; best = n; }
      }
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
        st.hoverPos = m;
        if (!st.hoverDirty)
        {
          st.hoverDirty = true;
          requestAnimationFrame(() =>
          {
            st.hoverDirty = false;
            if (st.drag || st.panning)
              return;

            const mv = st.hoverPos;
            const w = toWorld(mv.x, mv.y);
            const hover = pick(w.x, w.y);
            if (hover !== st.hover)
            {
              st.hover = hover;
              const cursor = hover ? 'pointer' : 'default';
              if (st.cursor !== cursor)
              {
                st.cursor = cursor;
                st.canvas.style.cursor = cursor;
              }
              requestFrame();
            }
          });
        }
      }
      if (st.drag || st.panning)
        requestFrame();
    };
    st.onLeave = () =>
    {
      st.hover = null;
      if (st.cursor !== 'default')
      {
        st.cursor = 'default';
        st.canvas.style.cursor = 'default';
      }
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
    canvas.addEventListener('mouseleave', st.onLeave);

    function step() {
      const a = st.alpha;
      if (a > 0.01 && !st.renderState.shouldStreamRender) {
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

      const rendering = st.renderState.shouldStreamRender && (st.renderState.nodeBudget < nodes.length || st.renderState.linkBudget < links.length);

      if (st.renderState.shouldStreamRender)
      {
        if (st.renderState.nodeBudget < nodes.length)
          st.renderState.nodeBudget = Math.min(nodes.length, st.renderState.nodeBudget + NODE_BATCH);
        if (st.renderState.linkBudget < links.length)
          st.renderState.linkBudget = Math.min(links.length, st.renderState.linkBudget + LINK_BATCH);
      }

      if ((!st.renderState.shouldStreamRender && a > 0.01) || st.drag || st.panning || rendering)
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
      const nodeBudget = st.renderState.shouldStreamRender ? st.renderState.nodeBudget : nodes.length;
      const linkBudget = st.renderState.shouldStreamRender ? st.renderState.linkBudget : links.length;

      for (let i = 0; i < linkBudget; i++) {
        const l = links[i];
        const lit = hv && (l.s === hv || l.t === hv);
        ctx.beginPath(); ctx.moveTo(l.s.x, l.s.y); ctx.lineTo(l.t.x, l.t.y);
        ctx.strokeStyle = lit ? col.brand2 : (l.amb ? col.amb : col.link);
        ctx.globalAlpha = hv ? (lit ? 0.95 : 0.15) : (l.amb ? 0.7 : 0.5);
        ctx.lineWidth = (lit ? 1.6 : 1) / st.scale;
        if (l.amb) ctx.setLineDash([4 / st.scale, 3 / st.scale]); else ctx.setLineDash([]);
        ctx.stroke();
      }
      ctx.setLineDash([]); ctx.globalAlpha = 1;

      for (let i = 0; i < nodeBudget; i++) {
        const n = nodes[i];
        const dim = hv && n !== hv && !(nb && nb.has(n));
        ctx.globalAlpha = dim ? 0.25 : 1;
        ctx.beginPath(); ctx.arc(n.x, n.y, n.r, 0, Math.PI * 2);
        ctx.fillStyle = n.god ? col.brand : nodeColorForKind(n.kind, col); ctx.fill();
        if (n.god || n === hv || n === st.selected) { ctx.lineWidth = 1.5 / st.scale; ctx.strokeStyle = col.brand2; ctx.stroke(); }
      }
      ctx.globalAlpha = 1;

      ctx.font = `${11 / st.scale}px ui-monospace, monospace`;
      ctx.fillStyle = col.label; ctx.textAlign = 'left'; ctx.textBaseline = 'middle';
      for (let i = 0; i < nodeBudget; i++) {
        const n = nodes[i];
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
      if (st.renderState.shouldStreamRender && st.selected)
      {
        const selectedIndex = st.nodes.findIndex(n => n.id === st.selected.id);
        if (selectedIndex >= 0 && selectedIndex >= st.renderState.nodeBudget)
          st.renderState.nodeBudget = Math.min(st.nodes.length, selectedIndex + 1);
      }
      if (st.selected) { // centre the view on the node — search/card jumps land where you look
        st.pan.x = st.w / 2 - st.selected.x * st.scale;
        st.pan.y = st.h / 2 - st.selected.y * st.scale;
      }
      st.alpha = Math.max(st.alpha, 0.2);
    if (st.dotnetRef) { try { st.dotnetRef.invokeMethodAsync('OnNodeClick', st.selected ? st.selected.id : null); } catch (e) {} }
    st.requestFrame && st.requestFrame();
  }

  function append(id, data) {
    const st = instances.get(id);
    if (!st || !data || !data.nodes || data.nodes.length === 0)
      return;

    const incomingTotal = data.totalNodes || data.nodes.length || 0;

    const nodes = (data.nodes || []).map((n, i) => {
      const angle = (i / data.nodes.length) * Math.PI * 2;
      const spread = Math.max(16, Math.ceil(Math.sqrt(st.totalNodes || st.nodes.length + data.nodes.length)));
      const gx = ((st.nodes.length + i) % spread - spread / 2) * 14;
      const gy = (Math.floor((st.nodes.length + i) / spread) - spread / 2) * 14;
      const node = {
        id: n.id, label: n.label || n.id, degree: n.degree | 0, god: !!n.god,
        kind: n.kind || null, labeled: !!n.labeled,
        x: Math.cos(angle) * 6 + gx + (Math.random() - 0.5) * 16,
        y: Math.sin(angle) * 6 + gy + (Math.random() - 0.5) * 16,
        vx: 0, vy: 0, fixed: false,
        r: n.god ? 7 : 3 + Math.min(6, (n.degree | 0) * 0.8),
      };

      st.nodes.push(node);
      st.byId.set(n.id, node);
      st.neighbors.set(node, new Set());
      return node;
    });

    const links = (data.links || [])
      .map(l => ({ s: st.byId.get(l.source), t: st.byId.get(l.target), amb: l.confidence === 'Ambiguous' }))
      .filter(l => l.s && l.t);

    st.totalNodes = Math.max(st.totalNodes || 0, incomingTotal, st.nodes.length);

    for (const l of links)
    {
      const a = l.s.id < l.t.id ? `${l.s.id}|${l.t.id}` : `${l.t.id}|${l.s.id}`;
      if (st.linkKeys.has(a))
        continue;
      st.linkKeys.add(a);
      st.links.push(l);
      st.neighbors.get(l.s).add(l.t);
      st.neighbors.get(l.t).add(l.s);
    }

    if (st.renderState.shouldStreamRender)
    {
      st.renderState.nodeBudget = Math.min(st.nodes.length, st.renderState.nodeBudget);
      st.renderState.linkBudget = Math.min(st.links.length, st.renderState.linkBudget);
    }
    else
    {
      st.renderState.nodeBudget = st.nodes.length;
      st.renderState.linkBudget = st.links.length;
    }
    if (st.requestFrame)
      st.requestFrame();
  }

  function destroy(id) {
    const st = instances.get(id);
    if (!st) return;
    if (st.graphKey && st.nodes.length > 0 && st.nodes.length <= 30000)
    {
      const points = {};
      for (const n of st.nodes)
        points[n.id] = { x: n.x, y: n.y };
      layoutCache.set(st.graphKey, { points });
      while (layoutCache.size > CACHE_SIZE)
        layoutCache.delete(layoutCache.keys().next().value);
    }
    cancelAnimationFrame(st.raf);
    try { st.ro && st.ro.disconnect(); } catch (e) {}
    window.removeEventListener('mousemove', st.onMove);
    window.removeEventListener('mouseup', st.onUp);
    try { st.canvas.removeEventListener('mousedown', st.onDown); st.canvas.removeEventListener('wheel', st.onWheel); st.canvas.removeEventListener('mouseleave', st.onLeave); } catch (e) {}
    instances.delete(id);
  }

  window.pcgraph = { init, append, select, destroy };
})();
