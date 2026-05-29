using System;
using AgentX.Core.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentX.Core.Data.Migrations
{
    /// <summary>
    /// Drops the <c>licenses</c> table. Agent-X is now 100% free and open-source
    /// (MIT) with every capability unconditionally available — there are no license
    /// tiers, activation, or feature gates, so the backing table is no longer used.
    /// </summary>
    /// <remarks>
    /// Hand-authored migration. The EF Core design-time tooling cannot run in this
    /// environment (the .NET 10 SDK's design host fails to resolve the
    /// <c>net8.0-windows10.0.22621.0</c> Windows SDK runtime pack), so this migration
    /// and the model-snapshot edit were written by hand to mirror exactly what
    /// <c>dotnet ef migrations add DropLicensesTable</c> would have produced. It is
    /// committed but NOT applied — run <c>dotnet ef database update</c> only after review.
    ///
    /// Data-loss note: dropping <c>licenses</c> discards any locally-stored activation
    /// rows. This is intentional and non-destructive to user content — the table only
    /// ever held offline license-key metadata (key, tier, customer name/email,
    /// activation timestamps), none of which has meaning now that all features are free.
    /// No other table referenced <c>licenses</c> (no foreign keys), so the drop is safe
    /// and isolated. <see cref="Down"/> recreates the table shape (empty) for full
    /// reversibility, matching the original InitialBaseline definition.
    /// </remarks>
    [DbContext(typeof(AgentXDbContext))]
    [Migration("20260528120000_DropLicensesTable")]
    public partial class DropLicensesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "licenses");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recreate the table exactly as InitialBaseline defined it, so the
            // migration is fully reversible. The table is restored empty; prior
            // activation rows are not (and cannot be) recovered.
            migrationBuilder.CreateTable(
                name: "licenses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LicenseKey = table.Column<string>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<string>(type: "TEXT", nullable: true),
                    Tier = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "starter"),
                    IsActivated = table.Column<bool>(type: "INTEGER", nullable: false),
                    ActivatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastValidatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CustomerEmail = table.Column<string>(type: "TEXT", nullable: true),
                    CustomerName = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_licenses", x => x.Id);
                });
        }
    }
}
