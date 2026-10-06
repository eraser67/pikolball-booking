using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleBallBooking.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingFacebookNameAndPaymentMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaintenanceNote",
                table: "BookingTimeSlots");

            migrationBuilder.AddColumn<string>(
                name: "MaintenanceNote",
                table: "CourtTimeSlots",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FacebookName",
                table: "Bookings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SelectedPaymentMethod",
                table: "Bookings",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaintenanceNote",
                table: "CourtTimeSlots");

            migrationBuilder.DropColumn(
                name: "FacebookName",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "SelectedPaymentMethod",
                table: "Bookings");

            migrationBuilder.AddColumn<string>(
                name: "MaintenanceNote",
                table: "BookingTimeSlots",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }
    }
}
