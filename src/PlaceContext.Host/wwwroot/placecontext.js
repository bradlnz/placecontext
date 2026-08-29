// PlaceContext portal: theme persistence + animated meters (bars/rings).
window.placecontext = {
  initTheme() {
    let t = 'dark';
    try { t = localStorage.getItem('placecontext-theme') || 'dark'; } catch (e) {}
    this.applyTheme(t);
    return t;
  },
  applyTheme(t) {
    t = t === 'light' ? 'light' : 'dark';
    const sh = document.getElementById('dcshell');
    if (sh) sh.dataset.theme = t;
    const bg = t === 'light' ? '#f5f6f8' : '#0a0b0d';
    document.documentElement.dataset.pcTheme = t;
    document.body.style.background = bg;
    document.documentElement.style.background = bg;
    document.documentElement.style.colorScheme = t;
    try { localStorage.setItem('placecontext-theme', t); } catch (e) {}
    return t;
  },
  toggleTheme() {
    const cur = (document.getElementById('dcshell')?.dataset.theme) || 'dark';
    return this.applyTheme(cur === 'dark' ? 'light' : 'dark');
  },
  beginSetup(form) {
    if (!(form instanceof HTMLFormElement)) return true;
    form.classList.add('is-submitting');
    form.setAttribute('aria-busy', 'true');
    const button = form.querySelector('button[type="submit"]');
    if (button) button.disabled = true;
    return true;
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
  scrollToElement(id) {
    requestAnimationFrame(() => {
      const el = document.getElementById(id);
      if (el) el.scrollIntoView({ behavior: 'smooth', block: 'center' });
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

  initFocusLayers() {
    if (this.focusLayerObserver) return;

    const mobile = window.matchMedia('(max-width: 950px)');
    const layerSelector = [
      '.dcmodal-overlay',
      '.dcsearch-overlay',
      '.table-modal-overlay',
      '.cluster-overlay',
      '.editor-backdrop',
      '.dcslide',
      '.sidebar.open',
      '.side-panel'
    ].join(',');
    const dialogSelector = [
      '[role="dialog"]',
      '.dcmodal',
      '.dcsearch-modal',
      '.table-modal',
      '.join-dialog'
    ].join(',');
    const focusableSelector = [
      'a[href]',
      'area[href]',
      'button:not([disabled])',
      'input:not([disabled]):not([type="hidden"])',
      'select:not([disabled])',
      'textarea:not([disabled])',
      '[contenteditable="true"]',
      '[tabindex]:not([tabindex="-1"])'
    ].join(',');

    let activeLayer = null;
    let activeDialog = null;
    let semanticState = null;
    let syncQueued = false;
    const openers = new WeakMap();
    const isolated = new Map();

    const isVisible = element => {
      const style = window.getComputedStyle(element);
      return style.display !== 'none' && style.visibility !== 'hidden';
    };

    const isEligibleLayer = element => {
      if (!isVisible(element)) return false;
      if (element.matches('.sidebar.open, .side-panel')) return mobile.matches;
      return true;
    };

    const dialogFor = layer => {
      if (layer.matches('.dcslide, .sidebar.open, .side-panel')) return layer;
      return layer.querySelector(dialogSelector) || layer;
    };

    const focusablesIn = dialog => Array.from(dialog.querySelectorAll(focusableSelector))
      .filter(element => isVisible(element)
        && !element.closest('[aria-hidden="true"]')
        && !element.closest('[inert]'));

    const focusFirst = (dialog, backwards = false) => {
      const focusables = focusablesIn(dialog);
      const target = backwards ? focusables.at(-1) : focusables[0];
      (target || dialog).focus({ preventScroll: true });
    };

    const restoreSemantics = () => {
      if (!semanticState) return;
      const { element, role, ariaModal, tabIndex } = semanticState;
      if (role === null) element.removeAttribute('role');
      else element.setAttribute('role', role);
      if (ariaModal === null) element.removeAttribute('aria-modal');
      else element.setAttribute('aria-modal', ariaModal);
      if (tabIndex === null) element.removeAttribute('tabindex');
      else element.setAttribute('tabindex', tabIndex);
      semanticState = null;
    };

    const applySemantics = dialog => {
      if (semanticState?.element === dialog) return;
      restoreSemantics();
      semanticState = {
        element: dialog,
        role: dialog.getAttribute('role'),
        ariaModal: dialog.getAttribute('aria-modal'),
        tabIndex: dialog.getAttribute('tabindex')
      };
      dialog.setAttribute('role', 'dialog');
      dialog.setAttribute('aria-modal', 'true');
      if (!dialog.hasAttribute('tabindex')) dialog.setAttribute('tabindex', '-1');
    };

    const clearIsolation = () => {
      isolated.forEach((wasInert, element) => { element.inert = wasInert; });
      isolated.clear();
      document.documentElement.classList.remove('pc-focus-layer-open');
    };

    const companionsFor = layer => {
      const companions = new Set();
      const previous = layer.previousElementSibling;
      const next = layer.nextElementSibling;
      if (layer.matches('.dcslide') && previous?.matches('.dcslide-scrim')) companions.add(previous);
      if (layer.matches('.side-panel') && previous?.matches('.side-panel-scrim')) companions.add(previous);
      if (layer.matches('.sidebar.open') && next?.matches('.nav-backdrop')) companions.add(next);
      return companions;
    };

    const isolateBackground = layer => {
      clearIsolation();
      const companions = companionsFor(layer);
      let branch = layer;
      let parent = layer.parentElement;
      while (parent) {
        Array.from(parent.children).forEach(sibling => {
          if (sibling === branch || companions.has(sibling) || isolated.has(sibling)) return;
          isolated.set(sibling, sibling.inert);
          sibling.inert = true;
        });
        branch = parent;
        parent = parent.parentElement;
      }
      document.documentElement.classList.add('pc-focus-layer-open');
    };

    const sync = () => {
      syncQueued = false;
      const layers = Array.from(document.querySelectorAll(layerSelector)).filter(isEligibleLayer);
      const nextLayer = layers.reduce((top, layer) => {
        const zIndex = Number.parseInt(window.getComputedStyle(layer).zIndex, 10) || 0;
        return !top || zIndex >= top.zIndex ? { layer, zIndex } : top;
      }, null)?.layer || null;
      const previousLayer = activeLayer;
      const restoreTarget = previousLayer && previousLayer !== nextLayer
        ? openers.get(previousLayer)
        : null;
      const previousLayerClosed = previousLayer
        && previousLayer !== nextLayer
        && !layers.includes(previousLayer);

      if (!nextLayer) {
        activeLayer = null;
        activeDialog = null;
        restoreSemantics();
        clearIsolation();
        if (restoreTarget?.isConnected && !restoreTarget.closest('[inert]')) {
          restoreTarget.focus({ preventScroll: true });
        }
        if (previousLayerClosed) openers.delete(previousLayer);
        return;
      }

      const nextDialog = dialogFor(nextLayer);
      if (!openers.has(nextLayer)) openers.set(nextLayer, document.activeElement);
      activeLayer = nextLayer;
      activeDialog = nextDialog;
      applySemantics(nextDialog);
      isolateBackground(nextLayer);

      if (restoreTarget?.isConnected && nextDialog.contains(restoreTarget)) {
        restoreTarget.focus({ preventScroll: true });
      } else if (!nextDialog.contains(document.activeElement)) {
        focusFirst(nextDialog);
      }
      if (previousLayerClosed) openers.delete(previousLayer);
    };

    const queueSync = () => {
      if (syncQueued) return;
      syncQueued = true;
      queueMicrotask(sync);
    };

    document.addEventListener('keydown', event => {
      if (event.key !== 'Tab' || !activeDialog) return;
      const focusables = focusablesIn(activeDialog);
      if (focusables.length === 0) {
        event.preventDefault();
        activeDialog.focus({ preventScroll: true });
        return;
      }

      const current = document.activeElement;
      const first = focusables[0];
      const last = focusables.at(-1);
      if (!activeDialog.contains(current)) {
        event.preventDefault();
        focusFirst(activeDialog, event.shiftKey);
      } else if (event.shiftKey && current === first) {
        event.preventDefault();
        last.focus({ preventScroll: true });
      } else if (!event.shiftKey && current === last) {
        event.preventDefault();
        first.focus({ preventScroll: true });
      }
    }, true);

    document.addEventListener('focusin', event => {
      if (activeDialog && !activeDialog.contains(event.target)) focusFirst(activeDialog);
    });

    // A panel's header, empty space, and scroll surface are often plain divs.
    // On touch devices those taps do not move focus, which leaves the focus
    // context behind the overlay and makes keyboard/screen-reader navigation
    // start from the page underneath. Keep native focus for controls, but put
    // the focusable dialog itself in focus when its non-interactive surface is
    // tapped.
    document.addEventListener('pointerdown', event => {
      if (!activeDialog || !(event.target instanceof Element) || !activeDialog.contains(event.target)) return;
      if (event.target.closest(focusableSelector)) return;
      activeDialog.focus({ preventScroll: true });
    }, true);

    this.focusLayerObserver = new MutationObserver(queueSync);
    this.focusLayerObserver.observe(document.body, {
      childList: true,
      subtree: true,
      attributes: true,
      attributeFilter: ['class']
    });
    if (mobile.addEventListener) mobile.addEventListener('change', queueSync);
    else mobile.addListener(queueSync);
    sync();
  },

  async renderPdf(elementId, url) {
    const container = document.getElementById(elementId);
    if (!container || !window.matchMedia('(max-width: 950px)').matches) return;
    if (container.dataset.pdfUrl === url && container.dataset.pdfState) return;

    container.dataset.pdfUrl = url;
    container.dataset.pdfState = 'loading';
    container.replaceChildren(Object.assign(document.createElement('div'), {
      className: 'pdf-loading',
      textContent: 'Preparing all PDF pages…'
    }));

    try {
      const pdfjs = await import('/vendor/pdfjs/pdf.mjs');
      pdfjs.GlobalWorkerOptions.workerSrc = '/vendor/pdfjs/pdf.worker.mjs';
      const pdf = await pdfjs.getDocument({ url, withCredentials: true }).promise;

      if (!container.isConnected || container.dataset.pdfUrl !== url) {
        await pdf.destroy();
        return;
      }

      container.replaceChildren();
      const pages = [];
      for (let pageNumber = 1; pageNumber <= pdf.numPages; pageNumber += 1) {
        const shell = document.createElement('section');
        shell.className = 'pdf-page-shell';
        shell.dataset.pageNumber = String(pageNumber);
        shell.setAttribute('aria-label', `PDF page ${pageNumber} of ${pdf.numPages}`);

        const label = document.createElement('span');
        label.className = 'pdf-page-label';
        label.textContent = `${pageNumber} / ${pdf.numPages}`;
        shell.appendChild(label);
        container.appendChild(shell);
        pages.push(shell);
      }

      const rendered = new Set();
      let observer;
      const renderPage = async shell => {
        const pageNumber = Number(shell.dataset.pageNumber);
        if (rendered.has(pageNumber) || !container.isConnected) return;
        rendered.add(pageNumber);

        const page = await pdf.getPage(pageNumber);
        const baseViewport = page.getViewport({ scale: 1 });
        const cssWidth = Math.max(240, container.clientWidth - 26);
        const viewport = page.getViewport({ scale: cssWidth / baseViewport.width });
        const pixelRatio = Math.min(window.devicePixelRatio || 1, 2);
        const canvas = document.createElement('canvas');
        const context = canvas.getContext('2d', { alpha: false });
        canvas.width = Math.floor(viewport.width * pixelRatio);
        canvas.height = Math.floor(viewport.height * pixelRatio);
        canvas.style.width = `${Math.floor(viewport.width)}px`;
        canvas.style.height = `${Math.floor(viewport.height)}px`;
        shell.style.minHeight = '0';
        shell.appendChild(canvas);

        await page.render({
          canvasContext: context,
          viewport,
          transform: pixelRatio === 1 ? null : [pixelRatio, 0, 0, pixelRatio, 0, 0]
        }).promise;
        page.cleanup();
        observer?.unobserve(shell);
      };

      observer = new IntersectionObserver(entries => {
        entries.filter(entry => entry.isIntersecting).forEach(entry => void renderPage(entry.target));
      }, { root: container, rootMargin: '600px 0px' });
      pages.forEach(page => observer.observe(page));
      container.dataset.pdfState = 'ready';
    } catch (error) {
      container.dataset.pdfState = 'error';
      container.replaceChildren(Object.assign(document.createElement('div'), {
        className: 'pdf-render-error',
        textContent: 'Could not render every page here. Use Open to view the PDF in your browser.'
      }));
    }
  },

  initPdfObserver() {
    if (this.pdfObserver) return;
    const renderAvailable = root => {
      if (root instanceof Element && root.matches('[data-pc-pdf]')) {
        void this.renderPdf(root.id, root.dataset.pdfUrl);
      }
      root.querySelectorAll?.('[data-pc-pdf]').forEach(el => {
        void this.renderPdf(el.id, el.dataset.pdfUrl);
      });
    };
    this.pdfObserver = new MutationObserver(records => {
      records.forEach(record => record.addedNodes.forEach(node => renderAvailable(node)));
    });
    this.pdfObserver.observe(document.body, { childList: true, subtree: true });
    renderAvailable(document);
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

window.placecontext.initPdfObserver();
window.placecontext.initFocusLayers();
