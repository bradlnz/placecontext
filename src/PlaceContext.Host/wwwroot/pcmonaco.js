// PlaceContext Monaco interop — the same editor AWS Lambda's console uses.
// Loads the Monaco AMD bundle from a CDN once, then creates/controls editors by element id.
// Uses model-per-file: each file gets its own TextModel keyed by "{editorId}::{path}",
// so switching files is instant (editor.setModel) and edits are preserved across switches.
window.pcmonaco = (function () {
  const VS = 'https://cdn.jsdelivr.net/npm/monaco-editor@0.52.2/min/vs';
  const LOAD_TIMEOUT_MS = 10000; // a hanging CDN must not hang the page
  const editors = new Map();   // id → Monaco editor
  const models = new Map();    // "id::path" → Monaco TextModel
  const fallbacks = new Map(); // id → plain <textarea> (CDN unreachable)
  const initLocks = new Map(); // id → in-flight init Promise (serializes concurrent inits)
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
  // a yellow-inspired 'placecontext-ch' theme — and a toggle mid-session re-themes
  // every mounted editor live.
  function shellTheme() {
    const shell = document.getElementById('dcshell');
    return shell && shell.getAttribute('data-theme') === 'light' ? 'vs' : 'placecontext-ch';
  }

  let yellowThemeDefined = false;
  function defineyellowTheme(monaco) {
    if (yellowThemeDefined) return;
    yellowThemeDefined = true;
    monaco.editor.defineTheme('placecontext-ch', {
      base: 'vs-dark',
      inherit: true,
      rules: [
        { token: 'keyword.sql', foreground: 'FFCC00', fontStyle: 'bold' },
        { token: 'operator.sql', foreground: 'E4E7EC' },
        { token: 'identifier.sql', foreground: '7EE787' },
        { token: 'string.sql', foreground: '9CDCFE' },
        { token: 'number.sql', foreground: 'B5CEA8' },
        { token: 'comment.sql', foreground: '7E838D', fontStyle: 'italic' }
      ],
      colors: {
        'editor.background': '#1F2125',
        'editor.lineHighlightBackground': '#2A2D33',
        'editorLineNumber.foreground': '#7E838D',
        'editorLineNumber.activeForeground': '#E4E7EC',
        'editor.selectionBackground': '#3E4249',
        'editor.inactiveSelectionBackground': '#33363D',
        'editorCursor.foreground': '#ff6200',
        'editorWhitespace.foreground': '#4B4F57'
      }
    });
  }

  let themeWatcher = null;
  function watchTheme(monaco) {
    if (themeWatcher) return;
    const shell = document.getElementById('dcshell');
    if (!shell) return;
    defineyellowTheme(monaco);
    themeWatcher = new MutationObserver(() => monaco.editor.setTheme(shellTheme()));
    themeWatcher.observe(shell, { attributes: true, attributeFilter: ['data-theme'] });
  }

  // ── SQL autocomplete ──────────────────────────────────────────────────────────────────────
  // Schema-aware completion for every Monaco 'sql' editor: the Blazor side pushes the project's
  // tables and OpenSearch indexes (with their columns/fields) through setSqlSchema, and this
  // provider suggests keywords, table/index names, and columns for the tables referenced so far.
  const SQL_KEYWORDS = [
    'SELECT','FROM','WHERE','JOIN','LEFT JOIN','RIGHT JOIN','INNER JOIN','OUTER JOIN','FULL JOIN',
    'ON','GROUP BY','ORDER BY','HAVING','LIMIT','OFFSET','AS','AND','OR','NOT','IN','IS','NULL',
    'IS NULL','IS NOT NULL','BETWEEN','LIKE','ILIKE','DISTINCT','COUNT','SUM','AVG','MIN','MAX',
    'CASE','WHEN','THEN','ELSE','END','INSERT INTO','UPDATE','DELETE FROM','VALUES','CREATE TABLE',
    'CREATE VIEW','ALTER TABLE','DROP TABLE','UNION','ALL','EXISTS','WITH','ASC','DESC','SELECT *',
  ];
  let sqlTables = [];          // [{ name, columns: [{ name, type }], kind: 'table' }]
  let sqlIndexes = [];         // [{ name, columns: [{ name, type }], kind: 'index' }]
  let sqlProviderRegistered = false;

  function allSchemaObjects() { return sqlTables.concat(sqlIndexes); }

  function registerSqlCompletion(monaco) {
    if (sqlProviderRegistered) return;
    sqlProviderRegistered = true;
    monaco.languages.registerCompletionItemProvider('sql', {
      triggerCharacters: ['.', ' '],
      provideCompletionItems(model, position) {
        const until = model.getValueInRange({
          startLineNumber: 1, startColumn: 1,
          endLineNumber: position.lineNumber, endColumn: position.column,
        });
        const word = model.getWordUntilPosition(position);
        const suggestions = [];

        // `table.` or `index.` → only that object's columns/fields.
        const dot = until.match(/([A-Za-z_][\w]*)\.\s*$/);
        if (dot) {
          const obj = allSchemaObjects().find(t => t.name === dot[1]);
          if (obj) {
            for (const c of obj.columns)
              suggestions.push({
                label: c.name, insertText: c.name, kind: monaco.languages.CompletionItemKind.Field,
                detail: c.type || (obj.kind === 'index' ? 'field' : 'column'), range: word, sortText: 'a',
              });
          }
          return { suggestions };
        }

        for (const kw of SQL_KEYWORDS)
          suggestions.push({
            label: kw, insertText: kw, kind: monaco.languages.CompletionItemKind.Keyword,
            range: word, sortText: 'z' + kw,
          });

        for (const t of sqlTables)
          suggestions.push({
            label: t.name, insertText: t.name, kind: monaco.languages.CompletionItemKind.Class,
            detail: 'table', range: word, sortText: 'a' + t.name,
          });

        for (const i of sqlIndexes)
          suggestions.push({
            label: i.name, insertText: i.name, kind: monaco.languages.CompletionItemKind.Interface,
            detail: 'index', range: word, sortText: 'a' + i.name,
          });

        const referenced = referencedSchemaObjects(until);
        const scope = referenced.length > 0 ? referenced : allSchemaObjects();
        const seen = new Set();
        for (const obj of scope) {
          for (const c of obj.columns) {
            if (seen.has(c.name)) continue; // same label in two objects: keep the first
            seen.add(c.name);
            suggestions.push({
              label: c.name, insertText: c.name, kind: monaco.languages.CompletionItemKind.Field,
              detail: (c.type || (obj.kind === 'index' ? 'field' : 'column'))
                + (scope.length > 1 ? ' · ' + obj.name : ''),
              range: word, sortText: 'b' + c.name,
            });
          }
        }
        return { suggestions };
      },
    });
  }

  function referencedSchemaObjects(text) {
    const found = new Set();
    const re = /\b(?:from|join|into|update|table|index)\s+(?:"([^"]+)"|([A-Za-z_][\w]*))/gi;
    let match;
    while ((match = re.exec(text))) found.add(match[1] || match[2]);
    const known = new Map(allSchemaObjects().map(t => [t.name, t]));
    return [...found].map(name => known.get(name)).filter(Boolean);
  }

  function normalizeSchema(schema) {
    // Backward-compat: callers used to pass a plain array of tables.
    if (Array.isArray(schema)) return { tables: schema, indexes: [] };
    return {
      tables: schema && schema.tables || [],
      indexes: schema && schema.indexes || [],
    };
  }

  function setSqlSchema(schema) {
    const normalized = normalizeSchema(schema);
    sqlTables = (normalized.tables || []).map(t => ({
      name: t.name,
      kind: 'table',
      columns: (t.columns || []).map(c => ({ name: c.name, type: c.type })),
    }));
    sqlIndexes = (normalized.indexes || []).map(i => ({
      name: i.name,
      kind: 'index',
      columns: (i.columns || []).map(c => ({ name: c.name, type: c.type })),
    }));
    if (window.monaco) registerSqlCompletion(window.monaco);
  }

  function modelKey(id, path) { return id + '::' + path; }

  function getOrCreateModel(monaco, id, path, value, language) {
    const key = modelKey(id, path);
    const existing = models.get(key);
    if (existing) {
      if (value !== undefined && existing.getValue() !== value) {
        existing.setValue(value || '');
      }
      if (language && existing.getLanguageId() !== language) {
        monaco.editor.setModelLanguage(existing, language);
      }
      return existing;
    }
    const uri = monaco.Uri.parse('file:///' + id + '/' + path);
    const model = monaco.editor.createModel(value || '', language || 'plaintext', uri);
    models.set(key, model);
    return model;
  }

  async function init(id, value, language, theme, path) {
    // Serialize concurrent inits for the same element: a rapid file switch during initial load
    // must not spawn two racing Monaco creates, or the visible editor can end up showing the
    // previous file (or a disposed instance) instead of the requested one.
    while (initLocks.has(id)) {
      try { await initLocks.get(id); } catch { /* keep waiting */ }
    }
    const lock = (async () => {
      try {
        const monaco = await loadMonaco();
        const el = document.getElementById(id);
        if (!el) return true;
        destroy(id);
        watchTheme(monaco);
        // Schema may have been pushed (setSqlSchema) before the CDN finished loading — the
        // provider is cheap and guarded, so registering here always wires autocomplete up.
        registerSqlCompletion(monaco);
        const modelPath = path || '__active__';
        const model = getOrCreateModel(monaco, id, modelPath, value, language);
        const editor = monaco.editor.create(el, {
          model: model,
          theme: shellTheme(),
          automaticLayout: true,
          minimap: { enabled: true },
          fontSize: 12.5,
          lineNumbers: 'on',
          scrollBeyondLastLine: false,
          tabSize: 2,
          renderWhitespace: 'selection',
          fontFamily: "'JetBrains Mono', ui-monospace, monospace",
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
            'background:#1f2125;color:#e4e7ec;padding:10px 12px;font-size:13px;line-height:1.55;' +
            "font-family:'JetBrains Mono',ui-monospace,monospace;tab-size:2";
          el.appendChild(ta);
          fallbacks.set(id, ta);
        }
        return false;
      }
    })();
    initLocks.set(id, lock);
    try { return await lock; } finally { initLocks.delete(id); }
  }

  // Switch the editor to display a different file. Creates a model if one doesn't exist yet.
  // This is the idiomatic Monaco multi-file pattern: instant switch, full edit preservation.
  function openFile(id, path, value, language) {
    try {
      const ta = fallbacks.get(id);
      if (ta) { ta.value = value || ''; return; }
      const editor = editors.get(id);
      if (!editor) {
        if (initLocks.has(id)) {
          initLocks.get(id).then(() => openFile(id, path, value, language));
          return;
        }
        init(id, value, language);
        return;
      }
      const monaco = window.monaco;
      if (!monaco) return;
      const model = getOrCreateModel(monaco, id, path, value, language);
      if (editor.getModel() !== model) {
        editor.setModel(model);
      }
    } catch (e) {
      console.warn('pcmonaco.openFile error:', e);
    }
  }

  // Remove a model from the cache (e.g. after file deletion). Disposes the TextModel.
  function closeFile(id, path) {
    try {
      const key = modelKey(id, path);
      const model = models.get(key);
      if (model) {
        model.dispose();
        models.delete(key);
      }
    } catch (e) {
      console.warn('pcmonaco.closeFile error:', e);
    }
  }

  function setValue(id, value, language) {
    try {
      const ta = fallbacks.get(id);
      if (ta) { ta.value = value || ''; return; }
      let editor = editors.get(id);
      if (!editor || !editor.getModel()) {
        if (initLocks.has(id)) {
          initLocks.get(id).then(() => setValue(id, value, language));
          return;
        }
        init(id, value, language);
        return;
      }
      editor.setValue(value || '');
      if (language && window.monaco && editor.getModel()) {
        const current = editor.getModel().getLanguageId();
        if (current !== language) {
          window.monaco.editor.setModelLanguage(editor.getModel(), language);
        }
      }
    } catch (e) {
      console.warn('pcmonaco.setValue error:', e);
    }
  }

  // null when no editor exists for the id — callers must NOT treat that as "empty file",
  // or a click during the initial load would wipe the file's content in the caller's state.
  function getValue(id) {
    try {
      const ta = fallbacks.get(id);
      if (ta) return ta.value;
      const editor = editors.get(id);
      if (editor && editor.getModel()) return editor.getValue();
    } catch (e) {
      console.warn('pcmonaco.getValue error:', e);
    }
    return null;
  }

  function destroy(id) {
    try {
      const ta = fallbacks.get(id);
      if (ta) { ta.remove(); fallbacks.delete(id); }
      const editor = editors.get(id);
      if (!editor) return;
      editor.dispose();
      editors.delete(id);
      // Dispose all models belonging to this editor instance
      const prefix = id + '::';
      for (const [key, model] of models) {
        if (key.startsWith(prefix)) {
          try { model.dispose(); } catch { /* already disposed */ }
          models.delete(key);
        }
      }
    } catch (e) {
      console.warn('pcmonaco.destroy error:', e);
    }
  }

  return { init, openFile, closeFile, setValue, getValue, destroy, setSqlSchema };
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
