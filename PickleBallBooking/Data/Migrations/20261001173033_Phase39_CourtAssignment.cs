using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PickleBallBooking.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase39_CourtAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AreCourtAssignmentsLocked",
                table: "Activities",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CourtAssignmentsLockedAt",
                table: "Activities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CourtAssignmentsLockedByUserId",
                table: "Activities",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ActivityCourtAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    ActivityId = table.Column<int>(type: "integer", nullable: false),
                    CourtId = table.Column<int>(type: "integer", nullable: false),
                    ActivityRsvpId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    SlotNumber = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityCourtAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityCourtAssignments_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActivityCourtAssignments_ActivityRsvps_ActivityRsvpId",
                        column: x => x.ActivityRsvpId,
                        principalTable: "ActivityRsvps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActivityCourtAssignments_Courts_CourtId",
                        column: x => x.CourtId,
                        principalTable: "Courts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityCourtAssignments_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityCourtAssignment_Org_Activity_Court",
                table: "ActivityCourtAssignments",
                columns: new[] { "OrganizationId", "ActivityId", "CourtId" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityCourtAssignment_Org_Activity_Rsvp",
                table: "ActivityCourtAssignments",
                columns: new[] { "OrganizationId", "ActivityId", "ActivityRsvpId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityCourtAssignments_ActivityId",
                table: "ActivityCourtAssignments",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityCourtAssignments_ActivityRsvpId",
                table: "ActivityCourtAssignments",
                column: "ActivityRsvpId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityCourtAssignments_CourtId",
                table: "ActivityCourtAssignments",
                column: "CourtId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityCourtAssignments");

            migrationBuilder.DropColumn(
                name: "AreCourtAssignmentsLocked",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "CourtAssignmentsLockedAt",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "CourtAssignmentsLockedByUserId",
                table: "Activities");
        }
    }
}
