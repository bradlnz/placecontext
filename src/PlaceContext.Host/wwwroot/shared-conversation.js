document.querySelectorAll('details.shared-artifact').forEach(details => {
  const render = () => {
    if (!details.open) return;
    const pdf = details.querySelector('[data-shared-pdf]');
    if (pdf) void window.placecontext.renderPdf(pdf.id, pdf.dataset.sharedPdf, false);
  };
  details.addEventListener('toggle', render);
  render();
});
