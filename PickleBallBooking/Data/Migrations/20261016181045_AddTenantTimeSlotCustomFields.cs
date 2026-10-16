using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleBallBooking.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantTimeSlotCustomFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(
                name: "CustomEndTime",
                table: "TenantTimeSlots",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "CustomStartTime",
                table: "TenantTimeSlots",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "TenantTimeSlots",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Ensure all global master slots are Active by default so tenants don't inherit old corrupted inactive states
            migrationBuilder.Sql("UPDATE \"TimeSlots\" SET \"Status\" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomEndTime",
                table: "TenantTimeSlots");

            migrationBuilder.DropColumn(
                name: "CustomStartTime",
                table: "TenantTimeSlots");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "TenantTimeSlots");
        }
    }
}
