using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VisitorManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class Licensing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LicensePackageId",
                table: "Tenants",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CreditTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Credits = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    VisitId = table.Column<int>(type: "INTEGER", nullable: true),
                    By = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LicensePackages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    BasePricePerMonth = table.Column<decimal>(type: "TEXT", nullable: false),
                    IncludedVisitors = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxVisitorsPerMonth = table.Column<int>(type: "INTEGER", nullable: false),
                    CostPerExtraVisitor = table.Column<decimal>(type: "TEXT", nullable: false),
                    MinCreditPurchase = table.Column<int>(type: "INTEGER", nullable: false),
                    FeatureList = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicensePackages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_LicensePackageId",
                table: "Tenants",
                column: "LicensePackageId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditTransactions_TenantId",
                table: "CreditTransactions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditTransactions_TenantId_CreatedAt",
                table: "CreditTransactions",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LicensePackages_Name",
                table: "LicensePackages",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Tenants_LicensePackages_LicensePackageId",
                table: "Tenants",
                column: "LicensePackageId",
                principalTable: "LicensePackages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tenants_LicensePackages_LicensePackageId",
                table: "Tenants");

            migrationBuilder.DropTable(
                name: "CreditTransactions");

            migrationBuilder.DropTable(
                name: "LicensePackages");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_LicensePackageId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LicensePackageId",
                table: "Tenants");
        }
    }
}
