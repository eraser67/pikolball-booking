// =========================================================
// PHASE 18 - Booking page client behaviour (moved from the
// Razor view for caching/consistency). Logic is UNCHANGED:
// selection, continuity checking, summary and redirects behave
// exactly as before. All authoritative validation stays on the
// server.
// =========================================================
document.addEventListener('DOMContentLoaded', function () {
    'use strict';

    const form = document.getElementById('bookingForm');
    const checkboxes = Array.from(document.querySelectorAll('.timeslot-checkbox'));
    const validationSummary = document.getElementById('validationSummary');
    const courtSelect = document.getElementById('courtSelect');
    const bookingDateInput = document.getElementById('bookingDate');
    const bookingSummaryContainer = document.getElementById('bookingSummaryContainer');

    const slotDisplayTimes = {};
    checkboxes.forEach(function (checkbox) {
        const card = checkbox.closest('.timeslot-btn');
        if (!card) { return; }
        const timeElement = card.querySelector('.timeslot-time');
        if (timeElement) {
            slotDisplayTimes[checkbox.value] = timeElement.textContent.trim();
        }
    });

    function getSelectedCheckboxes() {
        return checkboxes.filter(function (checkbox) { return checkbox.checked; });
    }

    function getSelectedSlotIds() {
        return getSelectedCheckboxes().map(function (checkbox) {
            return parseInt(checkbox.value, 10);
        });
    }

    function updateCardAppearance() {
        checkboxes.forEach(function (checkbox) {
            const card = checkbox.closest('.timeslot-btn');
            if (!card) { return; }
            if (checkbox.checked) { card.classList.add('selected'); }
            else { card.classList.remove('selected'); }
        });
    }

    function areSlotsContiguous() {
        const selected = getSelectedCheckboxes().sort(function (a, b) {
            return parseInt(a.value, 10) - parseInt(b.value, 10);
        });

        if (selected.length <= 1) { return true; }

        for (let i = 1; i < selected.length; i++) {
            const previousId = parseInt(selected[i - 1].value, 10);
            const currentId = parseInt(selected[i].value, 10);
            if (currentId !== previousId + 1) { return false; }
        }

        return true;
    }

    function updateSummary() {
        const selected = getSelectedCheckboxes();

        if (selected.length === 0) {
            bookingSummaryContainer.style.display = 'none';
            return;
        }

        if (courtSelect) {
            const courtName = courtSelect.options[courtSelect.selectedIndex]?.text || 'N/A';
            const summaryCourt = document.getElementById('summaryCourtName');
            if (summaryCourt) { summaryCourt.textContent = courtName; }
        }

        if (bookingDateInput) {
            const dateValue = bookingDateInput.value;
            const summaryDate = document.getElementById('summaryDate');
            if (dateValue && summaryDate) {
                const date = new Date(dateValue + 'T00:00:00');
                summaryDate.textContent = date.toLocaleDateString('en-US', {
                    weekday: 'long', year: 'numeric', month: 'long', day: 'numeric'
                });
            }
        }

        const selectedIds = getSelectedSlotIds();
        const firstId = selectedIds[0];
        const lastId = selectedIds[selectedIds.length - 1];
        const firstTime = slotDisplayTimes[firstId] || '';
        const lastTime = slotDisplayTimes[lastId] || '';
        const lastEndTime = lastTime.split('-')[1]?.trim() || '';

        const summaryTime = document.getElementById('summaryTime');
        if (summaryTime) { summaryTime.textContent = `${firstTime} to ${lastEndTime}`; }

        const summaryDuration = document.getElementById('summaryDuration');
        if (summaryDuration) { summaryDuration.textContent = `${selectedIds.length} hour(s)`; }

        bookingSummaryContainer.style.display = 'block';
    }

    function clearValidationError() {
        if (!validationSummary) { return; }
        validationSummary.style.display = 'none';
        validationSummary.innerHTML = '<strong>Please correct the following errors:</strong>';
    }

    function showValidationError(message) {
        if (!validationSummary) { return; }
        validationSummary.style.display = 'block';
        validationSummary.innerHTML = `<strong>Error:</strong> ${message}`;
    }

    checkboxes.forEach(function (checkbox) {
        checkbox.addEventListener('change', function () {
            clearValidationError();

            if (!checkbox.checked) {
                updateCardAppearance();
                updateSummary();
                return;
            }

            if (!areSlotsContiguous()) {
                checkbox.checked = false;
                showValidationError('Time slots must be continuous (no gaps allowed).');
                updateCardAppearance();
                updateSummary();
                return;
            }

            updateCardAppearance();
            updateSummary();
        });
    });

    if (courtSelect) {
        courtSelect.addEventListener('change', function () {
            const selectedCourt = this.value;
            if (!selectedCourt) { return; }
            const selectedDate = bookingDateInput ? bookingDateInput.value : '';
            if (selectedDate) {
                window.location.href = `/Booking?courtId=${selectedCourt}&date=${selectedDate}`;
            } else {
                window.location.href = `/Booking?courtId=${selectedCourt}`;
            }
        });
    }

    if (bookingDateInput) {
        bookingDateInput.addEventListener('change', function () {
            const selectedDate = this.value;
            if (!selectedDate) { return; }
            if (courtSelect && courtSelect.value) {
                window.location.href = `/Booking?courtId=${courtSelect.value}&date=${selectedDate}`;
            }
        });
    }

    if (form) {
        form.addEventListener('submit', function (event) {
            const selectedCount = getSelectedSlotIds().length;
            if (selectedCount === 0) {
                event.preventDefault();
                showValidationError('Please select at least one time slot.');
                return false;
            }
            return true;
        });
    }

    updateCardAppearance();
    updateSummary();
});
