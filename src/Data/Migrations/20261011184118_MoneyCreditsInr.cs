using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VisitorManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class MoneyCreditsInr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Credit becomes money, and the currency changes from USD to INR (converted at 1 USD = Rs 88).
            // 1. Ledger amounts, while the old per-check-in credits and USD prices are still there.
            migrationBuilder.Sql("""
                UPDATE CreditTransactions SET Amount = CAST(Credits AS REAL) * COALESCE((
                    SELECT CAST(p.CostPerExtraVisitor AS REAL) FROM Tenants t JOIN LicensePackages p ON p.Id = t.LicensePackageId
                    WHERE t.Id = CreditTransactions.TenantId), 0)
                WHERE Kind IN ('Grant', 'Usage');
                UPDATE CreditTransactions SET UnitPrice = -CAST(Amount AS REAL) WHERE Kind = 'Usage';
                UPDATE CreditTransactions SET UnitPrice = 0 WHERE Kind <> 'Usage';
                UPDATE CreditTransactions SET Amount = ROUND(CAST(Amount AS REAL) * 88, 2), UnitPrice = ROUND(CAST(UnitPrice AS REAL) * 88, 2);
                """);

            migrationBuilder.DropColumn(
                name: "Credits",
                table: "CreditTransactions");

            migrationBuilder.AlterColumn<decimal>(
                name: "MinCreditPurchase",
                table: "LicensePackages",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            // 2. Packages: minimum purchase was a number of credits, now an amount (rounded up to Rs 100); prices to INR.
            migrationBuilder.Sql("""
                UPDATE LicensePackages SET
                    MinCreditPurchase = MAX(100, CAST(((CAST(MinCreditPurchase AS REAL) * CAST(CostPerExtraVisitor AS REAL) * 88) + 99.999) / 100 AS INTEGER) * 100),
                    BasePricePerMonth = ROUND(CAST(BasePricePerMonth AS REAL) * 88),
                    CostPerExtraVisitor = ROUND(CAST(CostPerExtraVisitor AS REAL) * 88, 2);
                UPDATE LicensePackages SET FeatureList = REPLACE(FeatureList, 'IdCardScan', 'BusinessCardScan');
                UPDATE LicensePackages SET Description = REPLACE(Description, 'ID scanning', 'business card scanning');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "MinCreditPurchase",
                table: "LicensePackages",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.AddColumn<int>(
                name: "Credits",
                table: "CreditTransactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }
    }
}
