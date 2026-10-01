using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PickleBallBooking.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase35_RecurringActivitySeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create the ActivitySeries table
            migrationBuilder.CreateTable(
                name: "ActivitySeries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Format = table.Column<int>(type: "integer", nullable: false),
                    SkillLevel = table.Column<int>(type: "integer", nullable: false),
                    RecurrenceType = table.Column<int>(type: "integer", nullable: false),
                    WeeklyDays = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    MonthlyDayOfMonth = table.Column<int>(type: "integer", nullable: true),
                    SpecificDates = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SeriesStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SeriesEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OccurrenceStartTime = table.Column<TimeSpan>(type: "time without time zone", nullable: false),
                    OccurrenceEndTime = table.Column<TimeSpan>(type: "time without time zone", nullable: false),
                    MaxCapacity = table.Column<int>(type: "integer", nullable: false),
                    PricePerPlayer = table.Column<decimal>(type: "numeric(8,2)", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivitySeries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivitySeries_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivitySeries_OrganizationId_Status",
                table: "ActivitySeries",
                columns: new[] { "OrganizationId", "Status" });

            // 2. Add nullable SeriesId FK to Activities
            migrationBuilder.AddColumn<int>(
                name: "SeriesId",
                table: "Activities",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Activities_SeriesId",
                table: "Activities",
                column: "SeriesId");

            migrationBuilder.AddForeignKey(
                name: "FK_Activities_ActivitySeries_SeriesId",
                table: "Activities",
                column: "SeriesId",
                principalTable: "ActivitySeries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Activities_ActivitySeries_SeriesId",
                table: "Activities");

            migrationBuilder.DropIndex(
                name: "IX_Activities_SeriesId",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "Activities");

            migrationBuilder.DropTable(
                name: "ActivitySeries");
        }
    }
}
