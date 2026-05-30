using System;
using AgentX.Core.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentX.Core.Data.Migrations
{
    [DbContext(typeof(AgentXDbContext))]
    [Migration("20260430000000_AddTemporalIdentity")]
    public partial class AddTemporalIdentity : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TemporalBeliefs table — track beliefs/opinions over time
            migrationBuilder.CreateTable(
                name: "temporal_beliefs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Topic = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    FirstDetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastObservedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SentimentScore = table.Column<double>(type: "REAL", nullable: false),
                    ConfidenceLevel = table.Column<double>(type: "REAL", nullable: false),
                    CurrentStance = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    PreviousStance = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    HasEvolved = table.Column<bool>(type: "INTEGER", nullable: false),
                    StanceChangedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EvidenceJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_temporal_beliefs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_temporal_beliefs_Topic",
                table: "temporal_beliefs",
                column: "Topic",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_temporal_beliefs_LastObservedAt",
                table: "temporal_beliefs",
                column: "LastObservedAt");

            migrationBuilder.CreateIndex(
                name: "IX_temporal_beliefs_HasEvolved",
                table: "temporal_beliefs",
                column: "HasEvolved");

            // InsightMoments table — capture and resurface breakthrough insights
            migrationBuilder.CreateTable(
                name: "insight_moments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CapturedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Topic = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    InsightText = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    SignificanceScore = table.Column<double>(type: "REAL", nullable: false),
                    SourceType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SourceId = table.Column<long>(type: "INTEGER", nullable: true),
                    RelatedTopicsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ContextJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_insight_moments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_insight_moments_Topic",
                table: "insight_moments",
                column: "Topic");

            migrationBuilder.CreateIndex(
                name: "IX_insight_moments_SignificanceScore",
                table: "insight_moments",
                column: "SignificanceScore");

            migrationBuilder.CreateIndex(
                name: "IX_insight_moments_CapturedAt",
                table: "insight_moments",
                column: "CapturedAt");

            // EngagementMetrics table — track depth of interaction with content
            migrationBuilder.CreateTable(
                name: "engagement_metrics",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TargetType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    TargetId = table.Column<long>(type: "INTEGER", nullable: false),
                    FirstEngagedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastEngagedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TotalSecondsSpent = table.Column<int>(type: "INTEGER", nullable: false),
                    RevisitCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Depth = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    TopicsJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_engagement_metrics", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_engagement_metrics_TargetType_TargetId",
                table: "engagement_metrics",
                columns: new[] { "TargetType", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_engagement_metrics_LastEngagedAt",
                table: "engagement_metrics",
                column: "LastEngagedAt");

            // BeliefConflicts table — detect when past self disagrees with current self
            migrationBuilder.CreateTable(
                name: "belief_conflicts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BeliefId = table.Column<long>(type: "INTEGER", nullable: false),
                    Topic = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PreviousStance = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    CurrentStance = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    PreviousStancePeriod = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StanceChangedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ConflictMagnitude = table.Column<double>(type: "REAL", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    HasBeenAcknowledged = table.Column<bool>(type: "INTEGER", nullable: false),
                    AcknowledgedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ContextJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_belief_conflicts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_belief_conflicts_temporal_beliefs_BeliefId",
                        column: x => x.BeliefId,
                        principalTable: "temporal_beliefs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_belief_conflicts_BeliefId",
                table: "belief_conflicts",
                column: "BeliefId");

            migrationBuilder.CreateIndex(
                name: "IX_belief_conflicts_DetectedAt",
                table: "belief_conflicts",
                column: "DetectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_belief_conflicts_HasBeenAcknowledged",
                table: "belief_conflicts",
                column: "HasBeenAcknowledged");

            migrationBuilder.CreateIndex(
                name: "IX_belief_conflicts_ConflictMagnitude",
                table: "belief_conflicts",
                column: "ConflictMagnitude");

            // VoiceProfiles table — learn user's communication patterns
            migrationBuilder.CreateTable(
                name: "voice_profiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FirstSampleAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSampleAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SampleCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AvgSentenceLength = table.Column<double>(type: "REAL", nullable: false),
                    FormalityScore = table.Column<double>(type: "REAL", nullable: false),
                    CharacteristicPhrasesJson = table.Column<string>(type: "TEXT", nullable: false),
                    SentencePatternsJson = table.Column<string>(type: "TEXT", nullable: false),
                    BookendsJson = table.Column<string>(type: "TEXT", nullable: false),
                    StylisticTraitsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_voice_profiles", x => x.Id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "voice_profiles");

            migrationBuilder.DropTable(
                name: "belief_conflicts");

            migrationBuilder.DropTable(
                name: "engagement_metrics");

            migrationBuilder.DropTable(
                name: "insight_moments");

            migrationBuilder.DropTable(
                name: "temporal_beliefs");
        }
    }
}
