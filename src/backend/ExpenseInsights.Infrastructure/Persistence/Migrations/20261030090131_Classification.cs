using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ExpenseInsights.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Classification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ClassificationConfidence",
                table: "Transactions",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "ClassificationReason",
                table: "Transactions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClassificationReasonValue",
                table: "Transactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClassificationRuleId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClassificationSource",
                table: "Transactions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Unclassified");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClassifiedAt",
                table: "Transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MerchantKey",
                table: "Transactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "OffsetsCategoryId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Transactions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsHidden = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Categories_Categories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Categories_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClassificationHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassificationHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassificationHistory_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClassificationHistory_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CategorySettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    IsHidden = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategorySettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CategorySettings_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CategorySettings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MerchantRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Pattern = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MerchantRules_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MerchantRules_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransactionSplits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionSplits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionSplits_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionSplits_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TransactionSplits_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Id", "CreatedAt", "IsHidden", "Key", "Kind", "Name", "ParentId", "SortOrder", "UserId" },
                values: new object[,]
                {
                    { new Guid("0000000c-0000-0000-0000-000000000001"), new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "groceries", "Spending", null, null, 10, null },
                    { new Guid("0000000c-0000-0000-0000-000000000002"), new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "restaurants", "Spending", null, null, 20, null },
                    { new Guid("0000000c-0000-0000-0000-000000000003"), new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "transport", "Spending", null, null, 30, null },
                    { new Guid("0000000c-0000-0000-0000-000000000004"), new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "health", "Spending", null, null, 40, null },
                    { new Guid("0000000c-0000-0000-0000-000000000005"), new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "beauty", "Spending", null, null, 50, null },
                    { new Guid("0000000c-0000-0000-0000-000000000006"), new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "home", "Spending", null, null, 60, null },
                    { new Guid("0000000c-0000-0000-0000-000000000007"), new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "clothing", "Spending", null, null, 70, null },
                    { new Guid("0000000c-0000-0000-0000-000000000008"), new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "electronics", "Spending", null, null, 80, null },
                    { new Guid("0000000c-0000-0000-0000-000000000009"), new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "leisure", "Spending", null, null, 90, null },
                    { new Guid("0000000c-0000-0000-0000-000000000010"), new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "subscriptions", "Spending", null, null, 100, null },
                    { new Guid("0000000c-0000-0000-0000-000000000011"), new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "bills", "Spending", null, null, 110, null },
                    { new Guid("0000000c-0000-0000-0000-000000000012"), new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "onlineShopping", "Spending", null, null, 120, null },
                    { new Guid("0000000c-0000-0000-0000-000000000013"), new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "transfersToPeople", "Spending", null, null, 130, null },
                    { new Guid("0000000c-0000-0000-0000-000000000014"), new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "other", "Spending", null, null, 140, null },
                    { new Guid("0000000c-0000-0000-0000-000000000015"), new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "salary", "Income", null, null, 150, null },
                    { new Guid("0000000c-0000-0000-0000-000000000016"), new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "transfersFromPeople", "Income", null, null, 160, null },
                    { new Guid("0000000c-0000-0000-0000-000000000017"), new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "otherIncome", "Income", null, null, 170, null },
                    { new Guid("0000000c-0000-0000-0000-000000000018"), new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "interest", "Income", null, null, 180, null },
                    { new Guid("0000000c-0000-0000-0000-000000000019"), new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "refunds", "Refund", null, null, 190, null },
                    { new Guid("0000000c-0000-0000-0000-000000000020"), new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "ownTransfer", "Excluded", null, null, 200, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CategoryId",
                table: "Transactions",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ClassificationRuleId",
                table: "Transactions",
                column: "ClassificationRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_OffsetsCategoryId",
                table: "Transactions",
                column: "OffsetsCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_UserId_CategoryId",
                table: "Transactions",
                columns: new[] { "UserId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_UserId_MerchantKey",
                table: "Transactions",
                columns: new[] { "UserId", "MerchantKey" });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Key",
                table: "Categories",
                column: "Key",
                unique: true,
                filter: "\"Key\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentId",
                table: "Categories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_UserId_ParentId",
                table: "Categories",
                columns: new[] { "UserId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_CategorySettings_CategoryId",
                table: "CategorySettings",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CategorySettings_UserId_CategoryId",
                table: "CategorySettings",
                columns: new[] { "UserId", "CategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationHistory_TransactionId_ChangedAt",
                table: "ClassificationHistory",
                columns: new[] { "TransactionId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationHistory_UserId",
                table: "ClassificationHistory",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantRules_CategoryId",
                table: "MerchantRules",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantRules_UserId_MatchType_Pattern",
                table: "MerchantRules",
                columns: new[] { "UserId", "MatchType", "Pattern" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransactionSplits_CategoryId",
                table: "TransactionSplits",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionSplits_TransactionId",
                table: "TransactionSplits",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionSplits_UserId",
                table: "TransactionSplits",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Categories_CategoryId",
                table: "Transactions",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Categories_OffsetsCategoryId",
                table: "Transactions",
                column: "OffsetsCategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_MerchantRules_ClassificationRuleId",
                table: "Transactions",
                column: "ClassificationRuleId",
                principalTable: "MerchantRules",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Categories_CategoryId",
                table: "Transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Categories_OffsetsCategoryId",
                table: "Transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_MerchantRules_ClassificationRuleId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "CategorySettings");

            migrationBuilder.DropTable(
                name: "ClassificationHistory");

            migrationBuilder.DropTable(
                name: "MerchantRules");

            migrationBuilder.DropTable(
                name: "TransactionSplits");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_CategoryId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_ClassificationRuleId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_OffsetsCategoryId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_UserId_CategoryId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_UserId_MerchantKey",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ClassificationConfidence",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ClassificationReason",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ClassificationReasonValue",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ClassificationRuleId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ClassificationSource",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ClassifiedAt",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "MerchantKey",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "OffsetsCategoryId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Transactions");
        }
    }
}
