using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleBallBooking.Data.Migrations
{
    /// <summary>
    /// Completes the migration to the fixed hourly-slot booking model by removing the
    /// legacy single <c>TimeSlotId</c> reference from <c>Bookings</c>. Slot membership is
    /// now tracked exclusively through the <c>BookingTimeSlots</c> join table.
    /// </summary>
    public partial class DropLegacyBookingTimeSlotColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill BookingTimeSlot rows for any legacy bookings that still reference
            // a single slot directly, so no booking data is silently orphaned before the
            // legacy column is dropped.
            migrationBuilder.Sql(@"
                INSERT INTO ""BookingTimeSlots"" (""BookingId"", ""CourtId"", ""BookingDate"", ""TimeSlotId"", ""SlotOrder"", ""IsActive"", ""CreatedAt"", ""UpdatedAt"")
                SELECT b.""Id"", b.""CourtId"", b.""BookingDate"", b.""TimeSlotId"", 0, TRUE, NOW(), NOW()
                FROM ""Bookings"" b
                WHERE b.""TimeSlotId"" IS NOT NULL
                  AND b.""BookingStatus"" <> 2
                  AND NOT EXISTS (
                      SELECT 1 FROM ""BookingTimeSlots"" bts
                      WHERE bts.""BookingId"" = b.""Id""
                        AND bts.""TimeSlotId"" = b.""TimeSlotId""
                  )
                ON CONFLICT DO NOTHING;
            ");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_TimeSlotId",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_CourtId_BookingDate_TimeSlotId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "TimeSlotId",
                table: "Bookings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TimeSlotId",
                table: "Bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_CourtId_BookingDate_TimeSlotId",
                table: "Bookings",
                columns: new[] { "CourtId", "BookingDate", "TimeSlotId" },
                unique: true,
                filter: "\"BookingStatus\" <> 2");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_TimeSlotId",
                table: "Bookings",
                column: "TimeSlotId");

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_TimeSlots_TimeSlotId",
                table: "Bookings",
                column: "TimeSlotId",
                principalTable: "TimeSlots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
