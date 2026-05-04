using AgentX.Core.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentX.Core.Data.Migrations
{
    [DbContext(typeof(AgentXDbContext))]
    [Migration("20260503000000_AddEmbeddingModelVersioning")]
    public partial class AddEmbeddingModelVersioning : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DocumentChunkEntity additions
            migrationBuilder.AddColumn<string>(
                name: "EmbeddingModelVersion",
                table: "document_chunks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmbeddingDimensions",
                table: "document_chunks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmbeddedAt",
                table: "document_chunks",
                type: "TEXT",
                nullable: true);

            // MessageEntity additions (add EmbeddingDimensions)
            migrationBuilder.AddColumn<int>(
                name: "EmbeddingDimensions",
                table: "messages",
                type: "INTEGER",
                nullable: true);

            // MemoryEntity additions
            migrationBuilder.AddColumn<string>(
                name: "EmbeddingModelVersion",
                table: "memories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmbeddingDimensions",
                table: "memories",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmbeddedAt",
                table: "memories",
                type: "TEXT",
                nullable: true);

            // Create index on EmbeddingModelVersion for efficient querying by model version
            // This supports filtering by model version during re-embedding operations
            migrationBuilder.CreateIndex(
                name: "IX_document_chunks_EmbeddingModelVersion",
                table: "document_chunks",
                column: "EmbeddingModelVersion");

            migrationBuilder.CreateIndex(
                name: "IX_messages_EmbeddingModel",
                table: "messages",
                column: "EmbeddingModel");

            migrationBuilder.CreateIndex(
                name: "IX_memories_EmbeddingModelVersion",
                table: "memories",
                column: "EmbeddingModelVersion");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop indexes
            migrationBuilder.DropIndex(
                name: "IX_memories_EmbeddingModelVersion",
                table: "memories");

            migrationBuilder.DropIndex(
                name: "IX_messages_EmbeddingModel",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "IX_document_chunks_EmbeddingModelVersion",
                table: "document_chunks");

            // Drop MemoryEntity columns
            migrationBuilder.DropColumn(
                name: "EmbeddedAt",
                table: "memories");

            migrationBuilder.DropColumn(
                name: "EmbeddingDimensions",
                table: "memories");

            migrationBuilder.DropColumn(
                name: "EmbeddingModelVersion",
                table: "memories");

            // Drop MessageEntity column
            migrationBuilder.DropColumn(
                name: "EmbeddingDimensions",
                table: "messages");

            // Drop DocumentChunkEntity columns
            migrationBuilder.DropColumn(
                name: "EmbeddedAt",
                table: "document_chunks");

            migrationBuilder.DropColumn(
                name: "EmbeddingDimensions",
                table: "document_chunks");

            migrationBuilder.DropColumn(
                name: "EmbeddingModelVersion",
                table: "document_chunks");
        }
    }
}
