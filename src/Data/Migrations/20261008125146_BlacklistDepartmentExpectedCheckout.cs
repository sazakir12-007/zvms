using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VisitorManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class BlacklistDepartmentExpectedCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Department",
                table: "Visits",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpectedCheckOutAt",
                table: "Visits",
                type: "TEXT",
                nullable: true);

            // Existing visits: take the department from their host.
            migrationBuilder.Sql(
                "UPDATE Visits SET Department = (SELECT e.Department FROM Employees e WHERE e.Id = Visits.EmployeeId) WHERE Department IS NULL;");

            migrationBuilder.CreateTable(
                name: "BlacklistEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    VisitId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    RemovedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlacklistEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlacklistEntries_Visits_VisitId",
                        column: x => x.VisitId,
                        principalTable: "Visits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlacklistEntries_IsActive_Phone",
                table: "BlacklistEntries",
                columns: new[] { "IsActive", "Phone" });

            migrationBuilder.CreateIndex(
                name: "IX_BlacklistEntries_VisitId",
                table: "BlacklistEntries",
                column: "VisitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlacklistEntries");

            migrationBuilder.DropColumn(
                name: "Department",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "ExpectedCheckOutAt",
                table: "Visits");
        }
    }
}
