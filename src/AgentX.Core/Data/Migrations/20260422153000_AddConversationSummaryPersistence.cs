using System;
using AgentX.Core.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentX.Core.Data.Migrations
{
    [DbContext(typeof(AgentXDbContext))]
    [Migration("20260422153000_AddConversationSummaryPersistence")]
    public partial class AddConversationSummaryPersistence : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversation_summary_snapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConversationId = table.Column<long>(type: "INTEGER", nullable: false),
                    SnapshotVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    SummaryText = table.Column<string>(type: "TEXT", nullable: false),
                    PreviewText = table.Column<string>(type: "TEXT", nullable: false),
                    KeyPointsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    CoveredMessageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceConversationUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsIncremental = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_summary_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_conversation_summary_snapshots_conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversation_summary_states",
                columns: table => new
                {
                    ConversationId = table.Column<long>(type: "INTEGER", nullable: false),
                    LatestSnapshotId = table.Column<long>(type: "INTEGER", nullable: true),
                    LatestSnapshotVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    LastCoveredMessageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PendingMessageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsStale = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastRefreshRequestedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastRefreshAttemptedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastRefreshedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    ConsecutiveFailureCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_summary_states", x => x.ConversationId);
                    table.ForeignKey(
                        name: "FK_conversation_summary_states_conversation_summary_snapshots_LatestSnapshotId",
                        column: x => x.LatestSnapshotId,
                        principalTable: "conversation_summary_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_conversation_summary_states_conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_conversation_summary_snapshots_ConversationId",
                table: "conversation_summary_snapshots",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_summary_snapshots_ConversationId_SnapshotVersion",
                table: "conversation_summary_snapshots",
                columns: new[] { "ConversationId", "SnapshotVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_conversation_summary_snapshots_GeneratedAt",
                table: "conversation_summary_snapshots",
                column: "GeneratedAt");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_summary_states_IsStale",
                table: "conversation_summary_states",
                column: "IsStale");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_summary_states_LastRefreshedAt",
                table: "conversation_summary_states",
                column: "LastRefreshedAt");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_summary_states_LatestSnapshotId",
                table: "conversation_summary_states",
                column: "LatestSnapshotId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_summary_states");

            migrationBuilder.DropTable(
                name: "conversation_summary_snapshots");
        }
    }
}
