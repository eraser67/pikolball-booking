using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleBallBooking.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase38_PlayerCheckIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CheckInMethod",
                table: "Bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CheckedInAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckedInByUserId",
                table: "Bookings",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsNoShow",
                table: "Bookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CheckInMethod",
                table: "ActivityRsvps",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CheckedInAt",
                table: "ActivityRsvps",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckedInByUserId",
                table: "ActivityRsvps",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CheckInMethod",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CheckedInAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CheckedInByUserId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "IsNoShow",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CheckInMethod",
                table: "ActivityRsvps");

            migrationBuilder.DropColumn(
                name: "CheckedInAt",
                table: "ActivityRsvps");

            migrationBuilder.DropColumn(
                name: "CheckedInByUserId",
                table: "ActivityRsvps");
        }
    }
}
