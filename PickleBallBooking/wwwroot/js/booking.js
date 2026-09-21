// =========================================================
// PHASE 18/19 - Booking page client behaviour
// - Court & Date selection updates slots dynamically via AJAX (NO PAGE REFRESH)
// - In-place gap error message ("Cannot select time slot with gap.")
// - Shake animation for rejected slots
// - Step 4 ("Your Information") only shown after Calculate Price
// - Responsive auto-scroll directly to Step 4 on mobile after Calculate Price
// =========================================================
document.addEventListener('DOMContentLoaded', function () {
    'use strict';

    const form = document.getElementById('bookingForm');
    const validationSummary = document.getElementById('validationSummary');
    const courtSelect = document.getElementById('courtSelect');
    const bookingDateInput = document.getElementById('bookingDate');
    const bookingSummaryContainer = document.getElementById('bookingSummaryContainer');

    const timeslotEmptyState = document.getElementById('timeslotEmptyState');
    const timeslotContent = document.getElementById('timeslotContent');
    const timeslotGrid = document.getElementById('timeslotGrid');

    const timeslotGapAlert = document.getElementById('timeslotGapAlert');
    const timeslotGapAlertClose = document.getElementById('timeslotGapAlertClose');
    const timeslotGapAlertTitle = document.getElementById('timeslotGapAlertTitle');

    const step4Card = document.getElementById('step4Card');

    let slotDisplayTimes = {};

    function cacheDisplayTimes() {
        const checkboxes = Array.from(document.querySelectorAll('.timeslot-checkbox'));
        checkboxes.forEach(function (checkbox) {
            const card = checkbox.closest('.timeslot-btn');
            if (!card) { return; }
            const timeElement = card.querySelector('.timeslot-time');
            if (timeElement) {
                slotDisplayTimes[checkbox.value] = timeElement.textContent.trim();
            }
        });
    }
    cacheDisplayTimes();

    function getAllCheckboxes() {
        return Array.from(document.querySelectorAll('.timeslot-checkbox'));
    }

    function getSelectedCheckboxes() {
        return getAllCheckboxes().filter(function (checkbox) { return checkbox.checked; });
    }

    function getSelectedSlotIds() {
        return getSelectedCheckboxes().map(function (checkbox) {
            return parseInt(checkbox.value, 10);
        });
    }

    function updateCardAppearance() {
        getAllCheckboxes().forEach(function (checkbox) {
            const card = checkbox.closest('.timeslot-btn');
            if (!card) { return; }
            if (checkbox.checked) { card.classList.add('selected'); }
            else { card.classList.remove('selected'); }
        });
    }

    function hideGapAlert() {
        if (timeslotGapAlert) {
            timeslotGapAlert.classList.add('d-none');
        }
    }

    function showGapAlert(message) {
        if (!timeslotGapAlert) { return; }
        if (timeslotGapAlertTitle) {
            timeslotGapAlertTitle.textContent = message || 'Cannot select time slot with gap.';
        }
        timeslotGapAlert.classList.remove('d-none');
        timeslotGapAlert.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    if (timeslotGapAlertClose) {
        timeslotGapAlertClose.addEventListener('click', hideGapAlert);
    }

    function areSlotsContiguous() {
        const selected = getSelectedCheckboxes().map(function (cb) {
            const start = parseInt(cb.dataset.startMinutes || '0', 10);
            const end = parseInt(cb.dataset.endMinutes || '0', 10);
            return { cb: cb, id: parseInt(cb.value, 10), start: start, end: end };
        }).sort(function (a, b) {
            return a.start - b.start;
        });

        if (selected.length <= 1) { return true; }

        for (let i = 1; i < selected.length; i++) {
            if (selected[i].start !== selected[i - 1].end) {
                return false;
            }
        }

        return true;
    }

    function scrollToStep4() {
        const target = document.getElementById('step4Card') || document.getElementById('step4Heading');
        if (!target) { return; }

        target.scrollIntoView({ behavior: 'smooth', block: 'start' });
        const nameInput = document.getElementById('Input_CustomerName');
        if (nameInput) {
            setTimeout(function () {
                nameInput.focus({ preventScroll: true });
            }, 500);
        }
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
        const sortedSelected = selected.slice().sort(function (a, b) {
            return parseInt(a.dataset.startMinutes || '0', 10) - parseInt(b.dataset.startMinutes || '0', 10);
        });
        const firstId = sortedSelected[0].value;
        const lastId = sortedSelected[sortedSelected.length - 1].value;
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

    // Event delegation on timeslot grid for all checkbox changes
    if (timeslotGrid) {
        timeslotGrid.addEventListener('change', function (event) {
            const checkbox = event.target;
            if (!checkbox || !checkbox.classList.contains('timeslot-checkbox')) { return; }

            clearValidationError();
            hideGapAlert();

            const card = checkbox.closest('.timeslot-btn');

            if (!checkbox.checked) {
                updateCardAppearance();
                updateSummary();
                if (step4Card) {
                    step4Card.style.display = 'none';
                    const confirmActions = document.getElementById('confirmActionsContainer');
                    if (confirmActions) { confirmActions.style.display = 'none'; }
                }
                return;
            }

            if (!areSlotsContiguous()) {
                checkbox.checked = false;
                if (card) {
                    card.classList.add('shake-error');
                    setTimeout(function () {
                        card.classList.remove('shake-error');
                    }, 500);
                }
                showGapAlert('Cannot select time slot with gap.');
                showValidationError('Cannot select time slot with gap.');
                updateCardAppearance();
                updateSummary();
                return;
            }

            // Valid continuous slot selection
            updateCardAppearance();
            updateSummary();

            // If user alters selection after calculating price, hide step 4 until recalculated
            if (step4Card) {
                step4Card.style.display = 'none';
                const confirmActions = document.getElementById('confirmActionsContainer');
                if (confirmActions) { confirmActions.style.display = 'none'; }
            }
        });
    }

    // Fetch slots dynamically via AJAX without refreshing the link
    function loadSlotsAjax(courtId, dateVal) {
        if (!courtId) {
            if (timeslotEmptyState) { timeslotEmptyState.style.display = 'block'; }
            if (timeslotContent) { timeslotContent.style.display = 'none'; }
            if (bookingSummaryContainer) { bookingSummaryContainer.style.display = 'none'; }
            if (step4Card) { step4Card.style.display = 'none'; }
            return;
        }

        if (timeslotEmptyState) { timeslotEmptyState.style.display = 'none'; }
        if (timeslotContent) { timeslotContent.style.display = 'block'; }
        if (timeslotGrid) {
            timeslotGrid.style.opacity = '0.5';
            timeslotGrid.style.pointerEvents = 'none';
        }

        // Hide old gap alert and previous step 4 calculation
        hideGapAlert();
        if (step4Card) {
            step4Card.style.display = 'none';
            const confirmActions = document.getElementById('confirmActionsContainer');
            if (confirmActions) { confirmActions.style.display = 'none'; }
        }

        fetch(`/Booking?handler=Slots&courtId=${encodeURIComponent(courtId)}&date=${encodeURIComponent(dateVal)}`, {
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        })
        .then(function (res) { return res.json(); })
        .then(function (data) {
            if (timeslotGrid) {
                timeslotGrid.style.opacity = '1';
                timeslotGrid.style.pointerEvents = 'auto';
            }

            if (!data || !data.success || !Array.isArray(data.slots)) {
                if (timeslotEmptyState) { timeslotEmptyState.style.display = 'block'; }
                if (timeslotContent) { timeslotContent.style.display = 'none'; }
                return;
            }

            slotDisplayTimes = {};
            let html = '';

            data.slots.forEach(function (slot) {
                slotDisplayTimes[slot.timeSlotId] = slot.displayTime;

                const slotStatusClass = slot.status || 'unavailable';
                const isDisabled = !slot.isAvailable;
                const displayStatus = slot.isPast ? 'passed' : slot.status;
                const ariaLabel = `${slot.displayTime} - ${displayStatus}`;

                html += `
                    <label class="timeslot-btn ${slotStatusClass}"
                           title="${ariaLabel}"
                           data-slot-id="${slot.timeSlotId}"
                           data-start-minutes="${slot.startMinutes}"
                           data-end-minutes="${slot.endMinutes}">

                        <input type="checkbox"
                               name="SelectedSlotIds"
                               value="${slot.timeSlotId}"
                               class="timeslot-checkbox"
                               aria-label="${ariaLabel}"
                               data-slot-id="${slot.timeSlotId}"
                               data-start-minutes="${slot.startMinutes}"
                               data-end-minutes="${slot.endMinutes}"
                               ${isDisabled ? 'disabled' : ''} />

                        <span class="timeslot-time">
                            ${slot.displayTime}
                        </span>

                        <span class="timeslot-status">
                            ${displayStatus}
                        </span>

                    </label>
                `;
            });

            if (timeslotGrid) {
                timeslotGrid.innerHTML = html;
            }

            updateCardAppearance();
            updateSummary();
        })
        .catch(function (err) {
            console.error('Failed to load slots', err);
            if (timeslotGrid) {
                timeslotGrid.style.opacity = '1';
                timeslotGrid.style.pointerEvents = 'auto';
            }
        });
    }

    // Court select: dynamic update WITHOUT refreshing the page link
    if (courtSelect) {
        courtSelect.addEventListener('change', function () {
            const selectedCourt = this.value;
            const selectedDate = bookingDateInput ? bookingDateInput.value : '';

            const summaryCourt = document.getElementById('summaryCourtName');
            if (summaryCourt) {
                summaryCourt.textContent = this.options[this.selectedIndex]?.text || '-';
            }

            loadSlotsAjax(selectedCourt, selectedDate);
        });
    }

    // Booking date input: dynamic update WITHOUT refreshing the page link
    if (bookingDateInput) {
        bookingDateInput.addEventListener('change', function () {
            const selectedDate = this.value;
            const selectedCourt = courtSelect ? courtSelect.value : '';

            const summaryDate = document.getElementById('summaryDate');
            if (selectedDate && summaryDate) {
                const date = new Date(selectedDate + 'T00:00:00');
                summaryDate.textContent = date.toLocaleDateString('en-US', {
                    weekday: 'long', year: 'numeric', month: 'long', day: 'numeric'
                });
            }

            if (selectedCourt) {
                loadSlotsAjax(selectedCourt, selectedDate);
            }
        });
    }

    if (form) {
        form.addEventListener('submit', function (event) {
            const selectedCount = getSelectedSlotIds().length;
            if (selectedCount === 0) {
                event.preventDefault();
                showGapAlert('Please select at least one time slot.');
                showValidationError('Please select at least one time slot.');
                return false;
            }
            return true;
        });
    }

    // Initial page state
    updateCardAppearance();
    updateSummary();

    // After clicking Calculate Price (or on form reload), Step 4 is rendered in the DOM.
    // On mobile responsive views (< 992px), automatically scroll down to Step 4 ("Your Information")
    if (step4Card && window.innerWidth < 992) {
        setTimeout(function () {
            scrollToStep4();
        }, 300);
    }
});
