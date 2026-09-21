// =========================================================
// PHASE 18/19 - Booking page client behaviour
// - Selection & continuity validation
// - In-place gap error message ("Cannot select time slot with gap.")
// - Shake animation for rejected slots
// - Live AJAX price calculation
// - Responsive auto-scroll directly to Step 4 (Your information) on mobile
// =========================================================
document.addEventListener('DOMContentLoaded', function () {
    'use strict';

    const form = document.getElementById('bookingForm');
    const checkboxes = Array.from(document.querySelectorAll('.timeslot-checkbox'));
    const validationSummary = document.getElementById('validationSummary');
    const courtSelect = document.getElementById('courtSelect');
    const bookingDateInput = document.getElementById('bookingDate');
    const bookingSummaryContainer = document.getElementById('bookingSummaryContainer');

    const timeslotGapAlert = document.getElementById('timeslotGapAlert');
    const timeslotGapAlertClose = document.getElementById('timeslotGapAlertClose');
    const timeslotGapAlertTitle = document.getElementById('timeslotGapAlertTitle');

    const step4Card = document.getElementById('step4Card');
    const confirmActionsContainer = document.getElementById('confirmActionsContainer');
    const mobileProceedToStep4Container = document.getElementById('mobileProceedToStep4Container');
    const btnMobileProceedToStep4 = document.getElementById('btnMobileProceedToStep4');

    let autoScrollTimer = null;
    let priceFetchController = null;

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

    function fetchLivePrice() {
        const selectedIds = getSelectedSlotIds();
        if (selectedIds.length === 0 || !courtSelect || !bookingDateInput) { return; }
        const courtId = courtSelect.value;
        const dateVal = bookingDateInput.value;
        if (!courtId || !dateVal) { return; }

        if (priceFetchController) {
            priceFetchController.abort();
        }
        priceFetchController = new AbortController();

        const params = new URLSearchParams();
        params.append('courtId', courtId);
        params.append('date', dateVal);
        selectedIds.forEach(function (id) { params.append('slotIds', id); });

        fetch(`/Booking?handler=Price&${params.toString()}`, {
            signal: priceFetchController.signal,
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        })
        .then(function (res) { return res.json(); })
        .then(function (data) {
            if (data && data.success) {
                const priceContainer = document.getElementById('priceContainer');
                const summaryPrice = document.getElementById('summaryPrice');
                const step4PriceAlert = document.getElementById('step4PriceAlert');
                const step4PriceValue = document.getElementById('step4PriceValue');

                if (priceContainer && summaryPrice) {
                    summaryPrice.textContent = data.formattedPrice;
                    priceContainer.style.display = 'flex';
                }
                if (step4PriceAlert && step4PriceValue) {
                    step4PriceValue.textContent = data.formattedPrice;
                    step4PriceAlert.style.display = 'block';
                }
            }
        })
        .catch(function (err) {
            if (err.name !== 'AbortError') {
                console.warn('Could not fetch price', err);
            }
        });
    }

    function scrollToStep4() {
        const target = document.getElementById('step4Card') || document.getElementById('step4Heading');
        if (!target) { return; }

        target.style.display = 'block';
        if (confirmActionsContainer) {
            confirmActionsContainer.style.display = 'grid';
        }

        target.scrollIntoView({ behavior: 'smooth', block: 'start' });
        const nameInput = document.getElementById('Input_CustomerName');
        if (nameInput) {
            setTimeout(function () {
                nameInput.focus({ preventScroll: true });
            }, 600);
        }
    }

    if (btnMobileProceedToStep4) {
        btnMobileProceedToStep4.addEventListener('click', function () {
            scrollToStep4();
        });
    }

    function updateSummary() {
        const selected = getSelectedCheckboxes();

        if (selected.length === 0) {
            bookingSummaryContainer.style.display = 'none';
            if (step4Card && step4Card.dataset.hasCalculatedPrice !== 'true') {
                step4Card.style.display = 'none';
            }
            if (confirmActionsContainer && (!step4Card || step4Card.dataset.hasCalculatedPrice !== 'true')) {
                confirmActionsContainer.style.display = 'none';
            }
            if (mobileProceedToStep4Container) {
                mobileProceedToStep4Container.style.display = 'none';
            }
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

        if (step4Card) {
            step4Card.style.display = 'block';
        }
        if (confirmActionsContainer) {
            confirmActionsContainer.style.display = 'grid';
        }
        if (mobileProceedToStep4Container) {
            mobileProceedToStep4Container.style.display = 'block';
        }

        fetchLivePrice();
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
            hideGapAlert();

            if (autoScrollTimer) {
                clearTimeout(autoScrollTimer);
                autoScrollTimer = null;
            }

            const card = checkbox.closest('.timeslot-btn');

            if (!checkbox.checked) {
                updateCardAppearance();
                updateSummary();
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

            updateCardAppearance();
            updateSummary();

            // Mobile view: automatically smooth-scroll down directly to Step 4 after selection
            if (window.innerWidth < 992) {
                autoScrollTimer = setTimeout(function () {
                    scrollToStep4();
                }, 800);
            }
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

    // If loaded on mobile with Step 4 already active (e.g. calculated price or validation errors)
    const hasCalculatedPrice = step4Card && step4Card.dataset.hasCalculatedPrice === 'true';
    const hasValidationErrors = document.querySelector('.field-validation-error, .validation-summary-errors');
    if ((hasCalculatedPrice || hasValidationErrors) && window.innerWidth < 992) {
        setTimeout(function () {
            scrollToStep4();
        }, 300);
    }
});
