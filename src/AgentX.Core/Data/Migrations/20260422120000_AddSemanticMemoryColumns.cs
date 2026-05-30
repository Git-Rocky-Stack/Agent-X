using AgentX.Core.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentX.Core.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AgentXDbContext))]
    [Migration("20260422120000_AddSemanticMemoryColumns")]
    public partial class AddSemanticMemoryColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add new columns for Semantic Memory 2.0
            migrationBuilder.AddColumn<string>(
                name: "Embedding",
                table: "memories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LinkedMemoryId",
                table: "memories",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DecayRate",
                table: "memories",
                type: "REAL",
                nullable: false,
                defaultValue: 0.01);

            migrationBuilder.AddColumn<double>(
                name: "Confidence",
                table: "memories",
                type: "REAL",
                nullable: false,
                defaultValue: 0.8);

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "memories",
                type: "TEXT",
                nullable: true);

            // Index for associative-link traversal (memory -> LinkedMemoryId).
            migrationBuilder.CreateIndex(
                name: "IX_memories_LinkedMemoryId",
                table: "memories",
                column: "LinkedMemoryId");

            // NOTE: The self-referencing relationship (LinkedMemoryId -> memories.Id) is
            // intentionally NOT created as a DB-level foreign key. SQLite cannot add a FK to
            // an existing table (no ALTER TABLE ... ADD CONSTRAINT), so a standalone
            // AddForeignKey throws NotSupportedException under the SQLite provider. The
            // relationship is preserved at the model level (AgentXDbContext: HasOne
            // LinkedMemory) for navigation/Include, and the link-traversal code in
            // SemanticMemoryService already tolerates dangling links — so no DB constraint
            // is required, and one would conflict with auto-linking (RESTRICT would block
            // deleting any memory another links to).
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop index (no DB-level FK was created — see Up()).
            migrationBuilder.DropIndex(
                name: "IX_memories_LinkedMemoryId",
                table: "memories");

            // Drop new columns
            migrationBuilder.DropColumn(
                name: "Tags",
                table: "memories");

            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "memories");

            migrationBuilder.DropColumn(
                name: "DecayRate",
                table: "memories");

            migrationBuilder.DropColumn(
                name: "LinkedMemoryId",
                table: "memories");

            migrationBuilder.DropColumn(
                name: "Embedding",
                table: "memories");
        }
    }
}
