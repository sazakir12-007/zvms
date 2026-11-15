using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VisitorManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionValidity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ValidUntil",
                table: "Tenants",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Months",
                table: "PaymentOrders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "PaymentOrders",
                type: "TEXT",
                nullable: false,
                defaultValue: "Credit");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ValidUntil",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Months",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "PaymentOrders");
        }
    }
}
