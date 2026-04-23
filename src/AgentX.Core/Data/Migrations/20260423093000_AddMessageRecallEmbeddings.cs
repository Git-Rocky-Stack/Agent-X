using System;
using AgentX.Core.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentX.Core.Data.Migrations
{
    [DbContext(typeof(AgentXDbContext))]
    [Migration("20260423093000_AddMessageRecallEmbeddings")]
    public partial class AddMessageRecallEmbeddings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Embedding",
                table: "messages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmbeddedAt",
                table: "messages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingModel",
                table: "messages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_messages_EmbeddedAt",
                table: "messages",
                column: "EmbeddedAt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_messages_EmbeddedAt",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "Embedding",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "EmbeddedAt",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "EmbeddingModel",
                table: "messages");
        }
    }
}
