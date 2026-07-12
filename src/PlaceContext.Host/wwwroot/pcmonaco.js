// PlaceContext Monaco interop — the same editor AWS Lambda's console uses.
// Loads the Monaco AMD bundle from a CDN once, then creates/controls editors by element id.
// Mirrors the pcgraph.js shape: a single window.pcmonaco = { init, setValue, getValue, destroy } global,
// invoked from Blazor via IJSRuntime.InvokeVoidAsync / InvokeAsync.
window.pcmonaco = (function () {
  const VS = 'https://cdn.jsdelivr.net/npm/monaco-editor@0.52.2/min/vs';
  const LOAD_TIMEOUT_MS = 10000; // a hanging CDN must not hang the page
  const editors = new Map();   // id → Monaco editor
  const fallbacks = new Map(); // id → plain <textarea> (CDN unreachable)
  let loaderPromise = null;

  function loadMonaco() {
    if (window.monaco) return Promise.resolve(window.monaco);
    if (loaderPromise) return loaderPromise;
    loaderPromise = new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error('Monaco CDN timed out.')), LOAD_TIMEOUT_MS);
      const script = document.createElement('script');
      script.src = VS + '/loader.js';
      script.onload = () => {
        window.require.config({ paths: { vs: VS } });
        window.require(['vs/editor/editor.main'],
          () => { clearTimeout(timer); resolve(window.monaco); },
          (e) => { clearTimeout(timer); reject(e || new Error('Monaco modules failed to load.')); });
      };
      script.onerror = () => { clearTimeout(timer); reject(new Error('Failed to load Monaco from CDN.')); };
      document.head.appendChild(script);
    }).catch(e => { loaderPromise = null; throw e; }); // allow a retry on the next init
    return loaderPromise;
  }

  // Returns true when the real Monaco editor is up, false when it degraded to a plain
  // textarea (CDN unreachable — offline/self-hosted network). NEVER throws: an exception
  // here would propagate through JS interop and terminate the Blazor circuit, freezing
  // the page on "Loading…".
  // The editor follows the portal's theme: light shells get Monaco's white 'vs', dark get
  // 'vs-dark' — and a toggle mid-session re-themes every mounted editor live.
  function shellTheme() {
    const shell = document.getElementById('dcshell');
    return shell && shell.getAttribute('data-theme') === 'light' ? 'vs' : 'vs-dark';
  }

  let themeWatcher = null;
  function watchTheme(monaco) {
    if (themeWatcher) return;
    const shell = document.getElementById('dcshell');
    if (!shell) return;
    themeWatcher = new MutationObserver(() => monaco.editor.setTheme(shellTheme()));
    themeWatcher.observe(shell, { attributes: true, attributeFilter: ['data-theme'] });
  }

  async function init(id, value, language, theme) {
    try {
      const monaco = await loadMonaco();
      const el = document.getElementById(id);
      if (!el) return true;
      destroy(id);
      watchTheme(monaco);
      const editor = monaco.editor.create(el, {
        value: value || '',
        language: language || 'plaintext',
        theme: shellTheme(),
        automaticLayout: true,
        minimap: { enabled: true },
        fontSize: 12.5,
        lineNumbers: 'on',
        scrollBeyondLastLine: false,
        tabSize: 2,
        renderWhitespace: 'selection',
        fontFamily: "'IBM Plex Mono', ui-monospace, monospace",
      });
      editors.set(id, editor);
      return true;
    } catch (e) {
      console.warn('pcmonaco: falling back to a plain editor —', e && e.message);
      const el = document.getElementById(id);
      if (el) {
        destroy(id);
        el.innerHTML = '';
        const ta = document.createElement('textarea');
        ta.value = value || '';
        ta.spellcheck = false;
        ta.style.cssText =
          'width:100%;height:100%;box-sizing:border-box;resize:none;border:none;outline:none;' +
          'background:#1e1e1e;color:#d4d4d4;padding:10px 12px;font-size:12.5px;line-height:1.5;' +
          "font-family:'IBM Plex Mono',ui-monospace,monospace;tab-size:2";
        el.appendChild(ta);
        fallbacks.set(id, ta);
      }
      return false;
    }
  }

  function setValue(id, value, language) {
    const ta = fallbacks.get(id);
    if (ta) { ta.value = value || ''; return; }
    const editor = editors.get(id);
    if (!editor) return;
    editor.setValue(value || '');
    if (language && window.monaco) {
      window.monaco.editor.setModelLanguage(editor.getModel(), language);
    }
  }

  function getValue(id) {
    const ta = fallbacks.get(id);
    if (ta) return ta.value;
    const editor = editors.get(id);
    return editor ? editor.getValue() : '';
  }

  function destroy(id) {
    const ta = fallbacks.get(id);
    if (ta) { ta.remove(); fallbacks.delete(id); }
    const editor = editors.get(id);
    if (!editor) return;
    editor.dispose();
    editors.delete(id);
  }

  return { init, setValue, getValue, destroy };
})();

// pcdata — small helpers for the project Data page (CSV download from a data: URI).
window.pcdata = {
  download(filename, dataUri) {
    const a = document.createElement('a');
    a.href = dataUri;
    a.download = filename || 'export.csv';
    document.body.appendChild(a);
    a.click();
    a.remove();
  }
};
