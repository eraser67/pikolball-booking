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

  // ---------------------------------------------------------
  // Reusable confirmation modal (Bootstrap) for destructive
  // forms. Forms opt in with data-pb-confirm="<message>".
  // Functionality is unchanged: confirming submits the exact
  // same form; cancelling simply closes the modal.
  //
  // Note the two distinct attributes involved:
  //   data-pb-confirm-ok      -> on the <form>: the confirm
  //                              button's label text.
  //   data-pb-confirm-accept  -> on the modal's confirm button:
  //                              the marker the click handler looks for.
  // Keeping them separate ensures clicking the form's own submit
  // button can never be mistaken for pressing "Confirm".
  // ---------------------------------------------------------
  var pendingForm = null;
  var pendingSubmitter = null;
  var confirmModalHiddenBound = false;

  function getConfirmModal() {
    return document.getElementById("pbConfirmModal");
  }

  function clearPending() {
    pendingForm = null;
    pendingSubmitter = null;
  }

  function submitConfirmedForm(form, submitter) {
    // Briefly flag the form so the submit interceptor lets this one submission
    // through. The flag is removed immediately afterwards so it can never leak
    // into a later submission (which would skip the confirmation modal).
    form.dataset.pbConfirmed = "true";
    try {
      if (form.requestSubmit) {
        // requestSubmit runs validation and fires the submit event, honoring
        // the original submit button so the correct value/handler is posted.
        form.requestSubmit(submitter || null);
      } else {
        form.submit();
      }
    } finally {
      delete form.dataset.pbConfirmed;
    }
  }

  function openConfirmModal(form, submitter) {
    var modalEl = getConfirmModal();
    if (!modalEl || !window.bootstrap || !window.bootstrap.Modal) {
      // Fallback to native confirm if the Bootstrap modal is unavailable.
      if (window.confirm(form.dataset.pbConfirm)) {
        submitConfirmedForm(form, submitter);
      }
      return;
    }

    pendingForm = form;
    pendingSubmitter = submitter || null;

    var modal = window.bootstrap.Modal.getOrCreateInstance(modalEl);

    // Whenever the modal closes for any reason (Cancel, backdrop, Esc, or the
    // accept button), drop the pending form. This prevents a later click from
    // submitting stale state without confirmation.
    if (!confirmModalHiddenBound) {
      modalEl.addEventListener("hidden.bs.modal", clearPending);
      confirmModalHiddenBound = true;
    }

    var messageEl = modalEl.querySelector("[data-pb-confirm-message]");
    var titleEl = modalEl.querySelector("#pbConfirmModalLabel");
    var okBtn = modalEl.querySelector("[data-pb-confirm-accept]");

    if (messageEl) messageEl.textContent = form.dataset.pbConfirm || "Are you sure?";
    if (titleEl) titleEl.textContent = form.dataset.pbConfirmTitle || "Please confirm";
    if (okBtn) {
      okBtn.textContent = form.dataset.pbConfirmOk || "Confirm";
      var variant = form.dataset.pbConfirmVariant || "danger";
      okBtn.className = "btn btn-" + variant;
    }

    modal.show();
  }

  document.addEventListener("click", function (event) {
    // Only the modal's own accept button confirms. It uses a dedicated marker
    // (data-pb-confirm-accept) so closest() cannot accidentally match the
    // destructive <form>, which carries data-pb-confirm-ok as its button label.
    var okBtn = event.target.closest && event.target.closest("[data-pb-confirm-accept]");
    if (!okBtn) return;

    var form = pendingForm;
    var submitter = pendingSubmitter;
    clearPending();

    var modalEl = getConfirmModal();
    if (modalEl && window.bootstrap && window.bootstrap.Modal) {
      window.bootstrap.Modal.getOrCreateInstance(modalEl).hide();
    }

    if (!form) return;

    // Confirmed: proceed with the original submission.
    submitConfirmedForm(form, submitter);
  });

  document.addEventListener("submit", function (event) {
    var form = event.target;
    if (!form || form.dataset.pbNoLoading === "true") return;

    // If an earlier handler already prevented the submission (such as when the
    // user dismisses a confirmation popup), do NOT show the loading state.
    // Otherwise the overlay/button spinner would be stuck because the form
    // never submits.
    if (event.defaultPrevented) return;

    // Intercept destructive forms and ask for confirmation via the modal.
    if (form.dataset.pbConfirm && form.dataset.pbConfirmed !== "true") {
      event.preventDefault();
      openConfirmModal(form, event.submitter);
      return;
    }

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
