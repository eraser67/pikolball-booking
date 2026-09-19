using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleBallBooking.Data.Migrations
{
    /// <summary>
    /// Fixes the <c>BookingPeriod</c> computed column so the exclusion constraint
    /// (<c>EX_Bookings_NoOverlap</c>) works for the final hourly slot of the day
    /// (23:00-00:00).
    ///
    /// The previous expression built the range as
    /// <c>tsrange(BookingDate + StartTime, BookingDate + EndTime, '[)')</c>. For the
    /// 23:00-00:00 slot the stored <c>EndTime</c> is 00:00, so the upper bound landed
    /// on the same day's midnight - before the 23:00 lower bound. PostgreSQL rejects
    /// this with error 22000 ("range lower bound must be less than or equal to range
    /// upper bound"), which surfaced as a DbUpdateException whenever a booking ending
    /// at midnight was created.
    ///
    /// The corrected expression mirrors the application's overnight handling
    /// (see <c>AppClock.ToEndLocalDateTime</c>): the upper bound is built as a
    /// timestamp and, when <c>EndTime</c> does not advance past <c>StartTime</c>, one
    /// full day is added so the range rolls into the following day. Ranges within a
    /// single day are unchanged, so all existing behavior is preserved.
    /// </summary>
    [DbContext(typeof(global::PickleBallBooking.Data.ApplicationDbContext))]
    [Migration("20260918120000_FixBookingPeriodEndOfDayRange")]
    public partial class FixBookingPeriodEndOfDayRange : Migration
    {
        // Builds the period as a tsrange over timestamps. The upper bound is the
        // timestamp of BookingDate + EndTime; when EndTime <= StartTime (the
        // 23:00-00:00 slot, or genuine overnight ranges) a full day is added to that
        // timestamp so the upper bound is strictly after the lower bound.
        private const string AddConstraint =
            "ALTER TABLE \"Bookings\" ADD CONSTRAINT \"EX_Bookings_NoOverlap\" " +
            "EXCLUDE USING GIST (\"CourtId\" WITH =, \"BookingPeriod\" WITH &&) " +
            "WHERE (\"BookingStatus\" <> 2);";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the constraint before altering the column it depends on.
            migrationBuilder.Sql("ALTER TABLE \"Bookings\" DROP CONSTRAINT IF EXISTS \"EX_Bookings_NoOverlap\";");
            migrationBuilder.Sql("ALTER TABLE \"Bookings\" DROP COLUMN IF EXISTS \"BookingPeriod\";");

            migrationBuilder.Sql(BuildAddColumnStatement());
            migrationBuilder.Sql(AddConstraint);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"Bookings\" DROP CONSTRAINT IF EXISTS \"EX_Bookings_NoOverlap\";");
            migrationBuilder.Sql("ALTER TABLE \"Bookings\" DROP COLUMN IF EXISTS \"BookingPeriod\";");

            // Restore the original (pre-fix) definition.
            migrationBuilder.Sql(
                "ALTER TABLE \"Bookings\" " +
                "ADD COLUMN \"BookingPeriod\" tsrange GENERATED ALWAYS AS (" +
                "tsrange((\"BookingDate\"::timestamp + \"StartTime\"), (\"BookingDate\"::timestamp + \"EndTime\"), '[)')) STORED;");

            migrationBuilder.Sql(AddConstraint);
        }

        private static string BuildAddColumnStatement()
        {
            // NOTE: SQL identifiers are quoted with double quotes. Building the string
            // here keeps the embedded double quotes unambiguous in C# source.
            const string q = "\"";
            return
                "ALTER TABLE " + q + "Bookings" + q +
                " ADD COLUMN " + q + "BookingPeriod" + q + " tsrange GENERATED ALWAYS AS (" +
                "tsrange((" + q + "BookingDate" + q + "::timestamp + " + q + "StartTime" + q + "), " +
                "((" + q + "BookingDate" + q + "::timestamp + " + q + "EndTime" + q + ") + " +
                "CASE WHEN " + q + "EndTime" + q + " <= " + q + "StartTime" + q +
                " THEN interval '24 hours' ELSE interval '0 hours' END), '[)')) STORED;";
        }
    }
}

