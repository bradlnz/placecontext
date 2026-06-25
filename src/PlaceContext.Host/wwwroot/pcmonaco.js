// PlaceContext Monaco interop — the same editor AWS Lambda's console uses.
// Loads the Monaco AMD bundle from a CDN once, then creates/controls editors by element id.
// Mirrors the pcgraph.js shape: a single window.pcmonaco = { init, setValue, getValue, destroy } global,
// invoked from Blazor via IJSRuntime.InvokeVoidAsync / InvokeAsync.
window.pcmonaco = (function () {
  const VS = 'https://cdn.jsdelivr.net/npm/monaco-editor@0.52.2/min/vs';
  const editors = new Map();
  let loaderPromise = null;

  function loadMonaco() {
    if (window.monaco) return Promise.resolve(window.monaco);
    if (loaderPromise) return loaderPromise;
    loaderPromise = new Promise((resolve, reject) => {
      const script = document.createElement('script');
      script.src = VS + '/loader.js';
      script.onload = () => {
        window.require.config({ paths: { vs: VS } });
        window.require(['vs/editor/editor.main'], () => resolve(window.monaco));
      };
      script.onerror = () => reject(new Error('Failed to load Monaco from CDN.'));
      document.head.appendChild(script);
    });
    return loaderPromise;
  }

  async function init(id, value, language, theme) {
    const monaco = await loadMonaco();
    const el = document.getElementById(id);
    if (!el) return;
    destroy(id);
    const editor = monaco.editor.create(el, {
      value: value || '',
      language: language || 'plaintext',
      theme: theme || 'vs-dark',
      automaticLayout: true,
      minimap: { enabled: true },
      fontSize: 12.5,
      lineNumbers: 'on',
      scrollBeyondLastLine: false,
      tabSize: 2,
      renderWhitespace: 'selection',
      fontFamily: "'Geist Mono', ui-monospace, monospace",
    });
    editors.set(id, editor);
  }

  function setValue(id, value, language) {
    const editor = editors.get(id);
    if (!editor) return;
    editor.setValue(value || '');
    if (language && window.monaco) {
      window.monaco.editor.setModelLanguage(editor.getModel(), language);
    }
  }

  function getValue(id) {
    const editor = editors.get(id);
    return editor ? editor.getValue() : '';
  }

  function destroy(id) {
    const editor = editors.get(id);
    if (!editor) return;
    editor.dispose();
    editors.delete(id);
  }

  return { init, setValue, getValue, destroy };
})();
