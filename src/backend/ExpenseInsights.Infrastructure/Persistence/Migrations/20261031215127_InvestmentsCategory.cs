using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseInsights.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvestmentsCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Id", "CreatedAt", "IsHidden", "Key", "Kind", "Name", "ParentId", "SortOrder", "UserId" },
                values: new object[] { new Guid("0000000c-0000-0000-0000-000000000021"), new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "investments", "Excluded", null, null, 210, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("0000000c-0000-0000-0000-000000000021"));
        }
    }
}
