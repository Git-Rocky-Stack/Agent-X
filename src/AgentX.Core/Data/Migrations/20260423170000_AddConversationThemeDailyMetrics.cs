using System;
using AgentX.Core.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentX.Core.Data.Migrations
{
    [DbContext(typeof(AgentXDbContext))]
    [Migration("20260423170000_AddConversationThemeDailyMetrics")]
    public partial class AddConversationThemeDailyMetrics : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversation_theme_daily_metrics",
                columns: table => new
                {
                    ClusterId = table.Column<long>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ActiveConversationCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NewConversationCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SnapshotRefreshCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MaterializedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_theme_daily_metrics", x => new { x.ClusterId, x.Date });
                    table.ForeignKey(
                        name: "FK_conversation_theme_daily_metrics_conversation_theme_clusters_ClusterId",
                        column: x => x.ClusterId,
                        principalTable: "conversation_theme_clusters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_conversation_theme_daily_metrics_Date",
                table: "conversation_theme_daily_metrics",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_theme_daily_metrics_MaterializedAt",
                table: "conversation_theme_daily_metrics",
                column: "MaterializedAt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_theme_daily_metrics");
        }
    }
}
