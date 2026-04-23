using System;
using AgentX.Core.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentX.Core.Data.Migrations
{
    [DbContext(typeof(AgentXDbContext))]
    [Migration("20260423153000_AddConversationThemeClustering")]
    public partial class AddConversationThemeClustering : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Embedding",
                table: "conversation_summary_snapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmbeddedAt",
                table: "conversation_summary_snapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingModel",
                table: "conversation_summary_snapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "conversation_theme_clusters",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    PreviewText = table.Column<string>(type: "TEXT", nullable: false),
                    KeyPointsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    ConversationCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ActiveConversationCount7d = table.Column<int>(type: "INTEGER", nullable: false),
                    ActiveConversationCount30d = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastActiveAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MaterializedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_theme_clusters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "conversation_theme_memberships",
                columns: table => new
                {
                    ConversationId = table.Column<long>(type: "INTEGER", nullable: false),
                    SnapshotId = table.Column<long>(type: "INTEGER", nullable: false),
                    ClusterId = table.Column<long>(type: "INTEGER", nullable: false),
                    SimilarityScore = table.Column<float>(type: "REAL", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_theme_memberships", x => x.ConversationId);
                    table.ForeignKey(
                        name: "FK_conversation_theme_memberships_conversation_summary_snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "conversation_summary_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_conversation_theme_memberships_conversation_theme_clusters_ClusterId",
                        column: x => x.ClusterId,
                        principalTable: "conversation_theme_clusters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_conversation_theme_memberships_conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_conversation_summary_snapshots_EmbeddedAt",
                table: "conversation_summary_snapshots",
                column: "EmbeddedAt");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_theme_clusters_FirstSeenAt",
                table: "conversation_theme_clusters",
                column: "FirstSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_theme_clusters_LastActiveAt",
                table: "conversation_theme_clusters",
                column: "LastActiveAt");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_theme_clusters_MaterializedAt",
                table: "conversation_theme_clusters",
                column: "MaterializedAt");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_theme_memberships_AssignedAt",
                table: "conversation_theme_memberships",
                column: "AssignedAt");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_theme_memberships_ClusterId",
                table: "conversation_theme_memberships",
                column: "ClusterId");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_theme_memberships_SnapshotId",
                table: "conversation_theme_memberships",
                column: "SnapshotId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_theme_memberships");

            migrationBuilder.DropTable(
                name: "conversation_theme_clusters");

            migrationBuilder.DropIndex(
                name: "IX_conversation_summary_snapshots_EmbeddedAt",
                table: "conversation_summary_snapshots");

            migrationBuilder.DropColumn(
                name: "Embedding",
                table: "conversation_summary_snapshots");

            migrationBuilder.DropColumn(
                name: "EmbeddedAt",
                table: "conversation_summary_snapshots");

            migrationBuilder.DropColumn(
                name: "EmbeddingModel",
                table: "conversation_summary_snapshots");
        }
    }
}
