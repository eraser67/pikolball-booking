// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// =========================================================
// PHASE 18 - Progressive enhancement (loading states only).
// Purely cosmetic: if JavaScript is unavailable, forms still
// submit normally because this only runs on submit.
// =========================================================
(function () {
  "use strict";

  function showOverlay(text) {
    var overlay = document.getElementById("pbLoadingOverlay");
    if (!overlay) return;
    var label = overlay.querySelector("[data-pb-loading-text]");
    if (label && text) label.textContent = text;
    overlay.classList.add("is-visible");
  }

  function setButtonLoading(button) {
    if (!button || button.classList.contains("is-loading")) return;
    button.classList.add("is-loading");
    button.setAttribute("aria-busy", "true");
  }

  document.addEventListener("submit", function (event) {
    var form = event.target;
    if (!form || form.dataset.pbNoLoading === "true") return;

    var submitter = event.submitter;
    if (submitter) {
      setButtonLoading(submitter);
    } else {
      var btn = form.querySelector("button[type='submit'], input[type='submit']");
      setButtonLoading(btn);
    }

    showOverlay(form.dataset.pbLoadingText || "Loading\u2026");
  });

  // Keyboard focus outline parity for custom clickable slot cards (already
  // handled in CSS via :focus, this just ensures Space/Enter activate).
  document.addEventListener("keydown", function (event) {
    if (event.key !== " " && event.key !== "Enter") return;
    var el = event.target;
    if (!el || el.tagName !== "LABEL") return;
    if (!el.classList.contains("time-range-card")) return;
    event.preventDefault();
    el.click();
  });
})();
