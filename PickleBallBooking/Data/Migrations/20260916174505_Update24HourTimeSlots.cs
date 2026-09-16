using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleBallBooking.Data.Migrations
{
    /// <inheritdoc />
    public partial class Update24HourTimeSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // First, clear any Bookings that have TimeSlotId references to old slots
            migrationBuilder.Sql(@"
                DELETE FROM ""Bookings"" 
                WHERE ""TimeSlotId"" IS NOT NULL
            ");

            // Delete any BookingTimeSlots that reference the old slots (6 AM onwards)
            migrationBuilder.Sql(@"
                DELETE FROM ""BookingTimeSlots"" 
                WHERE ""TimeSlotId"" IN (
                    SELECT ""Id"" FROM ""TimeSlots"" 
                    WHERE ""StartTime"" >= '06:00:00'::time
                )
            ");

            // Delete existing CourtTimeSlots
            migrationBuilder.Sql(@"
                DELETE FROM ""CourtTimeSlots"" 
                WHERE ""TimeSlotId"" IN (
                    SELECT ""Id"" FROM ""TimeSlots"" 
                    WHERE ""StartTime"" >= '06:00:00'::time
                )
            ");

            // Delete existing TimeSlots (6 AM onwards)
            migrationBuilder.Sql(@"
                DELETE FROM ""TimeSlots"" 
                WHERE ""StartTime"" >= '06:00:00'::time
            ");

            // Seed 24 hourly TimeSlots (12:00 AM to 12:00 AM next day)
            migrationBuilder.Sql(@"
                INSERT INTO ""TimeSlots"" (""StartTime"", ""EndTime"", ""Status"") 
                VALUES
                ('00:00:00'::time, '01:00:00'::time, 0),
                ('01:00:00'::time, '02:00:00'::time, 0),
                ('02:00:00'::time, '03:00:00'::time, 0),
                ('03:00:00'::time, '04:00:00'::time, 0),
                ('04:00:00'::time, '05:00:00'::time, 0),
                ('05:00:00'::time, '06:00:00'::time, 0),
                ('06:00:00'::time, '07:00:00'::time, 0),
                ('07:00:00'::time, '08:00:00'::time, 0),
                ('08:00:00'::time, '09:00:00'::time, 0),
                ('09:00:00'::time, '10:00:00'::time, 0),
                ('10:00:00'::time, '11:00:00'::time, 0),
                ('11:00:00'::time, '12:00:00'::time, 0),
                ('12:00:00'::time, '13:00:00'::time, 0),
                ('13:00:00'::time, '14:00:00'::time, 0),
                ('14:00:00'::time, '15:00:00'::time, 0),
                ('15:00:00'::time, '16:00:00'::time, 0),
                ('16:00:00'::time, '17:00:00'::time, 0),
                ('17:00:00'::time, '18:00:00'::time, 0),
                ('18:00:00'::time, '19:00:00'::time, 0),
                ('19:00:00'::time, '20:00:00'::time, 0),
                ('20:00:00'::time, '21:00:00'::time, 0),
                ('21:00:00'::time, '22:00:00'::time, 0),
                ('22:00:00'::time, '23:00:00'::time, 0),
                ('23:00:00'::time, '00:00:00'::time, 0)
                ON CONFLICT DO NOTHING
            ");

            // Seed default CourtTimeSlot records for all active courts
            migrationBuilder.Sql(@"
                INSERT INTO ""CourtTimeSlots"" (""CourtId"", ""TimeSlotId"", ""AvailabilityStatus"", ""CreatedAt"", ""UpdatedAt"")
                SELECT c.""Id"", ts.""Id"", 0, NOW(), NOW()
                FROM ""Courts"" c
                CROSS JOIN ""TimeSlots"" ts
                WHERE c.""Status"" = 0  -- Active courts only
                ON CONFLICT (""CourtId"", ""TimeSlotId"") DO NOTHING
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Delete seeded data
            migrationBuilder.Sql(@"
                DELETE FROM ""BookingTimeSlots"" 
                WHERE ""TimeSlotId"" IN (
                    SELECT ""Id"" FROM ""TimeSlots"" 
                    WHERE ""StartTime"" >= '00:00:00'::time
                )
            ");

            migrationBuilder.Sql(@"
                DELETE FROM ""Bookings"" 
                WHERE ""TimeSlotId"" IS NOT NULL
            ");

            migrationBuilder.Sql(@"
                DELETE FROM ""CourtTimeSlots"" 
                WHERE ""TimeSlotId"" IN (
                    SELECT ""Id"" FROM ""TimeSlots"" 
                    WHERE ""StartTime"" >= '00:00:00'::time
                )
            ");

            migrationBuilder.Sql(@"
                DELETE FROM ""TimeSlots"" 
                WHERE ""StartTime"" >= '00:00:00'::time
            ");
        }
    }
}
