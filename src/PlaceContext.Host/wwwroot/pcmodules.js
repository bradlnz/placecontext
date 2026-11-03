// Register interop before Blazor starts; fetch optional UI code only on first use.
(() => {
  const sources = document.currentScript.dataset;
  const loads = new Map();

  function load(module) {
    if (!loads.has(module)) {
      const pending = new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = sources[module];
        script.onload = resolve;
        script.onerror = () => {
          script.remove();
          reject(new Error(`${module} failed to load.`));
        };
        document.head.appendChild(script);
      }).catch((error) => {
        loads.delete(module);
        throw error;
      });
      loads.set(module, pending);
    }
    return loads.get(module);
  }

  function register(namespace, module, methods) {
    window[namespace] = Object.fromEntries(
      methods.map((method) => [
        method,
        async function invoke(...args) {
          await load(module);
          if (window[namespace][method] === invoke) {
            loads.delete(module);
            throw new Error(`${namespace}.${method} was not registered.`);
          }
          return window[namespace][method](...args);
        },
      ]),
    );
  }

  register('pcgraph', 'pcgraph', ['init', 'append', 'select', 'destroy', 'splitter']);
  register('pcmonaco', 'pcmonaco', [
    'isMounted',
    'init',
    'openFile',
    'closeFile',
    'setValue',
    'getValue',
    'destroy',
    'setSqlSchema',
  ]);
  register('pcdata', 'pcmonaco', ['download', 'downloadStream']);
  register('pcstudio', 'pcstudio', ['splitter']);
  register('pcmap', 'pcmap', ['render', 'destroy']);
})();
