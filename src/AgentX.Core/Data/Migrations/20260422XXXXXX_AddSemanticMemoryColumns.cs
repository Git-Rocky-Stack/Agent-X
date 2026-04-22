using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentX.Core.Data.Migrations
{
    /// <inheritdoc />
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

            // Create index for associative links
            migrationBuilder.CreateIndex(
                name: "IX_memories_LinkedMemoryId",
                table: "memories",
                column: "LinkedMemoryId");

            // Create foreign key for self-referencing relationship
            migrationBuilder.AddForeignKey(
                name: "FK_memories_memories_LinkedMemoryId",
                table: "memories",
                column: "LinkedMemoryId",
                principalTable: "memories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop foreign key and index
            migrationBuilder.DropForeignKey(
                name: "FK_memories_memories_LinkedMemoryId",
                table: "memories");

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
