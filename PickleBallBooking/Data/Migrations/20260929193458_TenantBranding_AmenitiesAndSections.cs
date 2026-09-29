using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleBallBooking.Data.Migrations
{
    /// <inheritdoc />
    public partial class TenantBranding_AmenitiesAndSections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AmenitiesKeys",
                table: "Organizations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AnnouncementText",
                table: "Organizations",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OpeningHours",
                table: "Organizations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowAmenitiesSection",
                table: "Organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowAnnouncementBanner",
                table: "Organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowFaqSection",
                table: "Organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowHowItWorksSection",
                table: "Organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowLocationSection",
                table: "Organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowWhyUsSection",
                table: "Organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AmenitiesKeys",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "AnnouncementText",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "OpeningHours",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "ShowAmenitiesSection",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "ShowAnnouncementBanner",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "ShowFaqSection",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "ShowHowItWorksSection",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "ShowLocationSection",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "ShowWhyUsSection",
                table: "Organizations");
        }
    }
}
