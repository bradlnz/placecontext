document.querySelectorAll("[data-copy]").forEach((button) => {
  button.addEventListener("click", async () => {
    await navigator.clipboard.writeText(button.dataset.copy);
    const original = button.textContent;
    button.textContent = "Copied";
    setTimeout(() => { button.textContent = original; }, 1600);
  });
});

// Product console showcase: auto-rotating view tabs (no dependencies).
(() => {
  const shell = document.querySelector(".mock-shell");
  if (!shell) return;
  const tabs = Array.from(shell.querySelectorAll('[role="tab"][data-view]'));
  const views = Array.from(shell.querySelectorAll(".mock-view[data-view]"));
  if (!tabs.length || !views.length) return;
  const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
  const INTERVAL_MS = 4500;
  let timer = null;

  const activate = (key) => {
    tabs.forEach((tab) => {
      const on = tab.dataset.view === key;
      tab.classList.toggle("is-active", on);
      tab.setAttribute("aria-selected", on ? "true" : "false");
    });
    views.forEach((view) => view.classList.toggle("is-active", view.dataset.view === key));
  };

  const stop = () => {
    if (timer) { clearInterval(timer); timer = null; }
  };

  const start = () => {
    if (timer || reducedMotion.matches) return;
    timer = setInterval(() => {
      const current = tabs.findIndex((tab) => tab.classList.contains("is-active"));
      activate(tabs[(current + 1) % tabs.length].dataset.view);
    }, INTERVAL_MS);
  };

  tabs.forEach((tab) => tab.addEventListener("click", () => activate(tab.dataset.view)));
  shell.addEventListener("mouseenter", stop);
  shell.addEventListener("mouseleave", start);
  shell.addEventListener("focusin", stop);
  shell.addEventListener("focusout", start);
  if (reducedMotion.addEventListener) {
    reducedMotion.addEventListener("change", () => (reducedMotion.matches ? stop() : start()));
  }
  start();
})();
