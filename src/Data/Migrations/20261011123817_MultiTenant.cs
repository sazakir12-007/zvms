using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VisitorManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class MultiTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Visits_AccessCode",
                table: "Visits");

            migrationBuilder.DropIndex(
                name: "IX_Visits_CheckedInAt",
                table: "Visits");

            migrationBuilder.DropIndex(
                name: "IX_Visits_Status",
                table: "Visits");

            migrationBuilder.DropIndex(
                name: "IX_StaffUsers_Email",
                table: "StaffUsers");

            migrationBuilder.DropIndex(
                name: "IX_LookupItems_Category_Name",
                table: "LookupItems");

            migrationBuilder.DropIndex(
                name: "IX_Employees_Email",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_BlacklistEntries_IsActive_Phone",
                table: "BlacklistEntries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AppSettings",
                table: "AppSettings");

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Visits",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "VisitPhotos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "VisitAttachments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "StaffUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "LookupItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Employees",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "BlacklistEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_AppSettings",
                table: "AppSettings",
                columns: new[] { "TenantId", "Key" });

            migrationBuilder.CreateTable(
                name: "PlatformAdmins",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    SecurityStamp = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    FailedSignIns = table.Column<int>(type: "INTEGER", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformAdmins", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Visits_TenantId",
                table: "Visits",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Visits_TenantId_AccessCode",
                table: "Visits",
                columns: new[] { "TenantId", "AccessCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Visits_TenantId_CheckedInAt",
                table: "Visits",
                columns: new[] { "TenantId", "CheckedInAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Visits_TenantId_Status",
                table: "Visits",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_VisitPhotos_TenantId",
                table: "VisitPhotos",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitAttachments_TenantId",
                table: "VisitAttachments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffUsers_TenantId",
                table: "StaffUsers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffUsers_TenantId_Email",
                table: "StaffUsers",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LookupItems_TenantId",
                table: "LookupItems",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LookupItems_TenantId_Category_Name",
                table: "LookupItems",
                columns: new[] { "TenantId", "Category", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_TenantId",
                table: "Employees",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_TenantId_Email",
                table: "Employees",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlacklistEntries_TenantId",
                table: "BlacklistEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BlacklistEntries_TenantId_IsActive_Phone",
                table: "BlacklistEntries",
                columns: new[] { "TenantId", "IsActive", "Phone" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_TenantId",
                table: "AppSettings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAdmins_Email",
                table: "PlatformAdmins",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants",
                column: "Slug",
                unique: true);

            // Data from before multi-tenancy becomes the "main" tenant (only if there is any data).
            migrationBuilder.Sql(@"
                INSERT INTO Tenants (Id, Slug, Name, IsActive, CreatedAt)
                SELECT 1, 'main',
                       COALESCE((SELECT Value FROM AppSettings WHERE Key = 'Branding.CompanyName' AND Value <> ''), 'Main'),
                       1, datetime('now', 'localtime')
                WHERE EXISTS (SELECT 1 FROM Employees) OR EXISTS (SELECT 1 FROM Visits) OR EXISTS (SELECT 1 FROM StaffUsers)
                   OR EXISTS (SELECT 1 FROM LookupItems) OR EXISTS (SELECT 1 FROM AppSettings);");

            foreach (var table in new[] { "AppSettings", "BlacklistEntries", "Employees", "LookupItems", "StaffUsers", "VisitAttachments", "VisitPhotos", "Visits" })
            {
                migrationBuilder.Sql($"UPDATE {table} SET TenantId = 1 WHERE TenantId = 0 AND EXISTS (SELECT 1 FROM Tenants WHERE Id = 1);");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformAdmins");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Visits_TenantId",
                table: "Visits");

            migrationBuilder.DropIndex(
                name: "IX_Visits_TenantId_AccessCode",
                table: "Visits");

            migrationBuilder.DropIndex(
                name: "IX_Visits_TenantId_CheckedInAt",
                table: "Visits");

            migrationBuilder.DropIndex(
                name: "IX_Visits_TenantId_Status",
                table: "Visits");

            migrationBuilder.DropIndex(
                name: "IX_VisitPhotos_TenantId",
                table: "VisitPhotos");

            migrationBuilder.DropIndex(
                name: "IX_VisitAttachments_TenantId",
                table: "VisitAttachments");

            migrationBuilder.DropIndex(
                name: "IX_StaffUsers_TenantId",
                table: "StaffUsers");

            migrationBuilder.DropIndex(
                name: "IX_StaffUsers_TenantId_Email",
                table: "StaffUsers");

            migrationBuilder.DropIndex(
                name: "IX_LookupItems_TenantId",
                table: "LookupItems");

            migrationBuilder.DropIndex(
                name: "IX_LookupItems_TenantId_Category_Name",
                table: "LookupItems");

            migrationBuilder.DropIndex(
                name: "IX_Employees_TenantId",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_Employees_TenantId_Email",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_BlacklistEntries_TenantId",
                table: "BlacklistEntries");

            migrationBuilder.DropIndex(
                name: "IX_BlacklistEntries_TenantId_IsActive_Phone",
                table: "BlacklistEntries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AppSettings",
                table: "AppSettings");

            migrationBuilder.DropIndex(
                name: "IX_AppSettings_TenantId",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "VisitPhotos");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "VisitAttachments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StaffUsers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "LookupItems");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "BlacklistEntries");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AppSettings");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AppSettings",
                table: "AppSettings",
                column: "Key");

            migrationBuilder.CreateIndex(
                name: "IX_Visits_AccessCode",
                table: "Visits",
                column: "AccessCode");

            migrationBuilder.CreateIndex(
                name: "IX_Visits_CheckedInAt",
                table: "Visits",
                column: "CheckedInAt");

            migrationBuilder.CreateIndex(
                name: "IX_Visits_Status",
                table: "Visits",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_StaffUsers_Email",
                table: "StaffUsers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LookupItems_Category_Name",
                table: "LookupItems",
                columns: new[] { "Category", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_Email",
                table: "Employees",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlacklistEntries_IsActive_Phone",
                table: "BlacklistEntries",
                columns: new[] { "IsActive", "Phone" });
        }
    }
}
