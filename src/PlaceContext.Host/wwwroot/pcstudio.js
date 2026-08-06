// PlaceContext SQL Studio helpers — currently just the draggable divider between the query
// editor and the results pane. The handle (a thin strip) drags to resize the editor shell as a
// percentage of the studio column; the results pane flexes to fill whatever is left.
window.pcstudio = {
  splitter(handleId) {
    const handle = document.getElementById(handleId);
    if (!handle || handle.dataset.bound === '1') return;
    handle.dataset.bound = '1';
    const column = handle.parentElement;
    const top = handle.previousElementSibling;
    const bottom = handle.nextElementSibling;
    if (!column || !top || !bottom) return;

    const start = (e) => {
      if (e.button !== 0) return;
      e.preventDefault();
      const columnHeight = column.getBoundingClientRect().height;
      const startY = e.clientY;
      const startPct = (top.getBoundingClientRect().height / columnHeight) * 100;
      handle.setPointerCapture(e.pointerId);

      const onMove = (ev) => {
        const dy = ev.clientY - startY;
        let pct = startPct + (dy / columnHeight) * 100;
        pct = Math.min(78, Math.max(12, pct));
        top.style.height = pct + '%';
        handle.classList.add('dragging');
      };
      const onUp = (ev) => {
        handle.releasePointerCapture(ev.pointerId);
        handle.classList.remove('dragging');
        handle.removeEventListener('pointermove', onMove);
        handle.removeEventListener('pointerup', onUp);
      };
      handle.addEventListener('pointermove', onMove);
      handle.addEventListener('pointerup', onUp);
    };

    handle.addEventListener('pointerdown', start);
  },
};
