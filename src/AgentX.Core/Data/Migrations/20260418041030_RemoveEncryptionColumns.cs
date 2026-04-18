using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentX.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveEncryptionColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DpapiWrappedKey",
                table: "user_settings");

            migrationBuilder.DropColumn(
                name: "EncryptionEnabled",
                table: "user_settings");

            migrationBuilder.DropColumn(
                name: "EncryptionKeyStorageMode",
                table: "user_settings");

            migrationBuilder.DropColumn(
                name: "EncryptionSaltBase64",
                table: "user_settings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DpapiWrappedKey",
                table: "user_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EncryptionEnabled",
                table: "user_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EncryptionKeyStorageMode",
                table: "user_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptionSaltBase64",
                table: "user_settings",
                type: "TEXT",
                nullable: true);
        }
    }
}
