// PlaceContext Monaco interop — the same editor AWS Lambda's console uses.
// Loads the Monaco AMD bundle from a CDN once, then creates/controls editors by element id.
// Uses model-per-file: each file gets its own TextModel keyed by "{editorId}::{path}",
// so switching files is instant (editor.setModel) and edits are preserved across switches.
window.pcmonaco = (function () {
  const VS = 'https://cdn.jsdelivr.net/npm/monaco-editor@0.52.2/min/vs';
  const LOAD_TIMEOUT_MS = 10000; // a hanging CDN must not hang the page
  const editors = new Map(); // id → Monaco editor
  const models = new Map(); // "id::path" → Monaco TextModel
  const fallbacks = new Map(); // id → plain editor while Monaco loads or is unavailable
  const loadingViews = new Map(); // id → skeleton shown through Monaco's first render
  const valueWatchers = new Map();
  const initLocks = new Map(); // id → in-flight init Promise (serializes concurrent inits)
  let loaderPromise = null;

  function loadMonaco() {
    if (window.monaco) {
      return Promise.resolve(window.monaco);
    }
    if (loaderPromise) {
      return loaderPromise;
    }
    loaderPromise = new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error('Monaco CDN timed out.')), LOAD_TIMEOUT_MS);
      const script = document.createElement('script');
      script.src = VS + '/loader.js';
      script.onload = () => {
        window.require.config({ paths: { vs: VS } });
        window.require(
          ['vs/editor/editor.main'],
          () => {
            clearTimeout(timer);
            resolve(window.monaco);
          },
          (e) => {
            clearTimeout(timer);
            reject(e || new Error('Monaco modules failed to load.'));
          },
        );
      };
      script.onerror = () => {
        clearTimeout(timer);
        reject(new Error('Failed to load Monaco from CDN.'));
      };
      document.head.appendChild(script);
    }).catch((e) => {
      loaderPromise = null;
      throw e;
    }); // allow a retry on the next init
    return loaderPromise;
  }

  // Keep the plain editor's value while loading; expose it only if Monaco fails.
  // The editor follows the portal's theme: light shells get Monaco's white 'vs', dark get
  // a neutral PlaceContext theme — and a toggle mid-session re-themes
  // every mounted editor live.
  function shellTheme() {
    const shell = document.getElementById('dcshell');
    return shell && shell.getAttribute('data-theme') === 'light' ? 'vs' : 'placecontext-dark';
  }

  let darkThemeDefined = false;
  function defineDarkTheme(monaco) {
    if (darkThemeDefined) {
      return;
    }
    darkThemeDefined = true;
    monaco.editor.defineTheme('placecontext-dark', {
      base: 'vs-dark',
      inherit: true,
      rules: [
        { token: 'keyword.sql', foreground: 'FFCC00', fontStyle: 'bold' },
        { token: 'operator.sql', foreground: 'E4E7EC' },
        { token: 'identifier.sql', foreground: '7EE787' },
        { token: 'string.sql', foreground: '9CDCFE' },
        { token: 'number.sql', foreground: 'B5CEA8' },
        { token: 'comment.sql', foreground: '7E838D', fontStyle: 'italic' },
      ],
      colors: {
        'editor.background': '#171717',
        'editor.foreground': '#EDEDED',
        'editorGutter.background': '#171717',
        'minimap.background': '#171717',
        'editor.lineHighlightBackground': '#1C1C1C',
        'editorLineNumber.foreground': '#737373',
        'editorLineNumber.activeForeground': '#C7C7C7',
        'editor.selectionBackground': '#3E3E3E',
        'editor.inactiveSelectionBackground': '#2A2A2A',
        'editorCursor.foreground': '#EDEDED',
        'editorWhitespace.foreground': '#3E3E3E',
        'editorWidget.background': '#1C1C1C',
        'editorWidget.border': '#3E3E3E',
        'editorSuggestWidget.background': '#1C1C1C',
        'editorSuggestWidget.border': '#3E3E3E',
        'editorSuggestWidget.selectedBackground': '#333333',
        'editorHoverWidget.background': '#1C1C1C',
        'editorHoverWidget.border': '#3E3E3E',
        focusBorder: '#5A5A5A',
        'scrollbarSlider.background': '#3E3E3E88',
        'scrollbarSlider.hoverBackground': '#5A5A5AAA',
        'scrollbarSlider.activeBackground': '#707070AA',
      },
    });
  }

  let themeWatcher = null;
  function watchTheme(monaco) {
    if (themeWatcher) {
      return;
    }
    const shell = document.getElementById('dcshell');
    if (!shell) {
      return;
    }
    defineDarkTheme(monaco);
    themeWatcher = new MutationObserver(() => monaco.editor.setTheme(shellTheme()));
    themeWatcher.observe(shell, { attributes: true, attributeFilter: ['data-theme'] });
  }

  // ── SQL autocomplete ──────────────────────────────────────────────────────────────────────
  // Schema-aware completion for every Monaco 'sql' editor: the Blazor side pushes the project's
  // tables and OpenSearch indexes (with their columns/fields) through setSqlSchema, and this
  // provider suggests keywords, table/index names, and columns for the tables referenced so far.
  const SQL_KEYWORDS = [
    'SELECT',
    'FROM',
    'WHERE',
    'JOIN',
    'LEFT JOIN',
    'RIGHT JOIN',
    'INNER JOIN',
    'OUTER JOIN',
    'FULL JOIN',
    'ON',
    'GROUP BY',
    'ORDER BY',
    'HAVING',
    'LIMIT',
    'OFFSET',
    'AS',
    'AND',
    'OR',
    'NOT',
    'IN',
    'IS',
    'NULL',
    'IS NULL',
    'IS NOT NULL',
    'BETWEEN',
    'LIKE',
    'ILIKE',
    'DISTINCT',
    'COUNT',
    'SUM',
    'AVG',
    'MIN',
    'MAX',
    'CASE',
    'WHEN',
    'THEN',
    'ELSE',
    'END',
    'INSERT INTO',
    'UPDATE',
    'DELETE FROM',
    'VALUES',
    'CREATE TABLE',
    'CREATE VIEW',
    'ALTER TABLE',
    'DROP TABLE',
    'UNION',
    'ALL',
    'EXISTS',
    'WITH',
    'ASC',
    'DESC',
    'SELECT *',
  ];
  // Query-building snippets offered alongside schema completions. Labels start with the keyword
  // so Monaco's prefix filtering keeps them discoverable while typing.
  const SQL_SNIPPETS = [
    {
      label: 'SELECT columns FROM table LIMIT',
      detail: 'bounded column query',
      insertText: 'SELECT\n    ${1:column}\nFROM\n    ${2:table}\nLIMIT ${3:100}',
    },
    {
      label: 'SELECT * FROM table WHERE',
      detail: 'bounded filtered query',
      insertText: 'SELECT\n    ${1:*}\nFROM\n    ${2:table}\nWHERE\n    ${3:column} = ${4:value}\nLIMIT ${5:100}',
    },
    {
      label: 'COUNT(*) FROM table',
      detail: 'row count',
      insertText: 'SELECT COUNT(*) AS "row_count"\nFROM\n    ${1:table}',
    },
    {
      label: 'WHERE column AND column',
      detail: 'two-column filter',
      insertText: 'WHERE\n    ${1:column} = ${2:value}\n    AND ${3:column} = ${4:value}',
    },
  ];
  let sqlTables = []; // [{ name, columns: [{ name, type }], kind: 'table' }]
  let sqlIndexes = []; // [{ name, columns: [{ name, type }], kind: 'index' }]
  let sqlProviderRegistered = false;

  function allSchemaObjects() {
    return sqlTables.concat(sqlIndexes);
  }

  function registerSqlCompletion(monaco) {
    if (sqlProviderRegistered) {
      return;
    }
    sqlProviderRegistered = true;
    monaco.languages.registerCompletionItemProvider('sql', {
      triggerCharacters: ['.', ' '],
      provideCompletionItems(model, position) {
        const until = model.getValueInRange({
          startLineNumber: 1,
          startColumn: 1,
          endLineNumber: position.lineNumber,
          endColumn: position.column,
        });
        const word = model.getWordUntilPosition(position);
        const suggestions = [];

        // `table.` or `index.` → only that object's columns/fields.
        const dot = until.match(/([A-Za-z_][\w]*)\.\s*$/);
        if (dot) {
          const obj = allSchemaObjects().find((t) => t.name === dot[1]);
          if (obj) {
            for (const c of obj.columns) {
              suggestions.push({
                label: c.name,
                insertText: c.name,
                kind: monaco.languages.CompletionItemKind.Field,
                detail: c.type || (obj.kind === 'index' ? 'field' : 'column'),
                range: word,
                sortText: 'a',
              });
            }
          }
          return { suggestions };
        }

        for (const kw of SQL_KEYWORDS) {
          suggestions.push({
            label: kw,
            insertText: kw,
            kind: monaco.languages.CompletionItemKind.Keyword,
            range: word,
            sortText: 'z' + kw,
          });
        }

        for (const snippet of SQL_SNIPPETS) {
          suggestions.push({
            label: snippet.label,
            insertText: snippet.insertText,
            kind: monaco.languages.CompletionItemKind.Snippet,
            detail: snippet.detail,
            range: word,
            insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
            sortText: 'c' + snippet.label,
          });
        }

        for (const t of sqlTables) {
          suggestions.push({
            label: t.name,
            insertText: t.name,
            kind: monaco.languages.CompletionItemKind.Class,
            detail: 'table',
            range: word,
            sortText: 'a' + t.name,
          });
        }

        for (const i of sqlIndexes) {
          suggestions.push({
            label: i.name,
            insertText: i.name,
            kind: monaco.languages.CompletionItemKind.Interface,
            detail: 'index',
            range: word,
            sortText: 'a' + i.name,
          });
        }

        const referenced = referencedSchemaObjects(until);
        const scope = referenced.length > 0 ? referenced : allSchemaObjects();
        const seen = new Set();
        for (const obj of scope) {
          for (const c of obj.columns) {
            if (seen.has(c.name)) {
              continue;
            } // same label in two objects: keep the first
            seen.add(c.name);
            suggestions.push({
              label: c.name,
              insertText: c.name,
              kind: monaco.languages.CompletionItemKind.Field,
              detail:
                (c.type || (obj.kind === 'index' ? 'field' : 'column')) +
                (scope.length > 1 ? ' · ' + obj.name : ''),
              range: word,
              sortText: 'b' + c.name,
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
    while ((match = re.exec(text))) {
      found.add(match[1] || match[2]);
    }
    const known = new Map(allSchemaObjects().map((t) => [t.name, t]));
    return [...found].map((name) => known.get(name)).filter(Boolean);
  }

  function normalizeSchema(schema) {
    // Backward-compat: callers used to pass a plain array of tables.
    if (Array.isArray(schema)) {
      return { tables: schema, indexes: [] };
    }
    return {
      tables: (schema && schema.tables) || [],
      indexes: (schema && schema.indexes) || [],
    };
  }

  function setSqlSchema(schema) {
    const normalized = normalizeSchema(schema);
    sqlTables = (normalized.tables || []).map((t) => ({
      name: t.name,
      kind: 'table',
      columns: (t.columns || []).map((c) => ({ name: c.name, type: c.type })),
    }));
    sqlIndexes = (normalized.indexes || []).map((i) => ({
      name: i.name,
      kind: 'index',
      columns: (i.columns || []).map((c) => ({ name: c.name, type: c.type })),
    }));
    if (window.monaco) {
      registerSqlCompletion(window.monaco);
    }
  }

  let logQueryRegistered = false;
  function registerLogQuery(monaco) {
    if (logQueryRegistered) {
      return;
    }
    logQueryRegistered = true;
    const fields = ['job', 'project', 'run', 'status', 'level', 'stream', 'message', 'timestamp'];
    const keywords = [
      'logs',
      'fields',
      'project',
      'filter',
      'where',
      'stats',
      'summarize',
      'sort',
      'limit',
      'take',
      'as',
      'by',
      'contains',
      '!contains',
      'contains_cs',
      '!contains_cs',
      'AND',
      'OR',
      'NOT',
      'IN',
      'ORDER BY',
      'ASC',
      'DESC',
      'count()',
      'count_distinct()',
      'sum()',
      'avg()',
      'min()',
      'max()',
      'bin(5m)',
    ];
    monaco.languages.register({ id: 'logquery' });
    monaco.languages.setMonarchTokensProvider('logquery', {
      ignoreCase: true,
      tokenizer: {
        root: [
          [/"(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'/, 'string'],
          [
            /\b(?:logs|fields|project|filter|where|stats|summarize|sort|limit|take|as|by|contains_cs|contains|count|count_distinct|sum|avg|min|max|bin|AND|OR|NOT|IN|ORDER|ASC|DESC)\b/,
            'keyword',
          ],
          [/\b(?:job|project|run|status|level|stream|message|timestamp)\b/, 'type.identifier'],
          // Consume bare values whole so ERROR cannot be split into ERR + the OR keyword.
          [/[a-z_][\w.]*/, 'identifier'],
          [/-\d+[smhd]\b/, 'number'],
          [/[=!<>~]+/, 'operator'],
          [/[(),|]/, 'delimiter'],
        ],
      },
    });
    monaco.languages.registerCompletionItemProvider('logquery', {
      provideCompletionItems(model, position) {
        const word = model.getWordUntilPosition(position);
        const range = {
          startLineNumber: position.lineNumber,
          endLineNumber: position.lineNumber,
          startColumn: word.startColumn,
          endColumn: word.endColumn,
        };
        return {
          suggestions: [
            ...fields.map((label) => ({ label, kind: monaco.languages.CompletionItemKind.Field })),
            ...keywords.map((label) => ({ label, kind: monaco.languages.CompletionItemKind.Keyword })),
            ...[
              'TRACE',
              'DEBUG',
              'INFO',
              'WARN',
              'ERROR',
              'FATAL',
              'stdout',
              'stderr',
              'Succeeded',
              'Failed',
            ].map((label) => ({ label, kind: monaco.languages.CompletionItemKind.Value })),
          ].map((item) => ({ ...item, insertText: item.label, range })),
        };
      },
    });
  }

  function modelKey(id, path) {
    return id + '::' + path;
  }

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

  async function init(id, value, language, theme, path, readOnly = false, ariaLabel = null) {
    const label =
      ariaLabel || (readOnly ? 'Run output' : language === 'logquery' ? 'Log query' : 'Code editor');
    // Serialize concurrent inits for the same element: a rapid file switch during initial load
    // must not spawn two racing Monaco creates, or the visible editor can end up showing the
    // previous file (or a disposed instance) instead of the requested one.
    while (initLocks.has(id)) {
      try {
        await initLocks.get(id);
      } catch {
        /* keep waiting */
      }
    }
    const lock = (async () => {
      const el = document.getElementById(id);
      if (!el) {
        return true;
      }
      destroy(id);
      el.innerHTML = '';
      const ta = document.createElement('textarea');
      ta.value = value || '';
      ta.readOnly = readOnly;
      ta.hidden = true;
      ta.spellcheck = false;
      ta.dataset.path = path || '__active__';
      ta.dataset.language = language || 'plaintext';
      ta.setAttribute('aria-label', label);
      ta.style.cssText =
        'width:100%;height:100%;box-sizing:border-box;resize:none;border:none;' +
        'background:var(--card-2,#171717);color:var(--text,#ededed);padding:10px 12px;font-size:13px;line-height:1.55;' +
        "font-family:'JetBrains Mono',ui-monospace,monospace;tab-size:2";
      el.appendChild(ta);
      fallbacks.set(id, ta);
      const skeleton = document.createElement('div');
      skeleton.className = 'pcmonaco-loading';
      skeleton.setAttribute('role', 'status');
      skeleton.setAttribute('aria-label', 'Loading code editor');
      skeleton.innerHTML = '<span class="pcmonaco-loading-label">Loading code editor…</span>' +
        [64, 82, 48, 72, 56, 88, 40, 68].map(width =>
          `<span aria-hidden="true" class="dcskel dcskel-line" style="width:${width}%"></span>`).join('');
      el.classList.add('pcmonaco-pending');
      el.setAttribute('aria-busy', 'true');
      el.appendChild(skeleton);
      loadingViews.set(id, { el, skeleton });
      let upgrading = false;
      try {
        const monaco = await loadMonaco();
        if (document.getElementById(id) !== el || fallbacks.get(id) !== ta) {
          return true;
        }
        const currentValue = ta.value;
        const focused = document.activeElement === ta;
        const start = ta.selectionStart,
          end = ta.selectionEnd;
        const modelPath = ta.dataset.path,
          currentLanguage = ta.dataset.language;
        upgrading = true;
        destroy(id, true);
        watchTheme(monaco);
        // Schema may have been pushed (setSqlSchema) before the CDN finished loading — the
        // provider is cheap and guarded, so registering here always wires autocomplete up.
        registerSqlCompletion(monaco);
        if (language === 'logquery') {
          registerLogQuery(monaco);
        }
        const model = getOrCreateModel(monaco, id, modelPath, currentValue, currentLanguage);
        const editor = monaco.editor.create(el, {
          model: model,
          theme: shellTheme(),
          automaticLayout: true,
          readOnly,
          minimap: { enabled: !readOnly && language !== 'logquery' },
          ariaLabel: label,
          wordWrap: 'on',
          fontSize: 12.5,
          lineNumbers: 'on',
          scrollBeyondLastLine: false,
          tabSize: 2,
          renderWhitespace: 'selection',
          fontFamily: "'JetBrains Mono', ui-monospace, monospace",
        });
        editors.set(id, editor);
        // Creating the model starts the lazy language loader; wait before revealing its code.
        await monaco.editor.colorize('', model.getLanguageId(), {});
        if (editors.get(id) !== editor) return true;
        editor.layout();
        // Tokenize the visible lines now instead of exposing Monaco's plain-text first paint.
        monaco.editor.colorizeModelLine(model, editor.getVisibleRanges().at(-1)?.endLineNumber || 1);
        editor.render(true);
        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
        if (editors.get(id) !== editor) {
          return true;
        }
        if (focused) {
          const from = model.getPositionAt(start),
            to = model.getPositionAt(end);
          editor.setSelection({
            startLineNumber: from.lineNumber,
            startColumn: from.column,
            endLineNumber: to.lineNumber,
            endColumn: to.column,
          });
          editor.focus();
        }
        return true;
      } catch (e) {
        console.warn('pcmonaco: using the plain editor —', e && e.message);
        if (upgrading && document.getElementById(id) === el) {
          destroy(id);
          el.innerHTML = '';
          el.appendChild(ta);
          fallbacks.set(id, ta);
        }
        ta.hidden = false;
        return true;
      } finally {
        clearLoading(id);
      }
    })();
    initLocks.set(id, lock);
    try {
      return await lock;
    } finally {
      initLocks.delete(id);
    }
  }

  // Switch the editor to display a different file. Creates a model if one doesn't exist yet.
  // This is the idiomatic Monaco multi-file pattern: instant switch, full edit preservation.
  function openFile(id, path, value, language) {
    try {
      const ta = fallbacks.get(id);
      if (ta) {
        ta.value = value || '';
        ta.dataset.path = path;
        ta.dataset.language = language || ta.dataset.language;
        return;
      }
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
      if (!monaco) {
        return;
      }
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

  function watchValue(id, receiver) {
    valueWatchers.get(id)?.dispose();
    let timer;
    const changed = () => {
      clearTimeout(timer);
      timer = setTimeout(() => receiver.invokeMethodAsync('ValueEdited', getValue(id))
        .catch(error => console.debug('Editor callback ended:', error)), 300);
    };
    const editor = editors.get(id), textarea = fallbacks.get(id);
    const subscription = editor?.onDidChangeModelContent(changed);
    textarea?.addEventListener('input', changed);
    valueWatchers.set(id, { dispose() {
      clearTimeout(timer);
      subscription?.dispose();
      textarea?.removeEventListener('input', changed);
    } });
  }

  function setValue(id, value, language) {
    try {
      const ta = fallbacks.get(id);
      if (ta) {
        ta.value = value || '';
        ta.dataset.language = language || ta.dataset.language;
        return;
      }
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
      if (ta) {
        return ta.value;
      }
      const editor = editors.get(id);
      if (editor && editor.getModel()) {
        return editor.getValue();
      }
    } catch (e) {
      console.warn('pcmonaco.getValue error:', e);
    }
    return null;
  }

  // Insert text at the caret, replacing the selection. Used by the workflow query studio's
  // column/filter helpers. Returns the new editor text, or null while Monaco is still loading.
  function insertText(id, text) {
    try {
      const ta = fallbacks.get(id);
      if (ta) {
        const start = ta.selectionStart ?? ta.value.length;
        const end = ta.selectionEnd ?? start;
        ta.value = ta.value.slice(0, start) + (text || '') + ta.value.slice(end);
        const caret = start + (text || '').length;
        ta.selectionStart = ta.selectionEnd = caret;
        ta.focus();
        return ta.value;
      }
      const editor = editors.get(id);
      if (editor && editor.getModel()) {
        const selection = editor.getSelection() || editor.getModel().getFullModelRange();
        editor.executeEdits('pcmonaco.insertText', [
          { range: selection, text: text || '', forceMoveMarkers: true },
        ]);
        editor.focus();
        return editor.getValue();
      }
      if (initLocks.has(id)) {
        return initLocks.get(id).then(() => insertText(id, text));
      }
    } catch (e) {
      console.warn('pcmonaco.insertText error:', e);
    }
    return null;
  }

  function clearLoading(id) {
    const loading = loadingViews.get(id);
    if (!loading) return;
    loading.skeleton.remove();
    loading.el.classList.remove('pcmonaco-pending');
    loading.el.removeAttribute('aria-busy');
    loadingViews.delete(id);
  }

  function destroy(id, preserveLoading = false) {
    valueWatchers.get(id)?.dispose();
    valueWatchers.delete(id);
    if (!preserveLoading) clearLoading(id);
    try {
      const ta = fallbacks.get(id);
      if (ta) {
        ta.remove();
        fallbacks.delete(id);
      }
      const editor = editors.get(id);
      if (!editor) {
        return;
      }
      editor.dispose();
      editors.delete(id);
      // Dispose all models belonging to this editor instance
      const prefix = id + '::';
      for (const [key, model] of models) {
        if (key.startsWith(prefix)) {
          try {
            model.dispose();
          } catch {
            /* already disposed */
          }
          models.delete(key);
        }
      }
    } catch (e) {
      console.warn('pcmonaco.destroy error:', e);
    }
  }

  function isMounted(id) {
    const host = document.getElementById(id);
    const node = fallbacks.get(id) || editors.get(id)?.getDomNode();
    return !!(host && node && host.contains(node));
  }

  return { isMounted, init, openFile, closeFile, setValue, getValue, insertText, destroy, setSqlSchema, watchValue };
})();

// pcdata — downloads for project data and on-demand run output.
window.pcdata = {
  async downloadStream(filename, stream) {
    const buffer = await stream.arrayBuffer();
    const url = URL.createObjectURL(new Blob([buffer], { type: 'application/octet-stream' }));
    try {
      window.pcdata.download(filename, url);
    } finally {
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
  },
  download(filename, dataUri) {
    const a = document.createElement('a');
    a.href = dataUri;
    a.download = filename || 'export.csv';
    document.body.appendChild(a);
    a.click();
    a.remove();
  },
};
