// PlaceContext map renderer — draws Leaflet maps from stored specs ({markers, polygons, center, zoom}).
window.pcmap = (function () {
  const maps = {};
  const specs = {};
  let leafletLoader = null;

  function ensureLeaflet() {
    if (typeof window.L !== 'undefined') return Promise.resolve(window.L);
    if (leafletLoader) return leafletLoader;
    leafletLoader = Promise.all([
      new Promise((resolve, reject) => {
        let link = document.getElementById('pc-leaflet-css');
        if (link) { resolve(); return; }
        link = document.createElement('link');
        link.id = 'pc-leaflet-css';
        link.rel = 'stylesheet';
        link.href = 'https://unpkg.com/leaflet@1.9.4/dist/leaflet.css';
        link.crossOrigin = '';
        link.onload = resolve;
        link.onerror = () => reject(new Error('Leaflet styles failed to load.'));
        document.head.appendChild(link);
      }),
      new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = 'https://unpkg.com/leaflet@1.9.4/dist/leaflet.js';
        script.crossOrigin = '';
        script.onload = resolve;
        script.onerror = () => reject(new Error('Leaflet failed to load.'));
        document.head.appendChild(script);
      })
    ]).then(() => window.L).catch(error => {
      leafletLoader = null;
      console.warn('pcmap:', error.message);
      throw error;
    });
    return leafletLoader;
  }

  function tokens() {
    const shell = document.getElementById('dcshell');
    const dark = !shell || shell.getAttribute('data-theme') !== 'light';
    return { dark };
  }

  async function render(containerId, specRaw) {
    let spec;
    try { spec = typeof specRaw === 'string' ? JSON.parse(specRaw) : specRaw; }
    catch { return; }
    try { await ensureLeaflet(); }
    catch { return; }
    const el = document.getElementById(containerId);
    if (!el) return;

    // Blazor can replace the container element on re-render (streaming updates, session
    // loads) — the old Leaflet instance then sits on a detached node and the visible div
    // stays blank. Cheap no-op only when the SAME element is still initialized with the
    // SAME spec; otherwise tear down and rebuild.
    const specKey = JSON.stringify(spec);
    if (maps[containerId] && maps[containerId]._el === el && specs[containerId] === specKey) return;
    if (maps[containerId]) {
      try { maps[containerId].remove(); } catch { /* already detached */ }
      delete maps[containerId];
    }
    // A recycled element Leaflet half-initialized earlier: wipe its state before re-creating.
    if (el._leaflet_id) { try { el._leaflet_id = null; } catch { /* ignore */ } el.innerHTML = ''; }

    specs[containerId] = specKey;
    el.innerHTML = '';

    const t = tokens();

    // Default center/zoom
    const center = spec.center || [48.1351, 11.582]; // Munich
    const zoom = spec.zoom || 10;

    const map = L.map(el, {
      zoomControl: true,
      attributionControl: true
    }).setView(center, zoom);

    // Tile layer — use CartoDB dark/light based on theme
    const tileUrl = t.dark
      ? 'https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png'
      : 'https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png';
    L.tileLayer(tileUrl, {
      attribution: '&copy; <a href="https://carto.com/">CARTO</a>',
      subdomains: 'abcd',
      maxZoom: 19
    }).addTo(map);

    // Markers
    if (spec.markers && Array.isArray(spec.markers)) {
      for (const m of spec.markers) {
        const color = m.color || '#3b82f6';
        const marker = L.circleMarker([m.lat, m.lng], {
          radius: 7,
          fillColor: color,
          color: '#fff',
          weight: 2,
          opacity: 1,
          fillOpacity: 0.85
        }).addTo(map);
        if (m.label) marker.bindTooltip(m.label, { permanent: false });
      }
    }

    // Polygons
    if (spec.polygons && Array.isArray(spec.polygons)) {
      for (const p of spec.polygons) {
        const color = p.color || '#3b82f6';
        L.polygon(p.coords, {
          color: color,
          weight: 2,
          fillColor: color,
          fillOpacity: p.fillOpacity || 0.15
        }).addTo(map);
      }
    }

    // Fit bounds if we have markers or polygons
    if (spec.markers?.length > 0) {
      const allLats = spec.markers.map(m => m.lat);
      const allLngs = spec.markers.map(m => m.lng);
      const minLat = Math.min(...allLats);
      const maxLat = Math.max(...allLats);
      const minLng = Math.min(...allLngs);
      const maxLng = Math.max(...allLngs);
      map.fitBounds([[minLat, minLng], [maxLat, maxLng]], { padding: [30, 30] });
    }

    // Force a re-render after layout settles
    setTimeout(() => map.invalidateSize(), 100);
    map._el = el;
    maps[containerId] = map;
  }

  function destroy(containerId) {
    if (maps[containerId]) {
      maps[containerId].remove();
      delete maps[containerId];
      delete specs[containerId];
    }
  }

  return { render, destroy };
})();
