using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PickleBallBooking.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedTimeSlotsAndCourtDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Seed 24 hourly TimeSlots (6 AM to 10 PM)
            migrationBuilder.Sql(@"
                INSERT INTO ""TimeSlots"" (""StartTime"", ""EndTime"", ""Status"") 
                VALUES
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
                ('21:00:00'::time, '22:00:00'::time, 0)
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
                DELETE FROM ""CourtTimeSlots"" 
                WHERE ""TimeSlotId"" IN (
                    SELECT ""Id"" FROM ""TimeSlots"" 
                    WHERE ""StartTime"" >= '06:00:00'::time AND ""StartTime"" < '22:00:00'::time
                )
            ");

            migrationBuilder.Sql(@"
                DELETE FROM ""TimeSlots"" 
                WHERE ""StartTime"" >= '06:00:00'::time AND ""StartTime"" < '22:00:00'::time
            ");
        }
    }
}
