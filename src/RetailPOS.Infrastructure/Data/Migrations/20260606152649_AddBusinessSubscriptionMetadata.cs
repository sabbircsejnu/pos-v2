using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessSubscriptionMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_outlets",
                table: "businesses",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_users",
                table: "businesses",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "subscription_ends_at",
                table: "businesses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "subscription_plan",
                table: "businesses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "trial_ends_at",
                table: "businesses",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "max_outlets",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "max_users",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "subscription_ends_at",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "subscription_plan",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "trial_ends_at",
                table: "businesses");
        }
    }
}
