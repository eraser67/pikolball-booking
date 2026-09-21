// =========================================================
// PHASE 18/19/UX - Mobile-First Customer Booking Experience
// - Dynamic AJAX slot updates on Court / Date change
// - Multi-date selection persistence (overnight booking support)
// - Friendly, actionable gap error guidance with exact slot instructions
// - Next valid slot indicator (.can-extend)
// - Clear overnight transition summary (Sep 22 10 PM - 12 AM ↓ Sep 23 12 AM - 2 AM)
// - Mobile sticky summary bar with quick calculate / proceed
// - Step indicator progress synchronization
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
    const timeslotGapAlertDetail = document.getElementById('timeslotGapAlertDetail');

    const overnightContinueAlert = document.getElementById('overnightContinueAlert');
    const overnightContinueAlertClose = document.getElementById('overnightContinueAlertClose');
    const overnightContinueTitle = document.getElementById('overnightContinueTitle');
    const overnightContinueDetail = document.getElementById('overnightContinueDetail');

    const step4Card = document.getElementById('step4Card');
    const hiddenSelectedSlotsContainer = document.getElementById('hiddenSelectedSlotsContainer');
    const mobileStickySummary = document.getElementById('mobileStickySummary');
    const btnMobileProceed = document.getElementById('btnMobileProceed');

    // Selection state keyed by date string (e.g., "2026-09-22" -> Array of slot objects)
    let selectedSlotsByDate = {};
    let slotDisplayTimes = {};

    function getFormattedDate(dateStr) {
        if (!dateStr) return '';
        const d = new Date(dateStr + 'T00:00:00');
        return d.toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' });
    }

    function getShortFormattedDate(dateStr) {
        if (!dateStr) return '';
        const d = new Date(dateStr + 'T00:00:00');
        return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
    }

    function getFullFormattedDate(dateStr) {
        if (!dateStr) return '';
        const d = new Date(dateStr + 'T00:00:00');
        return d.toLocaleDateString('en-US', { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' });
    }

    function addDays(dateStr, days) {
        const d = new Date(dateStr + 'T00:00:00');
        d.setDate(d.getDate() + days);
        const y = d.getFullYear();
        const m = String(d.getMonth() + 1).padStart(2, '0');
        const day = String(d.getDate()).padStart(2, '0');
        return `${y}-${m}-${day}`;
    }

    function minutesTo12Hour(minutes) {
        let m = minutes % 1440;
        let hours = Math.floor(m / 60);
        const mins = m % 60;
        const period = hours >= 12 && hours < 24 ? 'PM' : 'AM';
        hours = hours % 12;
        if (hours === 0) hours = 12;
        const minsStr = mins > 0 ? `:${String(mins).padStart(2, '0')}` : ':00';
        return `${hours}${minsStr} ${period}`;
    }

    function minutesRangeTo12Hour(startMin, endMin) {
        return `${minutesTo12Hour(startMin)} – ${minutesTo12Hour(endMin)}`;
    }

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

    // Reset price and confirmation displays when slot selections change
    function resetPriceDisplay() {
        if (step4Card) {
            step4Card.style.display = 'none';
        }
        const confirmActions = document.getElementById('confirmActionsContainer');
        if (confirmActions) {
            confirmActions.style.display = 'none';
        }
        const priceContainer = document.getElementById('priceContainer');
        if (priceContainer) {
            priceContainer.style.display = 'none';
        }
        const summaryPrice = document.getElementById('summaryPrice');
        if (summaryPrice) {
            summaryPrice.textContent = '-';
        }
        if (mobileSummaryPrice) {
            mobileSummaryPrice.style.display = 'none';
        }
    }

    // Initialize selection state from DOM checkboxes and hidden container on page load
    function initSelectionFromDom() {
        const currentDate = bookingDateInput ? bookingDateInput.value : '';
        if (!currentDate) return;

        selectedSlotsByDate[currentDate] = [];
        const checkedBoxes = Array.from(document.querySelectorAll('.timeslot-checkbox:checked'));
        checkedBoxes.forEach(function (cb) {
            const id = parseInt(cb.value, 10);
            const start = parseInt(cb.dataset.startMinutes || '0', 10);
            const end = parseInt(cb.dataset.endMinutes || '0', 10);
            const card = cb.closest('.timeslot-btn');
            const timeStr = card ? card.querySelector('.timeslot-time')?.textContent.trim() : (slotDisplayTimes[id] || '');

            selectedSlotsByDate[currentDate].push({
                id: id,
                displayTime: timeStr,
                startMinutes: start,
                endMinutes: end
            });
        });

        if (selectedSlotsByDate[currentDate].length === 0) {
            delete selectedSlotsByDate[currentDate];
        }

        // Restore any slots passed from server for Day 2 across midnight in hiddenSelectedSlotsContainer
        if (hiddenSelectedSlotsContainer) {
            const hiddenInputs = Array.from(hiddenSelectedSlotsContainer.querySelectorAll('input[type="hidden"][data-date]'));
            hiddenInputs.forEach(function (input) {
                const date = input.dataset.date;
                const id = parseInt(input.value, 10);
                if (!date || isNaN(id)) return;

                if (!selectedSlotsByDate[date]) {
                    selectedSlotsByDate[date] = [];
                }

                if (!selectedSlotsByDate[date].some(function (s) { return s.id === id; })) {
                    const start = parseInt(input.dataset.startMinutes || '0', 10);
                    const end = parseInt(input.dataset.endMinutes || '0', 10);
                    const timeStr = input.dataset.displayTime || (slotDisplayTimes[id] || '');

                    selectedSlotsByDate[date].push({
                        id: id,
                        displayTime: timeStr,
                        startMinutes: start,
                        endMinutes: end
                    });
                }
            });
        }
    }
    initSelectionFromDom();

    function getAllCheckboxes() {
        return Array.from(document.querySelectorAll('.timeslot-checkbox'));
    }

    function getSelectedDatesSorted() {
        return Object.keys(selectedSlotsByDate)
            .filter(function (date) { return selectedSlotsByDate[date] && selectedSlotsByDate[date].length > 0; })
            .sort();
    }

    function getAllSelectedSlotsChronological() {
        const dates = getSelectedDatesSorted();
        const allSlots = [];
        dates.forEach(function (date) {
            const slots = selectedSlotsByDate[date].slice().sort(function (a, b) {
                return a.startMinutes - b.startMinutes;
            });
            slots.forEach(function (s) {
                allSlots.push({
                    date: date,
                    id: s.id,
                    displayTime: s.displayTime,
                    startMinutes: s.startMinutes,
                    endMinutes: s.endMinutes
                });
            });
        });
        return allSlots;
    }

    function getTotalSelectedCount() {
        return getAllSelectedSlotsChronological().length;
    }

    function hideGapAlert() {
        if (timeslotGapAlert) {
            timeslotGapAlert.classList.add('d-none');
        }
    }

    function showGapAlert(title, detail) {
        if (!timeslotGapAlert) { return; }
        if (timeslotGapAlertTitle) {
            timeslotGapAlertTitle.textContent = title || 'Please select continuous time slots.';
        }
        if (timeslotGapAlertDetail) {
            timeslotGapAlertDetail.textContent = detail || '';
            timeslotGapAlertDetail.style.display = detail ? 'block' : 'none';
        }
        timeslotGapAlert.classList.remove('d-none');
        timeslotGapAlert.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    if (timeslotGapAlertClose) {
        timeslotGapAlertClose.addEventListener('click', hideGapAlert);
    }

    if (overnightContinueAlertClose) {
        overnightContinueAlertClose.addEventListener('click', function () {
            if (overnightContinueAlert) {
                overnightContinueAlert.classList.add('d-none');
            }
        });
    }

    function updateOvernightContinueBanner() {
        if (!overnightContinueAlert || !bookingDateInput) return;
        const currentDate = bookingDateInput.value;
        if (!currentDate) {
            overnightContinueAlert.classList.add('d-none');
            return;
        }

        const prevDate = addDays(currentDate, -1);
        const nextDate = addDays(currentDate, 1);

        const prevSlots = selectedSlotsByDate[prevDate] || [];
        const nextSlots = selectedSlotsByDate[nextDate] || [];
        const currentSlots = selectedSlotsByDate[currentDate] || [];

        // Scenario 1: Previous date has slots ending at midnight (slot 24, 23:00 - 00:00)
        const prevEndsMidnight = prevSlots.some(function (s) { return s.endMinutes === 1440; });
        if (prevEndsMidnight) {
            const sortedPrev = prevSlots.slice().sort(function (a, b) { return a.startMinutes - b.startMinutes; });
            const prevRange = `${minutesTo12Hour(sortedPrev[0].startMinutes)} – 12:00 AM`;

            if (currentSlots.length === 0) {
                overnightContinueTitle.textContent = `Continuing booking from ${getShortFormattedDate(prevDate)} (${prevRange})`;
                overnightContinueDetail.textContent = `Select 12:00 AM – 1:00 AM to extend your booking into ${getShortFormattedDate(currentDate)}.`;
                overnightContinueAlert.classList.remove('d-none');
                return;
            } else {
                overnightContinueTitle.textContent = `Overnight booking active`;
                overnightContinueDetail.textContent = `Continuing from ${getShortFormattedDate(prevDate)} (${prevRange}) across midnight into ${getShortFormattedDate(currentDate)}.`;
                overnightContinueAlert.classList.remove('d-none');
                return;
            }
        }

        // Scenario 2: Next date has slots starting at midnight (slot 1, 00:00 - 01:00)
        const nextStartsMidnight = nextSlots.some(function (s) { return s.startMinutes === 0; });
        if (nextStartsMidnight) {
            const sortedNext = nextSlots.slice().sort(function (a, b) { return a.startMinutes - b.startMinutes; });
            const nextRange = `12:00 AM – ${minutesTo12Hour(sortedNext[sortedNext.length - 1].endMinutes)}`;

            overnightContinueTitle.textContent = `Continuing into ${getShortFormattedDate(nextDate)} (${nextRange})`;
            overnightContinueDetail.textContent = `Your booking connects across midnight to ${getShortFormattedDate(nextDate)}.`;
            overnightContinueAlert.classList.remove('d-none');
            return;
        }

        overnightContinueAlert.classList.add('d-none');
    }

    function syncHiddenSlotInputs() {
        if (!hiddenSelectedSlotsContainer || !bookingDateInput) return;
        const currentDate = bookingDateInput.value;
        let html = '';

        const dates = getSelectedDatesSorted();
        dates.forEach(function (date) {
            if (date !== currentDate) {
                selectedSlotsByDate[date].forEach(function (slot) {
                    html += `<input type="hidden" name="SelectedSlotIds" value="${slot.id}" data-date="${date}" />`;
                });
            }
        });

        hiddenSelectedSlotsContainer.innerHTML = html;
    }

    function updateCardAppearance() {
        const currentDate = bookingDateInput ? bookingDateInput.value : '';
        const currentSelected = selectedSlotsByDate[currentDate] || [];
        const currentSelectedIds = new Set(currentSelected.map(function (s) { return s.id; }));

        getAllCheckboxes().forEach(function (checkbox) {
            const card = checkbox.closest('.timeslot-btn');
            if (!card) return;
            const slotId = parseInt(checkbox.value, 10);
            const isChecked = currentSelectedIds.has(slotId);

            checkbox.checked = isChecked;

            const statusSpan = card.querySelector('.timeslot-status');

            if (isChecked) {
                card.classList.add('selected');
                card.classList.remove('can-extend');
                if (statusSpan) {
                    statusSpan.innerHTML = '<i class="bi bi-check-circle-fill" aria-hidden="true"></i> <span class="status-text">Selected</span>';
                }
            } else {
                card.classList.remove('selected');
                if (statusSpan) {
                    const isPast = card.classList.contains('past');
                    const isBooked = card.classList.contains('booked');
                    const isMaint = card.classList.contains('maintenance');

                    if (isPast) {
                        statusSpan.innerHTML = '<i class="bi bi-clock-history" aria-hidden="true"></i> <span class="status-text">Passed</span>';
                    } else if (isBooked) {
                        statusSpan.innerHTML = '<i class="bi bi-lock-fill" aria-hidden="true"></i> <span class="status-text">Booked</span>';
                    } else if (isMaint) {
                        statusSpan.innerHTML = '<i class="bi bi-tools" aria-hidden="true"></i> <span class="status-text">Maintenance</span>';
                    } else {
                        statusSpan.innerHTML = '<i class="bi bi-circle" aria-hidden="true"></i> <span class="status-text">Available</span>';
                    }
                }
            }
        });

        // Compute and highlight next valid extension slots (.can-extend)
        updateNextValidSlotGuidance();
    }

    function updateNextValidSlotGuidance() {
        const currentDate = bookingDateInput ? bookingDateInput.value : '';
        if (!currentDate) return;

        // Clear existing can-extend
        document.querySelectorAll('.timeslot-btn.can-extend').forEach(function (el) {
            el.classList.remove('can-extend');
        });

        const totalSelected = getTotalSelectedCount();
        if (totalSelected === 0) return;

        const currentSelected = (selectedSlotsByDate[currentDate] || []).slice().sort(function (a, b) {
            return a.startMinutes - b.startMinutes;
        });

        const prevDate = addDays(currentDate, -1);
        const nextDate = addDays(currentDate, 1);
        const prevSelected = selectedSlotsByDate[prevDate] || [];
        const nextSelected = selectedSlotsByDate[nextDate] || [];

        const availableCards = Array.from(document.querySelectorAll('.timeslot-btn.available:not(.selected)'));

        if (currentSelected.length > 0) {
            const minStart = currentSelected[0].startMinutes;
            const maxEnd = currentSelected[currentSelected.length - 1].endMinutes;

            availableCards.forEach(function (card) {
                const start = parseInt(card.dataset.startMinutes || '0', 10);
                const end = parseInt(card.dataset.endMinutes || '0', 10);

                if (end === minStart || start === maxEnd) {
                    card.classList.add('can-extend');
                }
            });
        } else if (prevSelected.some(function (s) { return s.endMinutes === 1440; })) {
            // Previous day ends at midnight: the valid extension on current date is slot starting at 0 (12 AM - 1 AM)
            availableCards.forEach(function (card) {
                const start = parseInt(card.dataset.startMinutes || '0', 10);
                if (start === 0) {
                    card.classList.add('can-extend');
                }
            });
        } else if (nextSelected.some(function (s) { return s.startMinutes === 0; })) {
            // Next day starts at midnight: the valid extension on current date is slot ending at 1440 (11 PM - 12 AM)
            availableCards.forEach(function (card) {
                const end = parseInt(card.dataset.endMinutes || '0', 10);
                if (end === 1440) {
                    card.classList.add('can-extend');
                }
            });
        }
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

    function updateStepIndicator() {
        const step1 = document.getElementById('stepIndicator1');
        const step2 = document.getElementById('stepIndicator2');
        const step3 = document.getElementById('stepIndicator3');
        const step4 = document.getElementById('stepIndicator4');

        const hasCourtAndDate = courtSelect && courtSelect.value && bookingDateInput && bookingDateInput.value;
        const hasSlots = getTotalSelectedCount() > 0;
        const hasPrice = step4Card && step4Card.style.display !== 'none';

        if (step1) {
            step1.className = 'pb-step ' + (hasCourtAndDate ? 'is-completed' : 'is-active');
        }
        if (step2) {
            step2.className = 'pb-step ' + (hasPrice ? 'is-completed' : hasSlots ? 'is-active' : '');
        }
        if (step3) {
            step3.className = 'pb-step ' + (hasPrice ? 'is-active' : '');
        }
        if (step4) {
            step4.className = 'pb-step';
        }
    }

    function updateSummary() {
        const allSlots = getAllSelectedSlotsChronological();
        const totalHours = allSlots.length;

        if (totalHours === 0) {
            if (bookingSummaryContainer) bookingSummaryContainer.style.display = 'none';
            if (mobileStickySummary) mobileStickySummary.style.display = 'none';
            document.body.classList.remove('has-mobile-summary');
            updateStepIndicator();
            return;
        }

        const courtName = courtSelect && courtSelect.selectedIndex >= 0
            ? (courtSelect.options[courtSelect.selectedIndex]?.text || 'Court')
            : 'Court';

        const summaryCourt = document.getElementById('summaryCourtName');
        if (summaryCourt) summaryCourt.textContent = courtName;

        const dates = getSelectedDatesSorted();
        const summaryDate = document.getElementById('summaryDate');
        const summaryTime = document.getElementById('summaryTime');
        const summaryDuration = document.getElementById('summaryDuration');
        const overnightTransitionItem = document.getElementById('summaryOvernightTransitionItem');
        const overnightDetails = document.getElementById('summaryOvernightDetails');

        const mobileSummaryCourtDate = document.getElementById('mobileSummaryCourtDate');
        const mobileSummaryTimeDuration = document.getElementById('mobileSummaryTimeDuration');
        const mobileSummaryPrice = document.getElementById('mobileSummaryPrice');

        if (dates.length === 1) {
            // Single-day booking
            const dateStr = dates[0];
            const dateSlots = selectedSlotsByDate[dateStr].slice().sort(function (a, b) { return a.startMinutes - b.startMinutes; });
            const firstMin = dateSlots[0].startMinutes;
            const lastMin = dateSlots[dateSlots.length - 1].endMinutes;
            const timeRangeStr = minutesRangeTo12Hour(firstMin, lastMin);

            if (summaryDate) summaryDate.textContent = getFullFormattedDate(dateStr);
            if (summaryTime) summaryTime.textContent = timeRangeStr;
            if (summaryDuration) summaryDuration.textContent = `${totalHours} hour(s)`;
            if (overnightTransitionItem) overnightTransitionItem.style.display = 'none';

            if (mobileSummaryCourtDate) mobileSummaryCourtDate.textContent = `${courtName} • ${getShortFormattedDate(dateStr)}`;
            if (mobileSummaryTimeDuration) mobileSummaryTimeDuration.textContent = `${timeRangeStr} (${totalHours} hr${totalHours > 1 ? 's' : ''})`;
        } else {
            // Overnight booking crossing midnight
            const date1 = dates[0];
            const date2 = dates[1];
            const slots1 = selectedSlotsByDate[date1].slice().sort(function (a, b) { return a.startMinutes - b.startMinutes; });
            const slots2 = selectedSlotsByDate[date2].slice().sort(function (a, b) { return a.startMinutes - b.startMinutes; });

            const range1Str = `${minutesTo12Hour(slots1[0].startMinutes)} – 12:00 AM`;
            const range2Str = `12:00 AM – ${minutesTo12Hour(slots2[slots2.length - 1].endMinutes)}`;
            const overallRange = `${minutesTo12Hour(slots1[0].startMinutes)} → ${minutesTo12Hour(slots2[slots2.length - 1].endMinutes)}`;

            if (summaryDate) summaryDate.textContent = `${getFormattedDate(date1)} → ${getFormattedDate(date2)}`;
            if (summaryTime) summaryTime.textContent = overallRange;
            if (summaryDuration) summaryDuration.textContent = `${totalHours} hour(s)`;

            if (overnightTransitionItem && overnightDetails) {
                overnightTransitionItem.style.display = 'block';
                overnightDetails.innerHTML = `
                    <div class="d-flex justify-content-between align-items-center mb-1">
                        <span class="fw-semibold">${getShortFormattedDate(date1)}:</span>
                        <span>${range1Str} <span class="badge bg-secondary ms-1">${slots1.length} hr${slots1.length > 1 ? 's' : ''}</span></span>
                    </div>
                    <div class="overnight-arrow"><i class="bi bi-arrow-down-short"></i></div>
                    <div class="d-flex justify-content-between align-items-center mb-1">
                        <span class="fw-semibold">${getShortFormattedDate(date2)}:</span>
                        <span>${range2Str} <span class="badge bg-secondary ms-1">${slots2.length} hr${slots2.length > 1 ? 's' : ''}</span></span>
                    </div>
                    <div class="mt-2 pt-1 border-top fw-bold text-primary small d-flex justify-content-between">
                        <span>Total Duration:</span>
                        <span>${totalHours} hours</span>
                    </div>
                `;
            }

            if (mobileSummaryCourtDate) mobileSummaryCourtDate.textContent = `${courtName} • ${getShortFormattedDate(date1)} → ${getShortFormattedDate(date2)}`;
            if (mobileSummaryTimeDuration) mobileSummaryTimeDuration.textContent = `${overallRange} (${totalHours} hrs total)`;
        }

        if (bookingSummaryContainer) bookingSummaryContainer.style.display = 'block';

        // Mobile Sticky Summary bar sync
        if (mobileStickySummary) {
            mobileStickySummary.style.display = 'block';
            document.body.classList.add('has-mobile-summary');

            const summaryPrice = document.getElementById('summaryPrice');
            const hasCalculatedPrice = summaryPrice && summaryPrice.textContent.trim() !== '-' && summaryPrice.textContent.trim() !== '';

            if (mobileSummaryPrice) {
                if (hasCalculatedPrice) {
                    mobileSummaryPrice.textContent = summaryPrice.textContent.trim();
                    mobileSummaryPrice.style.display = 'inline';
                } else {
                    mobileSummaryPrice.style.display = 'none';
                }
            }

            if (btnMobileProceed) {
                if (step4Card && step4Card.style.display !== 'none') {
                    btnMobileProceed.innerHTML = 'Enter Info <i class="bi bi-arrow-down ms-1"></i>';
                } else {
                    btnMobileProceed.innerHTML = '<i class="bi bi-calculator me-1"></i> Calculate';
                }
            }
        }

        syncHiddenSlotInputs();
        updateOvernightContinueBanner();
        updateStepIndicator();
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

    // Handles slot checkbox changes
    if (timeslotGrid) {
        timeslotGrid.addEventListener('change', function (event) {
            const checkbox = event.target;
            if (!checkbox || !checkbox.classList.contains('timeslot-checkbox')) { return; }

            clearValidationError();
            hideGapAlert();

            const card = checkbox.closest('.timeslot-btn');
            const currentDate = bookingDateInput ? bookingDateInput.value : '';
            if (!currentDate) return;

            const slotId = parseInt(checkbox.value, 10);
            const startMin = parseInt(checkbox.dataset.startMinutes || '0', 10);
            const endMin = parseInt(checkbox.dataset.endMinutes || '0', 10);
            const timeStr = card ? card.querySelector('.timeslot-time')?.textContent.trim() : (slotDisplayTimes[slotId] || '');

            const currentSlots = selectedSlotsByDate[currentDate] || [];

            if (!checkbox.checked) {
                // UNCHECKING: Check if unchecking creates an interior gap in the current date selection
                const remaining = currentSlots.filter(function (s) { return s.id !== slotId; })
                    .sort(function (a, b) { return a.startMinutes - b.startMinutes; });

                let hasGap = false;
                for (let i = 1; i < remaining.length; i++) {
                    if (remaining[i].startMinutes !== remaining[i - 1].endMinutes) {
                        hasGap = true;
                        break;
                    }
                }

                if (hasGap) {
                    checkbox.checked = true;
                    if (card) {
                        card.classList.add('shake-error');
                        setTimeout(function () { card.classList.remove('shake-error'); }, 500);
                    }
                    showGapAlert(
                        'Cannot remove an intermediate time slot.',
                        'Please remove slots from the beginning or end of your booking to keep your time continuous.'
                    );
                    return;
                }

                selectedSlotsByDate[currentDate] = remaining;
                if (remaining.length === 0) {
                    delete selectedSlotsByDate[currentDate];
                }

                updateCardAppearance();
                updateSummary();

                resetPriceDisplay();
                return;
            }

            // CHECKING: Validate continuity
            const prevDate = addDays(currentDate, -1);
            const nextDate = addDays(currentDate, 1);
            const prevSlots = (selectedSlotsByDate[prevDate] || []).slice().sort(function (a, b) { return a.startMinutes - b.startMinutes; });
            const nextSlots = (selectedSlotsByDate[nextDate] || []).slice().sort(function (a, b) { return a.startMinutes - b.startMinutes; });

            const sortedCurrent = currentSlots.slice().sort(function (a, b) { return a.startMinutes - b.startMinutes; });

            if (sortedCurrent.length === 0) {
                // First slot on this date
                if (prevSlots.length > 0) {
                    // Continuing from previous date
                    const prevEndsAtMidnight = prevSlots[prevSlots.length - 1].endMinutes === 1440;
                    if (!prevEndsAtMidnight) {
                        checkbox.checked = false;
                        if (card) {
                            card.classList.add('shake-error');
                            setTimeout(function () { card.classList.remove('shake-error'); }, 500);
                        }
                        showGapAlert(
                            `Cannot start booking on ${getShortFormattedDate(currentDate)} with a gap.`,
                            `Your booking on ${getShortFormattedDate(prevDate)} ends at ${minutesTo12Hour(prevSlots[prevSlots.length - 1].endMinutes)}. To extend across midnight, select 11:00 PM – 12:00 AM on ${getShortFormattedDate(prevDate)} first.`
                        );
                        return;
                    }

                    if (startMin !== 0) {
                        checkbox.checked = false;
                        if (card) {
                            card.classList.add('shake-error');
                            setTimeout(function () { card.classList.remove('shake-error'); }, 500);
                        }
                        showGapAlert(
                            'Please select continuous time slots.',
                            `To continue your booking from ${getShortFormattedDate(prevDate)}, please select 12:00 AM – 1:00 AM on ${getShortFormattedDate(currentDate)} first.`
                        );
                        return;
                    }
                } else if (nextSlots.length > 0) {
                    // Connecting to next date
                    const nextStartsAtMidnight = nextSlots[0].startMinutes === 0;
                    if (!nextStartsAtMidnight || endMin !== 1440) {
                        checkbox.checked = false;
                        if (card) {
                            card.classList.add('shake-error');
                            setTimeout(function () { card.classList.remove('shake-error'); }, 500);
                        }
                        showGapAlert(
                            'Please select continuous time slots.',
                            `To connect with your booking on ${getShortFormattedDate(nextDate)}, select 11:00 PM – 12:00 AM first.`
                        );
                        return;
                    }
                }
            } else {
                // Already have slots selected on currentDate
                const currentMin = sortedCurrent[0].startMinutes;
                const currentMax = sortedCurrent[sortedCurrent.length - 1].endMinutes;

                if (startMin === currentMax) {
                    // Valid: extends later
                } else if (endMin === currentMin) {
                    // Valid: extends earlier
                } else {
                    // Gap detected!
                    checkbox.checked = false;
                    if (card) {
                        card.classList.add('shake-error');
                        setTimeout(function () { card.classList.remove('shake-error'); }, 500);
                    }

                    if (startMin > currentMax) {
                        const requiredNext = minutesRangeTo12Hour(currentMax, Math.min(1440, currentMax + 60));
                        showGapAlert(
                            `Your current selection ends at ${minutesTo12Hour(currentMax)}.`,
                            `Please select ${requiredNext} first to continue your booking.`
                        );
                    } else if (endMin < currentMin) {
                        const requiredPrev = minutesRangeTo12Hour(Math.max(0, currentMin - 60), currentMin);
                        showGapAlert(
                            `Your current selection starts at ${minutesTo12Hour(currentMin)}.`,
                            `Please select ${requiredPrev} first to extend your booking earlier.`
                        );
                    } else {
                        showGapAlert(
                            'Please select continuous time slots.',
                            'Time slots must be connected with no gaps.'
                        );
                    }
                    return;
                }
            }

            // Valid selection
            if (!selectedSlotsByDate[currentDate]) {
                selectedSlotsByDate[currentDate] = [];
            }
            selectedSlotsByDate[currentDate].push({
                id: slotId,
                displayTime: timeStr,
                startMinutes: startMin,
                endMinutes: endMin
            });

            updateCardAppearance();
            updateSummary();

            resetPriceDisplay();
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

            const currentSelected = selectedSlotsByDate[dateVal] || [];
            const currentSelectedIds = new Set(currentSelected.map(function (s) { return s.id; }));

            data.slots.forEach(function (slot) {
                slotDisplayTimes[slot.timeSlotId] = slot.displayTime;

                const slotStatusClass = slot.status || 'unavailable';
                const isDisabled = !slot.isAvailable;
                const isSelected = currentSelectedIds.has(slot.timeSlotId);
                const displayStatus = slot.isPast ? 'passed' : slot.status;
                const ariaLabel = `${slot.displayTime} - ${displayStatus}`;

                const statusIcon = isSelected ? 'bi-check-circle-fill'
                    : slot.isPast ? 'bi-clock-history'
                    : slot.status === 'available' ? 'bi-circle'
                    : slot.status === 'booked' ? 'bi-lock-fill'
                    : slot.status === 'maintenance' ? 'bi-tools'
                    : 'bi-slash-circle';

                html += `
                    <label class="timeslot-btn ${slotStatusClass} ${isSelected ? 'selected' : ''}"
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
                               ${isDisabled ? 'disabled' : ''}
                               ${isSelected ? 'checked' : ''} />

                        <span class="timeslot-time">
                            ${slot.displayTime}
                        </span>

                        <span class="timeslot-status">
                            <i class="bi ${statusIcon}" aria-hidden="true"></i>
                            <span class="status-text">${isSelected ? 'selected' : displayStatus}</span>
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

    // Court select: dynamic update WITHOUT refreshing the page
    if (courtSelect) {
        courtSelect.addEventListener('change', function () {
            const selectedCourt = this.value;
            const selectedDate = bookingDateInput ? bookingDateInput.value : '';

            // Switching court clears selection since court availability differs
            selectedSlotsByDate = {};
            resetPriceDisplay();

            const summaryCourt = document.getElementById('summaryCourtName');
            if (summaryCourt) {
                summaryCourt.textContent = this.options[this.selectedIndex]?.text || '-';
            }

            loadSlotsAjax(selectedCourt, selectedDate);
        });
    }

    // Booking date input: dynamic update WITHOUT refreshing the page, PRESERVING existing selections
    if (bookingDateInput) {
        bookingDateInput.addEventListener('change', function () {
            const selectedDate = this.value;
            const selectedCourt = courtSelect ? courtSelect.value : '';

            if (selectedCourt) {
                loadSlotsAjax(selectedCourt, selectedDate);
            }
        });
    }

    // Mobile Proceed Button
    if (btnMobileProceed) {
        btnMobileProceed.addEventListener('click', function () {
            if (step4Card && step4Card.style.display !== 'none') {
                scrollToStep4();
            } else {
                const calculateBtn = document.getElementById('btnCalculatePrice');
                if (calculateBtn) {
                    calculateBtn.click();
                }
            }
        });
    }

    // Form submission intercept: ensure primary booking date is Day 1 of the booking
    // and all selected slots across all dates are canonically submitted with zero duplicates.
    if (form) {
        form.addEventListener('submit', function (event) {
            const allSelected = getAllSelectedSlotsChronological();
            if (allSelected.length === 0) {
                event.preventDefault();
                showGapAlert('Please select at least one time slot.');
                showValidationError('Please select at least one time slot.');
                return false;
            }

            // Set form's bookingDate to the earliest selected date (Day 1)
            const dates = getSelectedDatesSorted();
            if (dates.length > 0 && bookingDateInput) {
                bookingDateInput.value = dates[0];
            }

            // Canonicalize slot IDs into hiddenSelectedSlotsContainer:
            // To prevent duplicate or missing slot submissions regardless of which date the user
            // was viewing when clicking submit, we emit ALL selected slots as hidden inputs
            // and remove the name attribute from the dynamic grid checkboxes.
            let hiddenHtml = '';
            allSelected.forEach(function (slot) {
                hiddenHtml += `<input type="hidden" name="SelectedSlotIds" value="${slot.id}" data-date="${slot.date}" />`;
            });
            if (hiddenSelectedSlotsContainer) {
                hiddenSelectedSlotsContainer.innerHTML = hiddenHtml;
            }

            // Remove name attribute from all grid checkboxes so the browser only posts the canonical hidden inputs
            getAllCheckboxes().forEach(function (cb) {
                cb.removeAttribute('name');
            });

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
