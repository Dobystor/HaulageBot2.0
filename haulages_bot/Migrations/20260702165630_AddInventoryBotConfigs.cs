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
            migrationBuilder.AddColumn<int>(
                name: "TimezoneOffsetHours",
                table: "ServerConfigs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmployeeFullName",
                table: "Haulages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RouteDescription",
                table: "Haulages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleEconomicNumber",
                table: "Haulages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InventoryBotConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerConfigId = table.Column<int>(type: "INTEGER", nullable: false),
                    TonnageMin = table.Column<int>(type: "INTEGER", nullable: false),
                    TonnageMax = table.Column<int>(type: "INTEGER", nullable: false),
                    SitesMin = table.Column<int>(type: "INTEGER", nullable: false),
                    SitesMax = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryBotConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RethinkBotConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerConfigId = table.Column<int>(type: "INTEGER", nullable: false),
                    RethinkHost = table.Column<string>(type: "TEXT", nullable: false),
                    RethinkPort = table.Column<int>(type: "INTEGER", nullable: false),
                    RethinkPassword = table.Column<string>(type: "TEXT", nullable: false),
                    IntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxSimultaneousVehicles = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RethinkBotConfigs", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryBotConfigs");

            migrationBuilder.DropTable(
                name: "RethinkBotConfigs");

            migrationBuilder.DropColumn(
                name: "TimezoneOffsetHours",
                table: "ServerConfigs");

            migrationBuilder.DropColumn(
                name: "EmployeeFullName",
                table: "Haulages");

            migrationBuilder.DropColumn(
                name: "RouteDescription",
                table: "Haulages");

            migrationBuilder.DropColumn(
                name: "VehicleEconomicNumber",
                table: "Haulages");
        }
    }
}
