document.querySelectorAll("[data-copy]").forEach((button) => {
  button.addEventListener("click", async () => {
    await navigator.clipboard.writeText(button.dataset.copy);
    const original = button.textContent;
    button.textContent = "Copied";
    setTimeout(() => { button.textContent = original; }, 1600);
  });
});

// Product console showcase: auto-rotating app views (no dependencies).
(() => {
  const showcase = document.querySelector(".showcase");
  if (!showcase) return;
  const tabs = Array.from(showcase.querySelectorAll('.console-picker [role="tab"][data-view]'));
  const views = Array.from(showcase.querySelectorAll(".app-view[data-view]"));
  if (!tabs.length || !views.length) return;
  const title = document.getElementById("console-title");
  const subtitle = document.getElementById("console-subtitle");
  const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
  const INTERVAL_MS = 4500;
  let timer = null;

  const activate = (key) => {
    tabs.forEach((tab) => {
      const on = tab.dataset.view === key;
      tab.classList.toggle("is-active", on);
      tab.setAttribute("aria-selected", on ? "true" : "false");
      if (on && title && subtitle) {
        title.textContent = tab.dataset.title || tab.textContent;
        subtitle.textContent = tab.dataset.subtitle || "";
      }
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
  showcase.addEventListener("mouseenter", stop);
  showcase.addEventListener("mouseleave", start);
  showcase.addEventListener("focusin", stop);
  showcase.addEventListener("focusout", start);
  if (reducedMotion.addEventListener) {
    reducedMotion.addEventListener("change", () => (reducedMotion.matches ? stop() : start()));
  }
  start();
})();
