using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

// ─────────────────────────────────────────────────────────────────────────────
// Interface
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Phase 25: payment lifecycle management.
///
/// SECURITY RULES:
/// - Customers submit reference numbers via booking reference only — they cannot
///   set their own payment to Verified. That transition is admin-only.
/// - Admin verify/reject requires the payment to be in Submitted state.
/// - All admin operations are tenant-scoped (ITenantContext via DbContext filters).
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Creates a Pending payment for a booking.
    /// Called immediately after a booking is created.
    /// </summary>
    Task<Payment?> CreateForBookingAsync(int bookingId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the payment for a booking by its reference number.
    /// Used on the customer-facing payment page. Cross-tenant safe — only the
    /// booking reference is used, so customers cannot enumerate other tenants' payments.
    /// </summary>
    Task<PaymentLookup?> GetByBookingReferenceAsync(string bookingReference, CancellationToken ct = default);

    /// <summary>Retrieves a payment by id (tenant-scoped via query filter).</summary>
    Task<Payment?> GetByIdAsync(int paymentId, CancellationToken ct = default);

    /// <summary>
    /// Customer submits their GCash or other payment reference number (and optional proof path).
    /// Transitions: Pending → Submitted.
    /// Returns false if the payment cannot be submitted (wrong status, not found).
    /// </summary>
    Task<bool> SubmitAsync(int paymentId, string referenceNumber, string? proofImagePath, CancellationToken ct = default);

    /// <summary>
    /// Customer submits payment reference number, returning detailed result status.
    /// </summary>
    Task<PaymentSubmitResult> SubmitWithResultAsync(int paymentId, string referenceNumber, string? proofImagePath, CancellationToken ct = default);

    /// <summary>
    /// Admin verifies the payment.
    /// Transitions: Submitted → Verified.
    /// The adminUserId is the Identity UserId of the acting admin.
    /// </summary>
    Task<bool> VerifyAsync(int paymentId, string adminUserId, CancellationToken ct = default);

    /// <summary>
    /// Admin rejects the payment.
    /// Transitions: Submitted → Rejected.
    /// </summary>
    Task<bool> RejectAsync(int paymentId, string adminUserId, string? notes, CancellationToken ct = default);

    /// <summary>
    /// Cancels the payment for a booking (called when a booking is cancelled).
    /// Transitions: Pending or Submitted → Cancelled.
    /// </summary>
    Task CancelForBookingAsync(int bookingId, CancellationToken ct = default);

    /// <summary>Returns all payments for the current tenant, newest first.</summary>
    Task<List<PaymentSummary>> GetAllForTenantAsync(PaymentStatus? statusFilter = null, CancellationToken ct = default);

    /// <summary>
    /// Returns the active payment settings for the current tenant, or null.
    /// </summary>
    Task<OrganizationPaymentSettings?> GetPaymentSettingsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the active payment settings for a specific organization by id, or null.
    /// Useful on customer-facing pages where ambient tenant context may not be set.
    /// </summary>
    Task<OrganizationPaymentSettings?> GetPaymentSettingsForOrgAsync(int organizationId, CancellationToken ct = default);

    /// <summary>
    /// Saves (upsert) the payment settings for the current tenant.
    /// AccountName is required; AccountNumber is optional (e.g. when QR code is used).
    /// </summary>
    Task<bool> SavePaymentSettingsAsync(
        string accountName,
        string? accountNumber,
        string? instructions,
        string? qrCodeImagePath,
        bool isActive,
        CancellationToken ct = default);

    // ─── Tenant Payment Options (multi-method) ────────────────────────────

    /// <summary>Returns all active payment options for the current tenant, ordered by DisplayOrder.</summary>
    Task<List<TenantPaymentOption>> GetPaymentOptionsAsync(CancellationToken ct = default);

    /// <summary>Returns ALL payment options (active + inactive) for the current tenant — used by admin.</summary>
    Task<List<TenantPaymentOption>> GetAllPaymentOptionsAsync(CancellationToken ct = default);

    /// <summary>Returns a specific payment option by id (tenant-scoped).</summary>
    Task<TenantPaymentOption?> GetPaymentOptionByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Returns a payment option ignoring tenant query filters — for use on customer-facing pages.</summary>
    Task<TenantPaymentOption?> GetPaymentOptionByIdNoFilterAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Creates a new tenant payment option.
    /// Returns the created entity, or null if validation fails.
    /// </summary>
    Task<TenantPaymentOption?> CreatePaymentOptionAsync(
        string label,
        string accountName,
        string? accountNumber,
        string? instructions,
        string? qrCodeImagePath,
        int displayOrder,
        CancellationToken ct = default);

    /// <summary>Updates an existing payment option. Returns false if not found or not owned by current tenant.</summary>
    Task<bool> UpdatePaymentOptionAsync(
        int id,
        string label,
        string accountName,
        string? accountNumber,
        string? instructions,
        string? qrCodeImagePath,
        bool isActive,
        int displayOrder,
        CancellationToken ct = default);

    /// <summary>Deletes a payment option. Returns false if not found.</summary>
    Task<bool> DeletePaymentOptionAsync(int id, CancellationToken ct = default);
}

/// <summary>A lightweight summary row for the admin payments list.</summary>
public sealed record PaymentSummary(
    int Id,
    string BookingReference,
    string CustomerName,
    decimal Amount,
    PaymentStatus Status,
    PaymentMethod Method,
    string? ReferenceNumber,
    DateTime? SubmittedAt,
    DateTime CreatedAt);

/// <summary>Result of submitting a payment reference number.</summary>
public enum PaymentSubmitResult
{
    Success,
    NotFound,
    NotPending,
    EmptyReference,
    DuplicateReference,
    DatabaseError
}

/// <summary>Payment + booking details for the customer-facing payment page.</summary>
public sealed record PaymentLookup(
    int PaymentId,
    /// <summary>
    /// OrganizationId of the booking's tenant. Used server-side to construct the
    /// proof storage path. NEVER taken from client input.
    /// </summary>
    int OrganizationId,
    string BookingReference,
    string CustomerName,
    string CourtName,
    DateOnly BookingDate,
    TimeSpan StartTime,
    TimeSpan EndTime,
    decimal Amount,
    PaymentStatus Status,
    string? ReferenceNumber,
    DateTime? SubmittedAt = null,
    int? SelectedPaymentOptionId = null);

// ─────────────────────────────────────────────────────────────────────────────
// Implementation
// ─────────────────────────────────────────────────────────────────────────────

/// <inheritdoc />
public sealed class PaymentService : IPaymentService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly BookingEmailService? _emailService;
    private readonly BookingSmsService? _smsService;
    private readonly BookingTelegramService? _telegramService;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly ILogger<PaymentService>? _logger;

    public PaymentService(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        BookingEmailService? emailService = null,
        BookingSmsService? smsService = null,
        BookingTelegramService? telegramService = null,
        IServiceScopeFactory? scopeFactory = null,
        ILogger<PaymentService>? logger = null)
    {
        _context         = context;
        _tenantContext   = tenantContext;
        _emailService    = emailService;
        _smsService      = smsService;
        _telegramService = telegramService;
        _scopeFactory    = scopeFactory;
        _logger          = logger;
    }

    public async Task<Payment?> CreateForBookingAsync(int bookingId, CancellationToken ct = default)
    {
        // Load the booking (ignores query filters so customer/confirmation page can create payment).
        var booking = await _context.Bookings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null) return null;

        // Idempotent — don't create a second payment for the same booking.
        var existing = await _context.Payments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.BookingId == bookingId, ct);
        if (existing is not null) return existing;

        var now = DateTime.UtcNow;
        var payment = new Payment
        {
            OrganizationId = booking.OrganizationId,
            BookingId      = bookingId,
            Amount         = booking.Price,
            PaymentMethod  = PaymentMethod.GCash,
            PaymentStatus  = PaymentStatus.Pending,
            CreatedAt      = now,
            UpdatedAt      = now
        };

        _context.Payments.Add(payment);
        var prev = _context.SuppressTenantWriteGuard;
        _context.SuppressTenantWriteGuard = true;
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        finally
        {
            _context.SuppressTenantWriteGuard = prev;
        }
        return payment;
    }

    public async Task<PaymentLookup?> GetByBookingReferenceAsync(string bookingReference, CancellationToken ct = default)
    {
        // Intentionally ignores the tenant query filter so customers can look up
        // their payment without being on a specific subdomain. The booking reference
        // is a random token — there is no enumeration risk.
        var result = await _context.Payments
            .IgnoreQueryFilters()
            .Include(p => p.Booking)
                .ThenInclude(b => b!.Court)
            .FirstOrDefaultAsync(p => p.Booking!.BookingReference == bookingReference, ct);

        if (result?.Booking is null)
        {
            // If the payment record doesn't exist yet, check if the booking exists.
            var booking = await _context.Bookings
                .IgnoreQueryFilters()
                .Include(b => b.Court)
                .FirstOrDefaultAsync(b => b.BookingReference == bookingReference, ct);

            if (booking is null) return null;

            var createdPayment = await CreateForBookingAsync(booking.Id, ct);
            if (createdPayment is null) return null;

            return new PaymentLookup(
                createdPayment.Id,
                createdPayment.OrganizationId,
                booking.BookingReference,
                booking.CustomerName,
                booking.Court?.Name ?? "Court",
                booking.BookingDate,
                booking.StartTime,
                booking.EndTime,
                createdPayment.Amount,
                createdPayment.PaymentStatus,
                createdPayment.ReferenceNumber,
                createdPayment.SubmittedAt,
                booking.SelectedPaymentOptionId);
        }

        return new PaymentLookup(
            result.Id,
            result.OrganizationId,  // server-loaded — never from client
            result.Booking.BookingReference,
            result.Booking.CustomerName,
            result.Booking.Court?.Name ?? "Court",
            result.Booking.BookingDate,
            result.Booking.StartTime,
            result.Booking.EndTime,
            result.Amount,
            result.PaymentStatus,
            result.ReferenceNumber,
            result.SubmittedAt,
            result.Booking.SelectedPaymentOptionId);
    }

    public async Task<Payment?> GetByIdAsync(int paymentId, CancellationToken ct = default)
    {
        return await _context.Payments
            .Include(p => p.Booking)
                .ThenInclude(b => b!.Court)
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct);
    }

    public async Task<PaymentSubmitResult> SubmitWithResultAsync(int paymentId, string referenceNumber, string? proofImagePath, CancellationToken ct = default)
    {
        // Submission lookup intentionally ignores the query filter so customers can
        // submit without being on a specific subdomain (they arrived via email / QR link).
        var payment = await _context.Payments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct);

        if (payment is null) return PaymentSubmitResult.NotFound;

        // Only Pending payments can be submitted.
        if (payment.PaymentStatus != PaymentStatus.Pending) return PaymentSubmitResult.NotPending;

        var trimmed = (referenceNumber ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed)) return PaymentSubmitResult.EmptyReference;

        // Duplicate reference check: reject if the same reference number has
        // already been submitted for any other payment (ignores query filters so it
        // checks across all tenants — payment references are globally unique).
        var isDuplicate = await _context.Payments
            .IgnoreQueryFilters()
            .AnyAsync(p => p.ReferenceNumber == trimmed &&
                           p.Id != paymentId &&
                           p.PaymentStatus != PaymentStatus.Cancelled, ct);
        if (isDuplicate) return PaymentSubmitResult.DuplicateReference;

        var now = DateTime.UtcNow;
        payment.ReferenceNumber = trimmed;
        if (proofImagePath is not null)
        {
            payment.ProofImagePath = proofImagePath;
        }
        payment.PaymentStatus = PaymentStatus.Submitted;
        payment.SubmittedAt   = now;
        payment.UpdatedAt     = now;

        var prev = _context.SuppressTenantWriteGuard;
        _context.SuppressTenantWriteGuard = true;
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return PaymentSubmitResult.DatabaseError;
        }
        finally
        {
            _context.SuppressTenantWriteGuard = prev;
        }

        // Phase 26: notify customer and org after successful submission.
        try
        {
            var bookingForEmail = await _context.Bookings
                .IgnoreQueryFilters()
                .Include(b => b.Court)
                .FirstOrDefaultAsync(b => b.Id == payment.BookingId, ct);
            var orgForEmail = bookingForEmail is not null
                ? await _context.Organizations.FindAsync([payment.OrganizationId], ct)
                : null;
            if (bookingForEmail is not null && orgForEmail is not null)
            {
                _emailService?.SendPaymentSubmittedToCustomerAsync(bookingForEmail, payment, orgForEmail);
                _emailService?.SendPaymentSubmittedToOrgAsync(bookingForEmail, payment, orgForEmail);
                _telegramService?.SendPaymentSubmittedAlertAsync(bookingForEmail, payment, orgForEmail);
            }
        }
        catch (Exception ex)
        {
            // Email notification errors must never block payment submission.
            // The payment is already saved — just log and continue.
            _ = ex; // logged by IEmailService internally
        }

        // AI auto-verification (fire-and-forget — never blocks the customer's HTTP response).
        // IMPORTANT: We use IServiceScopeFactory to create a NEW DI scope for the background task.
        // The current _context is Scoped and will be disposed when this HTTP request ends —
        // we cannot use it directly from Task.Run().
        if (_scopeFactory is not null)
        {
            var bgPaymentId = payment.Id;
            var bgProofPath = payment.ProofImagePath;
            _ = Task.Run(async () =>
            {
                try
                {
                    await RunAiVerificationAsync(bgPaymentId, bgProofPath, CancellationToken.None);
                }
                catch (Exception bgEx)
                {
                    _logger?.LogError(bgEx,
                        "AI verification background task failed for payment {Id}", bgPaymentId);
                }
            });
        }

        return PaymentSubmitResult.Success;
    }

    public async Task<bool> SubmitAsync(int paymentId, string referenceNumber, string? proofImagePath, CancellationToken ct = default)
    {
        var result = await SubmitWithResultAsync(paymentId, referenceNumber, proofImagePath, ct);
        return result == PaymentSubmitResult.Success;
    }

    public async Task<bool> VerifyAsync(int paymentId, string adminUserId, CancellationToken ct = default)
    {
        // Admin-facing: tenant query filter is active.
        var payment = await _context.Payments
            .Include(p => p.Booking)
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct);

        if (payment is null || payment.PaymentStatus != PaymentStatus.Submitted)
            return false;

        var now = DateTime.UtcNow;
        payment.PaymentStatus    = PaymentStatus.Verified;
        payment.VerifiedAt       = now;
        payment.VerifiedByUserId = adminUserId;
        payment.UpdatedAt        = now;

        // Automatically confirm the booking when payment is verified.
        if (payment.Booking is not null &&
            payment.Booking.BookingStatus == BookingStatus.Pending)
        {
            payment.Booking.BookingStatus = BookingStatus.Confirmed;
            payment.Booking.UpdatedAt     = now;
        }

        await _context.SaveChangesAsync(ct);

        // Phase 26: notify customer that their booking is confirmed.
        try
        {
            var verifyBooking = payment.Booking;
            if (verifyBooking is not null)
            {
                verifyBooking.Court ??= await _context.Courts.FindAsync([verifyBooking.CourtId], ct);
                var verifyOrg = await _context.Organizations.FindAsync([payment.OrganizationId], ct);
                if (verifyOrg is not null)
                {
                    _emailService?.SendPaymentVerifiedAsync(verifyBooking, verifyOrg);
                    _smsService?.SendPaymentVerifiedAsync(verifyBooking, verifyOrg);
                }
            }
        }
        catch (Exception ex) { _ = ex; }

        return true;
    }

    // ─── AI Auto-Verification ─────────────────────────────────────────────────

    /// <summary>
    /// Background task: creates a FRESH DI scope (so DbContext, services are not disposed),
    /// loads the payment + booking + org, checks if AI verification is enabled,
    /// runs the cascade, then either auto-verifies or notifies the admin for manual review.
    /// </summary>
    private async Task RunAiVerificationAsync(
        int paymentId,
        string? proofImagePath,
        CancellationToken ct)
    {
        if (_scopeFactory is null) return;

        // Create a brand-new DI scope — the original scoped services (DbContext, etc.)
        // are already disposed when the HTTP request that triggered this ends.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db          = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var aiSvc       = scope.ServiceProvider.GetService<IAiPaymentVerificationService>();
        var proofStorage = scope.ServiceProvider.GetService<IPaymentProofStorage>();
        var emailSvc    = scope.ServiceProvider.GetService<BookingEmailService>();
        var smsSvc      = scope.ServiceProvider.GetService<BookingSmsService>();
        var telegramSvc = scope.ServiceProvider.GetService<BookingTelegramService>();

        if (aiSvc is null)
        {
            _logger?.LogWarning("AI verification service not registered. Skipping payment {Id}.", paymentId);
            return;
        }

        // Load payment + booking using fresh context (ignores tenant filter for cross-tenant safety)
        var payment = await db.Payments
            .IgnoreQueryFilters()
            .Include(p => p.Booking)
                .ThenInclude(b => b!.Court)
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct);

        if (payment is null || payment.Booking is null)
        {
            _logger?.LogWarning("AI verification: payment {Id} not found or has no booking.", paymentId);
            return;
        }

        var org = await db.Organizations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == payment.OrganizationId, ct);

        if (org is null) return;

        // Only run if this org has the AI toggle ON
        if (!org.EnableAiPaymentVerification)
        {
            _logger?.LogDebug("AI verification is disabled for org {OrgId}. Skipping payment {Id}.",
                org.Id, paymentId);
            return;
        }

        _logger?.LogInformation(
            "AI verification starting for payment {Id} (org={OrgId}).", paymentId, org.Id);

        // Get a time-limited signed URL for the proof image (if one was uploaded)
        string? proofSignedUrl = null;
        if (!string.IsNullOrEmpty(proofImagePath) && proofStorage is not null)
        {
            try
            {
                proofSignedUrl = await proofStorage.GetProofSignedUrlAsync(proofImagePath);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex,
                    "Could not get signed URL for proof image on payment {Id}. Continuing without image.",
                    paymentId);
            }
        }

        // Resolve expected recipient details (from the booking's selected payment option or org settings)
        string? expectedAccountName = null;
        string? expectedAccountNumber = null;

        if (payment.Booking.SelectedPaymentOptionId.HasValue)
        {
            var opt = await db.TenantPaymentOptions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(o => o.Id == payment.Booking.SelectedPaymentOptionId.Value, ct);
            if (opt is not null)
            {
                expectedAccountName = opt.AccountName;
                expectedAccountNumber = opt.AccountNumber;
            }
        }

        if (string.IsNullOrEmpty(expectedAccountName))
        {
            var orgSettings = await db.OrganizationPaymentSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.OrganizationId == payment.OrganizationId, ct);
            if (orgSettings is not null)
            {
                expectedAccountName = orgSettings.AccountName;
                expectedAccountNumber = orgSettings.AccountNumber;
            }
        }

        // Run the AI cascade
        var result = await aiSvc.EvaluateAsync(
            payment, payment.Booking, proofSignedUrl, expectedAccountName, expectedAccountNumber, ct);

        var aiActor = $"system:ai-agent:{result.ProviderUsed}";
        var aiNotes = $"AI verification by {result.ProviderUsed} | Confidence: {result.Confidence:P0} | Vision: {result.VisionUsed} | {result.Reason}";
        if (aiNotes.Length > 500)
        {
            aiNotes = aiNotes[..497] + "...";
        }

        _logger?.LogInformation(
            "Payment {Id}: AI decision={Decision}, confidence={Confidence:P0}, provider={Provider}",
            paymentId, result.Decision, result.Confidence, result.ProviderUsed);

        if (result.Decision == AiVerificationDecision.AutoVerify)
        {
            var now = DateTime.UtcNow;
            payment.PaymentStatus    = PaymentStatus.Verified;
            payment.VerifiedAt       = now;
            payment.VerifiedByUserId = aiActor;
            payment.Notes            = aiNotes;
            payment.UpdatedAt        = now;

            if (payment.Booking.BookingStatus == BookingStatus.Pending)
            {
                payment.Booking.BookingStatus = BookingStatus.Confirmed;
                payment.Booking.UpdatedAt     = now;
            }

            var prev = db.SuppressTenantWriteGuard;
            db.SuppressTenantWriteGuard = true;
            try   { await db.SaveChangesAsync(ct); }
            finally { db.SuppressTenantWriteGuard = prev; }

            _logger?.LogInformation("Payment {Id} auto-verified by AI. Booking confirmed.", paymentId);

            // Notify customer: booking confirmed
            try
            {
                payment.Booking.Court ??= await db.Courts.FindAsync([payment.Booking.CourtId], ct);
                emailSvc?.SendPaymentVerifiedAsync(payment.Booking, org);
                smsSvc?.SendPaymentVerifiedAsync(payment.Booking, org);
            }
            catch (Exception ex) { _logger?.LogWarning(ex, "AI-verify customer notification failed."); }
        }
        else
        {
            var now = DateTime.UtcNow;
            payment.Notes     = aiNotes;
            payment.UpdatedAt = now;

            var prev = db.SuppressTenantWriteGuard;
            db.SuppressTenantWriteGuard = true;
            try   { await db.SaveChangesAsync(ct); }
            finally { db.SuppressTenantWriteGuard = prev; }

            // ManualReview: notify admin via email + Telegram
            _logger?.LogInformation(
                "Payment {Id}: AI confidence {Confidence:P0} below threshold — escalating to manual review. Reason: {Reason}",
                paymentId, result.Confidence, result.Reason);

            try
            {
                emailSvc?.SendAiManualReviewRequiredAsync(payment.Booking, payment, org, result.Reason);
                telegramSvc?.SendAiManualReviewAlertAsync(payment.Booking, payment, org, result.Reason);
            }
            catch (Exception ex) { _logger?.LogWarning(ex, "AI manual review notification failed."); }
        }
    }

    public async Task<bool> RejectAsync(int paymentId, string adminUserId, string? notes, CancellationToken ct = default)
    {

        var payment = await _context.Payments
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct);

        if (payment is null || payment.PaymentStatus != PaymentStatus.Submitted)
            return false;

        var now = DateTime.UtcNow;
        payment.PaymentStatus    = PaymentStatus.Rejected;
        payment.VerifiedAt       = now;
        payment.VerifiedByUserId = adminUserId;
        payment.Notes            = notes;
        payment.UpdatedAt        = now;

        await _context.SaveChangesAsync(ct);

        // Phase 26: notify customer their payment was rejected.
        try
        {
            var rejectBooking = await _context.Bookings
                .IgnoreQueryFilters()
                .Include(b => b.Court)
                .FirstOrDefaultAsync(b => b.Id == payment.BookingId, ct);
            if (rejectBooking is not null)
            {
                var rejectOrg = await _context.Organizations.FindAsync([payment.OrganizationId], ct);
                if (rejectOrg is not null)
                {
                    _emailService?.SendPaymentRejectedAsync(rejectBooking, payment, rejectOrg);
                    _smsService?.SendPaymentRejectedAsync(rejectBooking, payment, rejectOrg);
                }
            }
        }
        catch (Exception ex) { _ = ex; }

        return true;
    }

    public async Task CancelForBookingAsync(int bookingId, CancellationToken ct = default)
    {

        var now = DateTime.UtcNow;
        var payments = await _context.Payments
            .Where(p => p.BookingId == bookingId &&
                        (p.PaymentStatus == PaymentStatus.Pending ||
                         p.PaymentStatus == PaymentStatus.Submitted))
            .ToListAsync(ct);

        foreach (var p in payments)
        {
            p.PaymentStatus = PaymentStatus.Cancelled;
            p.UpdatedAt     = now;
        }

        if (payments.Count > 0)
        {
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task<List<PaymentSummary>> GetAllForTenantAsync(PaymentStatus? statusFilter = null, CancellationToken ct = default)
    {
        var query = _context.Payments
            .AsNoTracking()
            .Include(p => p.Booking)
            .AsQueryable();

        if (statusFilter.HasValue)
        {
            query = query.Where(p => p.PaymentStatus == statusFilter.Value);
        }

        var payments = await query
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

        return payments
            .Where(p => p.Booking is not null)
            .Select(p => new PaymentSummary(
                p.Id,
                p.Booking!.BookingReference,
                p.Booking.CustomerName,
                p.Amount,
                p.PaymentStatus,
                p.PaymentMethod,
                p.ReferenceNumber,
                p.SubmittedAt,
                p.CreatedAt))
            .ToList();
    }

    public async Task<OrganizationPaymentSettings?> GetPaymentSettingsAsync(CancellationToken ct = default)
    {
        return await _context.OrganizationPaymentSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);
    }

    public async Task<OrganizationPaymentSettings?> GetPaymentSettingsForOrgAsync(int organizationId, CancellationToken ct = default)
    {
        return await _context.OrganizationPaymentSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, ct);
    }

    public async Task<bool> SavePaymentSettingsAsync(
        string accountName,
        string? accountNumber,
        string? instructions,
        string? qrCodeImagePath,
        bool isActive,
        CancellationToken ct = default)
    {
        var name   = (accountName   ?? string.Empty).Trim();
        var number = (accountNumber ?? string.Empty).Trim();

        var now      = DateTime.UtcNow;
        var existing = await _context.OrganizationPaymentSettings.FirstOrDefaultAsync(ct);

        // When GCash is enabled, require either an AccountName OR a QR code.
        // A QR code alone is sufficient — customers can scan and pay without knowing the account name.
        // effectiveQrPath considers both a newly-uploaded QR (qrCodeImagePath) and one
        // already stored in the database (existing.QRCodeImagePath).
        var effectiveQrPath = qrCodeImagePath ?? existing?.QRCodeImagePath;
        if (isActive && string.IsNullOrEmpty(name) && string.IsNullOrEmpty(effectiveQrPath))
            return false;

        if (existing is null)
        {
            var settings = new OrganizationPaymentSettings
            {
                AccountName     = name,
                AccountNumber   = number,
                Instructions    = instructions,
                QRCodeImagePath = qrCodeImagePath,
                IsActive        = isActive,
                CreatedAt       = now,
                UpdatedAt       = now
            };
            _context.OrganizationPaymentSettings.Add(settings);
        }
        else
        {
            existing.AccountName   = name;
            existing.AccountNumber = number;
            existing.Instructions  = instructions;
            existing.IsActive      = isActive;
            existing.UpdatedAt     = now;
            // Only overwrite QR path when a new image was uploaded.
            if (qrCodeImagePath is not null)
            {
                existing.QRCodeImagePath = qrCodeImagePath;
            }
        }

        await _context.SaveChangesAsync(ct);
        return true;
    }

    // ─── Tenant Payment Options ───────────────────────────────────────────────

    public async Task<List<TenantPaymentOption>> GetPaymentOptionsAsync(CancellationToken ct = default)
        => await _context.TenantPaymentOptions
            .Where(o => o.IsActive)
            .OrderBy(o => o.DisplayOrder).ThenBy(o => o.Id)
            .ToListAsync(ct);

    public async Task<List<TenantPaymentOption>> GetAllPaymentOptionsAsync(CancellationToken ct = default)
        => await _context.TenantPaymentOptions
            .OrderBy(o => o.DisplayOrder).ThenBy(o => o.Id)
            .ToListAsync(ct);

    public async Task<TenantPaymentOption?> GetPaymentOptionByIdAsync(int id, CancellationToken ct = default)
        => await _context.TenantPaymentOptions.FindAsync(new object[] { id }, ct);

    /// <summary>
    /// Loads a payment option regardless of which tenant is active.
    /// Used on the customer-facing payment page where tenant context is not set.
    /// </summary>
    public async Task<TenantPaymentOption?> GetPaymentOptionByIdNoFilterAsync(int id, CancellationToken ct = default)
        => await _context.TenantPaymentOptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<TenantPaymentOption?> CreatePaymentOptionAsync(
        string label,
        string accountName,
        string? accountNumber,
        string? instructions,
        string? qrCodeImagePath,
        int displayOrder,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(accountName))
            return null;

        var option = new TenantPaymentOption
        {
            Label           = label.Trim(),
            AccountName     = accountName.Trim(),
            AccountNumber   = accountNumber?.Trim(),
            Instructions    = instructions?.Trim(),
            QRCodeImagePath = qrCodeImagePath,
            DisplayOrder    = displayOrder,
            IsActive        = true,
            CreatedAt       = DateTime.UtcNow,
            UpdatedAt       = DateTime.UtcNow,
        };
        _context.TenantPaymentOptions.Add(option);
        await _context.SaveChangesAsync(ct);
        return option;
    }

    public async Task<bool> UpdatePaymentOptionAsync(
        int id,
        string label,
        string accountName,
        string? accountNumber,
        string? instructions,
        string? qrCodeImagePath,
        bool isActive,
        int displayOrder,
        CancellationToken ct = default)
    {
        var option = await _context.TenantPaymentOptions.FindAsync(new object[] { id }, ct);
        if (option is null) return false;

        option.Label         = label.Trim();
        option.AccountName   = accountName.Trim();
        option.AccountNumber = accountNumber?.Trim();
        option.Instructions  = instructions?.Trim();
        option.IsActive      = isActive;
        option.DisplayOrder  = displayOrder;
        option.UpdatedAt     = DateTime.UtcNow;
        // null = keep existing | "" = clear | any path = set new
        if (qrCodeImagePath is not null)
            option.QRCodeImagePath = string.IsNullOrEmpty(qrCodeImagePath) ? null : qrCodeImagePath;


        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeletePaymentOptionAsync(int id, CancellationToken ct = default)
    {
        var option = await _context.TenantPaymentOptions.FindAsync(new object[] { id }, ct);
        if (option is null) return false;
        _context.TenantPaymentOptions.Remove(option);
        await _context.SaveChangesAsync(ct);
        return true;
    }
}


