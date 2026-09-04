using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace haulages_bot.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlServerMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    ServerConfigId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => new { x.CompanyId, x.ServerConfigId });
                });

            migrationBuilder.CreateTable(
                name: "DataConfigurationLocal",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ServerConfigId = table.Column<int>(type: "int", nullable: false),
                    TonnageVariation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Time = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SelectedRoutes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SelectedEmployees = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SelectedVehicles = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataConfigurationLocal", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InventoryBotConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ServerConfigId = table.Column<int>(type: "int", nullable: false),
                    TonnageMin = table.Column<int>(type: "int", nullable: false),
                    TonnageMax = table.Column<int>(type: "int", nullable: false),
                    SitesMin = table.Column<int>(type: "int", nullable: false),
                    SitesMax = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryBotConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Materials",
                columns: table => new
                {
                    materialTypeId = table.Column<int>(type: "int", nullable: false),
                    ServerConfigId = table.Column<int>(type: "int", nullable: false),
                    name = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Materials", x => new { x.materialTypeId, x.ServerConfigId });
                });

            migrationBuilder.CreateTable(
                name: "ProductionPlanBotConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ServerConfigId = table.Column<int>(type: "int", nullable: false),
                    TonnageMin = table.Column<int>(type: "int", nullable: false),
                    TonnageMax = table.Column<int>(type: "int", nullable: false),
                    LawMinGrTon = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LawMaxGrTon = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LawMinPercent = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LawMaxPercent = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionPlanBotConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RethinkBotConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ServerConfigId = table.Column<int>(type: "int", nullable: false),
                    RethinkHost = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RethinkPort = table.Column<int>(type: "int", nullable: false),
                    RethinkPassword = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IntervalSeconds = table.Column<int>(type: "int", nullable: false),
                    MaxSimultaneousVehicles = table.Column<int>(type: "int", nullable: false),
                    ScooptramCount = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RethinkBotConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Routes",
                columns: table => new
                {
                    haulagePathId = table.Column<int>(type: "int", nullable: false),
                    ServerConfigId = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    distance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    isExtraction = table.Column<bool>(type: "bit", nullable: false),
                    isEnabled = table.Column<bool>(type: "bit", nullable: false),
                    selectedMaterialType = table.Column<int>(type: "int", nullable: false),
                    materialTypeId = table.Column<int>(type: "int", nullable: true),
                    materialType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    loadPointId = table.Column<int>(type: "int", nullable: false),
                    loadPointName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    timeInHour = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    unLoadPointId = table.Column<int>(type: "int", nullable: false),
                    unLoadPointName = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Routes", x => new { x.haulagePathId, x.ServerConfigId });
                });

            migrationBuilder.CreateTable(
                name: "ServerConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ApiUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClientSecret = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Password = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AccessToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RefreshToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TokenExpiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsBotRunning = table.Column<bool>(type: "bit", nullable: false),
                    IsSyncEnabledLocal = table.Column<bool>(type: "bit", nullable: false),
                    TimezoneOffsetHours = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Shifts",
                columns: table => new
                {
                    WorkShiftId = table.Column<int>(type: "int", nullable: false),
                    ServerConfigId = table.Column<int>(type: "int", nullable: false),
                    ShiftId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Enabled = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    OperationTime = table.Column<TimeSpan>(type: "time", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shifts", x => new { x.WorkShiftId, x.ServerConfigId });
                });

            migrationBuilder.CreateTable(
                name: "TokenRegistries",
                columns: table => new
                {
                    TokenRegistryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    access_token = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    token_type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    refresh_token = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenRegistries", x => x.TokenRegistryId);
                });

            migrationBuilder.CreateTable(
                name: "VehicleType",
                columns: table => new
                {
                    VehicleTypeId = table.Column<int>(type: "int", nullable: false),
                    ServerConfigId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleType", x => new { x.VehicleTypeId, x.ServerConfigId });
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    ServerConfigId = table.Column<int>(type: "int", nullable: false),
                    NoEmployee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PaternalLastName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MaternalLastName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => new { x.EmployeeId, x.ServerConfigId });
                    table.ForeignKey(
                        name: "FK_Employees_Companies_CompanyId_ServerConfigId",
                        columns: x => new { x.CompanyId, x.ServerConfigId },
                        principalTable: "Companies",
                        principalColumns: new[] { "CompanyId", "ServerConfigId" });
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    ServerConfigId = table.Column<int>(type: "int", nullable: false),
                    Plates = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EconomicNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmptyWeight = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FuelTankCapacity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VehicleTypeId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LoadingCapacity = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => new { x.VehicleId, x.ServerConfigId });
                    table.ForeignKey(
                        name: "FK_Vehicles_Companies_CompanyId_ServerConfigId",
                        columns: x => new { x.CompanyId, x.ServerConfigId },
                        principalTable: "Companies",
                        principalColumns: new[] { "CompanyId", "ServerConfigId" });
                    table.ForeignKey(
                        name: "FK_Vehicles_VehicleType_VehicleTypeId_ServerConfigId",
                        columns: x => new { x.VehicleTypeId, x.ServerConfigId },
                        principalTable: "VehicleType",
                        principalColumns: new[] { "VehicleTypeId", "ServerConfigId" });
                });

            migrationBuilder.CreateTable(
                name: "Haulages",
                columns: table => new
                {
                    HaulageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VehicleId = table.Column<int>(type: "int", nullable: true),
                    EmployeeId = table.Column<int>(type: "int", nullable: true),
                    PathId = table.Column<int>(type: "int", nullable: true),
                    Weight = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Comments = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Dateofcarries = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    materialTypeId = table.Column<int>(type: "int", nullable: true),
                    LoadPointId = table.Column<int>(type: "int", nullable: true),
                    UnloadPointId = table.Column<int>(type: "int", nullable: true),
                    ShiftId = table.Column<int>(type: "int", nullable: true),
                    LoadPointName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UnloadPointName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LawType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Kilometers = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MaterialType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VehicleEconomicNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmployeeFullName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RouteDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ServerConfigId = table.Column<int>(type: "int", nullable: false),
                    VehicleNavigationVehicleId = table.Column<int>(type: "int", nullable: true),
                    VehicleNavigationServerConfigId = table.Column<int>(type: "int", nullable: true),
                    EmployeeId1 = table.Column<int>(type: "int", nullable: true),
                    EmployeeServerConfigId = table.Column<int>(type: "int", nullable: true),
                    haulagePathId = table.Column<int>(type: "int", nullable: true),
                    HaulagePathServerConfigId = table.Column<int>(type: "int", nullable: true),
                    MaterialTypeematerialTypeId = table.Column<int>(type: "int", nullable: true),
                    MaterialTypeeServerConfigId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Haulages", x => x.HaulageId);
                    table.ForeignKey(
                        name: "FK_Haulages_Employees_EmployeeId1_EmployeeServerConfigId",
                        columns: x => new { x.EmployeeId1, x.EmployeeServerConfigId },
                        principalTable: "Employees",
                        principalColumns: new[] { "EmployeeId", "ServerConfigId" });
                    table.ForeignKey(
                        name: "FK_Haulages_Materials_MaterialTypeematerialTypeId_MaterialTypeeServerConfigId",
                        columns: x => new { x.MaterialTypeematerialTypeId, x.MaterialTypeeServerConfigId },
                        principalTable: "Materials",
                        principalColumns: new[] { "materialTypeId", "ServerConfigId" });
                    table.ForeignKey(
                        name: "FK_Haulages_Routes_haulagePathId_HaulagePathServerConfigId",
                        columns: x => new { x.haulagePathId, x.HaulagePathServerConfigId },
                        principalTable: "Routes",
                        principalColumns: new[] { "haulagePathId", "ServerConfigId" });
                    table.ForeignKey(
                        name: "FK_Haulages_Vehicles_VehicleNavigationVehicleId_VehicleNavigationServerConfigId",
                        columns: x => new { x.VehicleNavigationVehicleId, x.VehicleNavigationServerConfigId },
                        principalTable: "Vehicles",
                        principalColumns: new[] { "VehicleId", "ServerConfigId" });
                });

            migrationBuilder.CreateTable(
                name: "ProgrammingRecords",
                columns: table => new
                {
                    ProgrammingRecordId = table.Column<int>(type: "int", nullable: false),
                    ServerConfigId = table.Column<int>(type: "int", nullable: false),
                    HaulageId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Dateofcarries = table.Column<TimeSpan>(type: "time", nullable: false),
                    EmployeeId1 = table.Column<int>(type: "int", nullable: false),
                    EmployeeServerConfigId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgrammingRecords", x => new { x.ProgrammingRecordId, x.ServerConfigId });
                    table.ForeignKey(
                        name: "FK_ProgrammingRecords_Employees_EmployeeId1_EmployeeServerConfigId",
                        columns: x => new { x.EmployeeId1, x.EmployeeServerConfigId },
                        principalTable: "Employees",
                        principalColumns: new[] { "EmployeeId", "ServerConfigId" });
                    table.ForeignKey(
                        name: "FK_ProgrammingRecords_Haulages_HaulageId",
                        column: x => x.HaulageId,
                        principalTable: "Haulages",
                        principalColumn: "HaulageId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_CompanyId_ServerConfigId",
                table: "Employees",
                columns: new[] { "CompanyId", "ServerConfigId" });

            migrationBuilder.CreateIndex(
                name: "IX_Haulages_EmployeeId1_EmployeeServerConfigId",
                table: "Haulages",
                columns: new[] { "EmployeeId1", "EmployeeServerConfigId" });

            migrationBuilder.CreateIndex(
                name: "IX_Haulages_haulagePathId_HaulagePathServerConfigId",
                table: "Haulages",
                columns: new[] { "haulagePathId", "HaulagePathServerConfigId" });

            migrationBuilder.CreateIndex(
                name: "IX_Haulages_MaterialTypeematerialTypeId_MaterialTypeeServerConfigId",
                table: "Haulages",
                columns: new[] { "MaterialTypeematerialTypeId", "MaterialTypeeServerConfigId" });

            migrationBuilder.CreateIndex(
                name: "IX_Haulages_VehicleNavigationVehicleId_VehicleNavigationServerConfigId",
                table: "Haulages",
                columns: new[] { "VehicleNavigationVehicleId", "VehicleNavigationServerConfigId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProgrammingRecords_EmployeeId1_EmployeeServerConfigId",
                table: "ProgrammingRecords",
                columns: new[] { "EmployeeId1", "EmployeeServerConfigId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProgrammingRecords_HaulageId",
                table: "ProgrammingRecords",
                column: "HaulageId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_CompanyId_ServerConfigId",
                table: "Vehicles",
                columns: new[] { "CompanyId", "ServerConfigId" });

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_VehicleTypeId_ServerConfigId",
                table: "Vehicles",
                columns: new[] { "VehicleTypeId", "ServerConfigId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataConfigurationLocal");

            migrationBuilder.DropTable(
                name: "InventoryBotConfigs");

            migrationBuilder.DropTable(
                name: "ProductionPlanBotConfigs");

            migrationBuilder.DropTable(
                name: "ProgrammingRecords");

            migrationBuilder.DropTable(
                name: "RethinkBotConfigs");

            migrationBuilder.DropTable(
                name: "ServerConfigs");

            migrationBuilder.DropTable(
                name: "Shifts");

            migrationBuilder.DropTable(
                name: "TokenRegistries");

            migrationBuilder.DropTable(
                name: "Haulages");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "Materials");

            migrationBuilder.DropTable(
                name: "Routes");

            migrationBuilder.DropTable(
                name: "Vehicles");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropTable(
                name: "VehicleType");
        }
    }
}
