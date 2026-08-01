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
