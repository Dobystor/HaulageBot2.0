using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace haulages_bot.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryBotConfigs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Usar SQL raw para ser idempotente en SQLite (las tablas/columnas pueden existir
            // por migraciones manuales con ExecuteSqlRaw en Program.cs)

            // Tabla InventoryBotConfigs
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS InventoryBotConfigs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ServerConfigId INTEGER NOT NULL,
                    TonnageMin INTEGER NOT NULL DEFAULT 200,
                    TonnageMax INTEGER NOT NULL DEFAULT 800,
                    SitesMin INTEGER NOT NULL DEFAULT 2,
                    SitesMax INTEGER NOT NULL DEFAULT 5,
                    IsEnabled INTEGER NOT NULL DEFAULT 0
                )");

            // Tabla RethinkBotConfigs (puede ya existir por ExecuteSqlRaw)
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS RethinkBotConfigs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ServerConfigId INTEGER NOT NULL,
                    RethinkHost TEXT NOT NULL DEFAULT '',
                    RethinkPort INTEGER NOT NULL DEFAULT 28015,
                    RethinkPassword TEXT NOT NULL DEFAULT '',
                    IntervalSeconds INTEGER NOT NULL DEFAULT 30,
                    MaxSimultaneousVehicles INTEGER NOT NULL DEFAULT 5,
                    IsEnabled INTEGER NOT NULL DEFAULT 0
                )");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryBotConfigs");
        }
    }
}
