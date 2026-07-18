/* ==========================================================================
   Feasibility Reports theme — progressive enhancement only.
   The store works without JavaScript; this adds small conveniences.
   ========================================================================== */

(function () {
  'use strict';

  /* Mobile navigation toggle */
  var header = document.querySelector('[data-header]');
  var navToggle = document.querySelector('[data-nav-toggle]');

  if (header && navToggle) {
    navToggle.addEventListener('click', function () {
      var isOpen = header.classList.toggle('nav-open');
      navToggle.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
    });
  }

  /* Add-to-cart busy state (fires only after native validation passes) */
  document.querySelectorAll('[data-product-form]').forEach(function (form) {
    form.addEventListener('submit', function () {
      var button = form.querySelector('[data-submit-button]');
      if (button) {
        button.disabled = true;
        button.textContent = button.getAttribute('data-adding-text') || 'Adding…';
      }
    });
  });

  /* FAQ accordion: close other items when one opens */
  var faqItems = document.querySelectorAll('.faq__item');
  faqItems.forEach(function (item) {
    item.addEventListener('toggle', function () {
      if (!item.open) return;
      faqItems.forEach(function (other) {
        if (other !== item) other.open = false;
      });
    });
  });
})();
