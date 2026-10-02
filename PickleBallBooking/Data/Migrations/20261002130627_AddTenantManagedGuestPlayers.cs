using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleBallBooking.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantManagedGuestPlayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdminNotes",
                table: "PlayerProfiles",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedByOrganizationId",
                table: "PlayerProfiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsGuest",
                table: "PlayerProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerProfile_CreatedByOrganizationId",
                table: "PlayerProfiles",
                column: "CreatedByOrganizationId");

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerProfiles_Organizations_CreatedByOrganizationId",
                table: "PlayerProfiles",
                column: "CreatedByOrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlayerProfiles_Organizations_CreatedByOrganizationId",
                table: "PlayerProfiles");

            migrationBuilder.DropIndex(
                name: "IX_PlayerProfile_CreatedByOrganizationId",
                table: "PlayerProfiles");

            migrationBuilder.DropColumn(
                name: "AdminNotes",
                table: "PlayerProfiles");

            migrationBuilder.DropColumn(
                name: "CreatedByOrganizationId",
                table: "PlayerProfiles");

            migrationBuilder.DropColumn(
                name: "IsGuest",
                table: "PlayerProfiles");
        }
    }
}
