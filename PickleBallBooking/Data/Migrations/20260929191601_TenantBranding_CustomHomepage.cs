using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleBallBooking.Data.Migrations
{
    /// <inheritdoc />
    public partial class TenantBranding_CustomHomepage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AboutText",
                table: "Organizations",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FacebookUrl",
                table: "Organizations",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstagramUrl",
                table: "Organizations",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryColorHex",
                table: "Organizations",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowActivitiesOnHome",
                table: "Organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Tagline",
                table: "Organizations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwitterUrl",
                table: "Organizations",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AboutText",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "FacebookUrl",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "InstagramUrl",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "PrimaryColorHex",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "ShowActivitiesOnHome",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "Tagline",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "TwitterUrl",
                table: "Organizations");
        }
    }
}
