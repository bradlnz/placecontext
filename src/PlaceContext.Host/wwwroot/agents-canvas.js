// Agent canvas interactions — draggable nodes + dynamic connection lines.
// Keeps interactions in JS to keep the Blazor component render-focused.
window.agentsCanvas = (() => {
  const stateByCanvas = new WeakMap();

  const toNumber = (value, fallback = 0) => {
    const parsed = Number.parseFloat(value);
    return Number.isFinite(parsed) ? parsed : fallback;
  };

  const clamp = (value, min, max) => Math.min(max, Math.max(min, value));

  const getState = (canvas) => {
    let state = stateByCanvas.get(canvas);
    if (state) return state;

    state = {
      dragging: null,
      moveHandler: null,
      upHandler: null,
      resizeHandler: null,
      rafPending: false,
      boundNodes: new WeakSet()
    };
    stateByCanvas.set(canvas, state);
    return state;
  };

  const updateCanvasLines = (canvas) => {
    const rect = canvas.getBoundingClientRect();
    const lines = canvas.querySelectorAll('.agent-connection-line');

    for (const line of lines) {
      const source = canvas.querySelector(`[data-agent-id="${line.dataset.from}"]`);
      const target = canvas.querySelector(`[data-agent-id="${line.dataset.to}"]`);
      if (!source || !target) {
        line.setAttribute('visibility', 'hidden');
        continue;
      }

      const sourceRect = source.getBoundingClientRect();
      const targetRect = target.getBoundingClientRect();
      const sourceMidX = sourceRect.left - rect.left + sourceRect.width / 2;
      const sourceMidY = sourceRect.top - rect.top + sourceRect.height / 2;
      const targetMidX = targetRect.left - rect.left + targetRect.width / 2;
      const targetMidY = targetRect.top - rect.top + targetRect.height / 2;

      const sourceY = sourceMidY < targetMidY
        ? sourceRect.top - rect.top + sourceRect.height
        : sourceRect.top - rect.top;
      const targetY = sourceMidY < targetMidY
        ? targetRect.top - rect.top
        : targetRect.top - rect.top + targetRect.height;

      line.setAttribute('x1', sourceMidX.toFixed(1));
      line.setAttribute('y1', sourceY.toFixed(1));
      line.setAttribute('x2', targetMidX.toFixed(1));
      line.setAttribute('y2', targetY.toFixed(1));
      line.setAttribute('visibility', 'visible');
    }
  };

  const getNodeWidth = (nodeRectById, id, fallback = 230) => nodeRectById.get(id)?.width ?? fallback;

  const getNodeLayoutRect = (node, canvasRect) => {
    const rect = node.getBoundingClientRect();
    const dx = toNumber(getComputedStyle(node).getPropertyValue('--agent-dx'));
    const dy = toNumber(getComputedStyle(node).getPropertyValue('--agent-dy'));

    return {
      width: rect.width,
      height: rect.height,
      left: rect.left - canvasRect.left - dx,
      top: rect.top - canvasRect.top - dy
    };
  };

  const computeSubtreeWidth = (id, childrenByParent, subtreeWidthById, nodeRectById, horizontalGap, visited) => {
    if (subtreeWidthById.has(id))
      return subtreeWidthById.get(id) ?? 0;

    if (visited.has(id))
      return getNodeWidth(nodeRectById, id);

    visited.add(id);
    const children = childrenByParent.get(id) ?? [];
    if (children.length === 0)
    {
      const leafWidth = getNodeWidth(nodeRectById, id);
      subtreeWidthById.set(id, leafWidth);
      return leafWidth;
    }

    let width = 0;
    for (const childId of children)
    {
      width += computeSubtreeWidth(childId, childrenByParent, subtreeWidthById, nodeRectById, horizontalGap, visited);
    }
    width += Math.max(0, children.length - 1) * horizontalGap;

    const ownWidth = getNodeWidth(nodeRectById, id);
    const subtreeWidth = Math.max(ownWidth, width);
    subtreeWidthById.set(id, subtreeWidth);
    return subtreeWidth;
  };

  const layoutSubtree = (id, centerX, depth, depthGap, startY, childrenByParent, byId, nodeRectById,
    positions, movedByUserIds, subtreeWidthById, horizontalGap, horizontalPadding, canvasWidth, canvasHeight, rootId) => {
    const width = getNodeWidth(nodeRectById, id);
    const x = centerX - (width / 2);
    const y = startY + (depth * depthGap);
    const clampedX = clamp(x, horizontalPadding, Math.max(horizontalPadding, canvasWidth - width - horizontalPadding));
    const nodeHeight = nodeRectById.get(id)?.height ?? 0;
    const clampedY = clamp(y, 0, Math.max(0, canvasHeight - nodeHeight - horizontalPadding));

    positions.set(id, {
      x: clampedX,
      y: clampedY,
      width,
      height: nodeHeight
    });

    const isRoot = id === rootId;
    if (movedByUserIds.has(id) && !isRoot)
      return;

    const children = childrenByParent.get(id);
    if (!children || children.length === 0)
      return;

    const sortedChildren = [...children].sort((a, b) =>
      (byId.get(a)?.querySelector('strong')?.textContent ?? '')
        .localeCompare(byId.get(b)?.querySelector('strong')?.textContent ?? '', undefined, { sensitivity: 'base' }));

    const totalChildrenWidth = sortedChildren
      .reduce((sum, childId) => sum + (subtreeWidthById.get(childId) ?? getNodeWidth(nodeRectById, childId)), 0)
      + Math.max(0, sortedChildren.length - 1) * horizontalGap;
    let cursor = centerX - (totalChildrenWidth / 2);

    for (const childId of sortedChildren)
    {
      const childWidth = subtreeWidthById.get(childId) ?? getNodeWidth(nodeRectById, childId);
      layoutSubtree(
        childId,
        cursor + (childWidth / 2),
        depth + 1,
        depthGap,
        startY,
        childrenByParent,
        byId,
        nodeRectById,
        positions,
        movedByUserIds,
        subtreeWidthById,
        horizontalGap,
        horizontalPadding,
        canvasWidth,
        canvasHeight,
        rootId
      );
      cursor += childWidth + horizontalGap;
    }
  };

  const layoutTree = (canvas) => {
    const canvasRect = canvas.getBoundingClientRect();
    const nodes = [...canvas.querySelectorAll('.agent-node[data-agent-id]')];
    if (!nodes.length)
      return;

    const byId = new Map();
    const nodeRectById = new Map();
    const parentById = new Map();
    const childrenByParent = new Map();

    for (const node of nodes)
    {
      const id = node.dataset.agentId;
      if (!id)
        continue;

      byId.set(id, node);
      nodeRectById.set(id, getNodeLayoutRect(node, canvasRect));
    }

    const commandNode = nodes.find((node) => node.dataset.agentKind === 'command');
    const rootId = commandNode?.dataset.agentId;
    if (!commandNode || !rootId)
      return;

    for (const node of nodes)
    {
      const id = node.dataset.agentId;
      if (!id)
        continue;

      if (node === commandNode)
      {
        parentById.set(id, null);
        continue;
      }

      const explicitParentId = node.dataset.parentAgentId;
      const parentId = explicitParentId && byId.has(explicitParentId)
        ? explicitParentId
        : rootId;

      parentById.set(id, parentId);
      const siblings = childrenByParent.get(parentId);
      if (siblings)
        siblings.push(id);
      else
        childrenByParent.set(parentId, [id]);
    }

    const commandRect = nodeRectById.get(rootId);
    if (!commandRect)
      return;

    const averageNodeHeight = Math.max(...[...nodeRectById.values()].map((rect) => rect.height), 0);
    const depthGap = Math.max(170, averageNodeHeight + 40);
    const horizontalGap = 26;
    const horizontalPadding = 20;
    const commandWidth = commandRect.width;

    const rootY = commandRect.top;
    const positions = new Map();
    const movedByUserIds = new Set(
      nodes
        .filter((node) => node.classList.contains('dragged-by-user'))
        .map((node) => node.dataset.agentId)
        .filter(Boolean)
    );

    const subtreeWidthById = new Map();
    const visited = new Set();
    computeSubtreeWidth(
      rootId,
      childrenByParent,
      subtreeWidthById,
      nodeRectById,
      horizontalGap,
      visited
    );

    const rootWidth = getNodeWidth(nodeRectById, rootId);
    const centeredRootX = (canvasRect.width - rootWidth) / 2;
    const centeredRoot = clamp(centeredRootX, horizontalPadding, Math.max(horizontalPadding, canvasRect.width - rootWidth - horizontalPadding));
    positions.set(rootId, { x: centeredRoot, y: rootY, width: rootWidth, height: commandRect.height });

    layoutSubtree(
      rootId,
      centeredRoot + (rootWidth / 2),
      0,
      depthGap,
      rootY,
      childrenByParent,
      byId,
      nodeRectById,
      positions,
      movedByUserIds,
        subtreeWidthById,
        horizontalGap,
        horizontalPadding,
        canvasRect.width,
        canvasRect.height,
        rootId
    );

    for (const node of nodes)
    {
      const id = node.dataset.agentId;
      if (!id)
        continue;

      if (node.classList.contains('dragged-by-user') && node.dataset.agentKind !== 'command')
        continue;

      const target = positions.get(id);
      const rect = nodeRectById.get(id);
      if (!target || !rect)
        continue;

      const currentNaturalX = rect.left;
      const currentNaturalY = rect.top;
      node.style.setProperty('--agent-dx', `${Math.round(target.x - currentNaturalX)}px`);
      node.style.setProperty('--agent-dy', `${Math.round(target.y - currentNaturalY)}px`);
    }

    queueRefresh(canvas);
  };

  const queueRefresh = (canvas) => {
    const state = getState(canvas);
    if (state.rafPending) return;
    state.rafPending = true;
    requestAnimationFrame(() => {
      state.rafPending = false;
      updateCanvasLines(canvas);
    });
  };

  const stopDrag = (canvas, state, event) => {
    if (!state.dragging || state.dragging.pointerId !== event.pointerId) return;
    try {
      state.dragging.node.releasePointerCapture(event.pointerId);
    } catch (error) {
      // ignore
    }
    state.dragging.node.classList.remove('dragging-node');
    // Preserve manual placement for this session. Re-layout should not reset dragged nodes.
    state.dragging.node.classList.add('dragged-by-user');
    state.dragging = null;
    queueRefresh(canvas);
  };

  const startDrag = (canvas, state, node, event) => {
    if (state.dragging) return;
    if (event.button !== undefined && event.button !== 0 && event.button !== -1) return;
    if (event.pointerType && event.pointerType === 'touch' && event.isPrimary === false) return;
    if (node.dataset.agentKind === 'command') return;

    event.preventDefault();

    const rect = node.getBoundingClientRect();
    const canvasRect = canvas.getBoundingClientRect();
    const currentDx = toNumber(getComputedStyle(node).getPropertyValue('--agent-dx'));
    const currentDy = toNumber(getComputedStyle(node).getPropertyValue('--agent-dy'));

    state.dragging = {
      node,
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      currentDx,
      currentDy,
      width: rect.width,
      height: rect.height,
      baseLeft: rect.left - canvasRect.left - currentDx,
      baseTop: rect.top - canvasRect.top - currentDy,
      canvasWidth: canvasRect.width,
      canvasHeight: canvasRect.height
    };

    node.classList.add('dragging-node');
    node.classList.add('dragged-by-user');
    node.setPointerCapture(event.pointerId);
    queueRefresh(canvas);
  };

  const moveNode = (canvas, state, event) => {
    if (!state.dragging || state.dragging.pointerId !== event.pointerId) return;
    const drag = state.dragging;
    const nextX = clamp(
      drag.currentDx + (event.clientX - drag.startX),
      -drag.baseLeft,
      drag.canvasWidth - drag.width - drag.baseLeft
    );
    const nextY = clamp(
      drag.currentDy + (event.clientY - drag.startY),
      -drag.baseTop,
      drag.canvasHeight - drag.height - drag.baseTop
    );
    drag.node.style.setProperty('--agent-dx', `${nextX}px`);
    drag.node.style.setProperty('--agent-dy', `${nextY}px`);
    queueRefresh(canvas);
  };

  const bindNode = (canvas, node, state) => {
    if (state.boundNodes.has(node)) return;
    state.boundNodes.add(node);
    node.setAttribute('draggable', 'false');
    node.addEventListener('pointerdown', (event) => startDrag(canvas, state, node, event));
  };

  const bindCanvasNodes = (canvas, state) => {
    const nodes = canvas.querySelectorAll('.draggable-node[data-agent-id]');
    for (const node of nodes) {
      bindNode(canvas, node, state);
    }
  };

  const setupHandlers = (canvas, state) => {
    if (!state.moveHandler) {
      state.moveHandler = (event) => moveNode(canvas, state, event);
      state.upHandler = (event) => stopDrag(canvas, state, event);
      document.addEventListener('pointermove', state.moveHandler, { passive: false });
      document.addEventListener('pointerup', state.upHandler, { passive: false });
      document.addEventListener('pointercancel', state.upHandler, { passive: false });
      state.resizeHandler = () => {
        if (!state.dragging)
          layoutTree(canvas);
        else
          queueRefresh(canvas);
      };
      window.addEventListener('resize', state.resizeHandler);
      window.addEventListener('orientationchange', state.resizeHandler);
    }
  };

  const initCanvas = (canvasId) => {
    const canvas = typeof canvasId === 'string'
      ? document.getElementById(canvasId)
      : canvasId;
    if (!canvas) return;

    const state = getState(canvas);
    setupHandlers(canvas, state);
    bindCanvasNodes(canvas, state);
    layoutTree(canvas);
    queueRefresh(canvas);
  };

  return {
    init(canvasId) {
      initCanvas(canvasId);
    },
    dispose(canvasId) {
      const canvas = typeof canvasId === 'string'
        ? document.getElementById(canvasId)
        : canvasId;
      if (!canvas) return;
      const state = stateByCanvas.get(canvas);
      if (!state) return;

      if (state.moveHandler) {
        document.removeEventListener('pointermove', state.moveHandler);
        document.removeEventListener('pointerup', state.upHandler);
        document.removeEventListener('pointercancel', state.upHandler);
      }
      if (state.resizeHandler) {
        window.removeEventListener('resize', state.resizeHandler);
        window.removeEventListener('orientationchange', state.resizeHandler);
      }
      if (state.dragging?.node) {
        state.dragging.node.classList.remove('dragging-node');
      }
      stateByCanvas.delete(canvas);
    }
  };
})();
