using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace PickleBallBooking.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingPeriodExclusionConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            migrationBuilder.AddColumn<NpgsqlRange<DateTime>>(
                name: "BookingPeriod",
                table: "Bookings",
                type: "tsrange",
                nullable: false,
                computedColumnSql: "tsrange((\"BookingDate\"::timestamp + \"StartTime\"), (\"BookingDate\"::timestamp + \"EndTime\"), '[)')",
                stored: true);

            migrationBuilder.Sql(@"
ALTER TABLE ""Bookings""
ADD CONSTRAINT ""EX_Bookings_NoOverlap""
EXCLUDE USING GIST (
    ""CourtId"" WITH =,
    ""BookingPeriod"" WITH &&
)
WHERE (""BookingStatus"" <> 2);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"Bookings\" DROP CONSTRAINT IF EXISTS \"EX_Bookings_NoOverlap\";");

            migrationBuilder.DropColumn(
                name: "BookingPeriod",
                table: "Bookings");

            migrationBuilder.Sql("DROP EXTENSION IF EXISTS btree_gist;");
        }
    }
}
