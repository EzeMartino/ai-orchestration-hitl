using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orchestration.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialMetricsExtractionDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinancialMetricsExtractionDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialMetricsExtractionDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialMetricsExtractionDrafts_AnalysisSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "AnalysisSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FinancialMetricsExtractionDrafts_AspNetUsers_ReviewedByUser~",
                        column: x => x.ReviewedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialMetricsExtractionDrafts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialMetricsExtractionDrafts_ReviewedByUserId",
                table: "FinancialMetricsExtractionDrafts",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialMetricsExtractionDrafts_SessionId",
                table: "FinancialMetricsExtractionDrafts",
                column: "SessionId",
                unique: true,
                filter: "\"Status\" = 'PendingReview'");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialMetricsExtractionDrafts_UserId_SessionId_Status",
                table: "FinancialMetricsExtractionDrafts",
                columns: new[] { "UserId", "SessionId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinancialMetricsExtractionDrafts");
        }
    }
}
