using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace haulages_bot.Migrations
{
    /// <inheritdoc />
    public partial class InitialSQLiteMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerConfigId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => new { x.CompanyId, x.ServerConfigId });
                });

            migrationBuilder.CreateTable(
                name: "DataConfigurationLocal",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerConfigId = table.Column<int>(type: "INTEGER", nullable: false),
                    TonnageVariation = table.Column<string>(type: "TEXT", nullable: false),
                    Time = table.Column<string>(type: "TEXT", nullable: false),
                    SelectedRoutes = table.Column<string>(type: "TEXT", nullable: false),
                    SelectedEmployees = table.Column<string>(type: "TEXT", nullable: false),
                    SelectedVehicles = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataConfigurationLocal", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Materials",
                columns: table => new
                {
                    materialTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerConfigId = table.Column<int>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Materials", x => new { x.materialTypeId, x.ServerConfigId });
                });

            migrationBuilder.CreateTable(
                name: "Routes",
                columns: table => new
                {
                    haulagePathId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerConfigId = table.Column<int>(type: "INTEGER", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    distance = table.Column<decimal>(type: "TEXT", nullable: false),
                    isExtraction = table.Column<bool>(type: "INTEGER", nullable: false),
                    isEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    loadPointId = table.Column<int>(type: "INTEGER", nullable: false),
                    loadPointName = table.Column<string>(type: "TEXT", nullable: false),
                    timeInHour = table.Column<decimal>(type: "TEXT", nullable: false),
                    unLoadPointId = table.Column<int>(type: "INTEGER", nullable: false),
                    unLoadPointName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Routes", x => new { x.haulagePathId, x.ServerConfigId });
                });

            migrationBuilder.CreateTable(
                name: "ServerConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ApiUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", nullable: false),
                    ClientSecret = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: true),
                    Password = table.Column<string>(type: "TEXT", nullable: true),
                    AccessToken = table.Column<string>(type: "TEXT", nullable: true),
                    RefreshToken = table.Column<string>(type: "TEXT", nullable: true),
                    TokenExpiry = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsBotRunning = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSyncEnabledLocal = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Shifts",
                columns: table => new
                {
                    WorkShiftId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerConfigId = table.Column<int>(type: "INTEGER", nullable: false),
                    ShiftId = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<string>(type: "TEXT", nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    OperationTime = table.Column<TimeSpan>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shifts", x => new { x.WorkShiftId, x.ServerConfigId });
                });

            migrationBuilder.CreateTable(
                name: "TokenRegistries",
                columns: table => new
                {
                    TokenRegistryId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    access_token = table.Column<string>(type: "TEXT", nullable: false),
                    token_type = table.Column<string>(type: "TEXT", nullable: false),
                    refresh_token = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenRegistries", x => x.TokenRegistryId);
                });

            migrationBuilder.CreateTable(
                name: "VehicleType",
                columns: table => new
                {
                    VehicleTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerConfigId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleType", x => new { x.VehicleTypeId, x.ServerConfigId });
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    EmployeeId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerConfigId = table.Column<int>(type: "INTEGER", nullable: false),
                    NoEmployee = table.Column<decimal>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    PaternalLastName = table.Column<string>(type: "TEXT", nullable: false),
                    MaternalLastName = table.Column<string>(type: "TEXT", nullable: false),
                    FullName = table.Column<string>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false)
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
                    VehicleId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerConfigId = table.Column<int>(type: "INTEGER", nullable: false),
                    Plates = table.Column<string>(type: "TEXT", nullable: false),
                    EconomicNumber = table.Column<string>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    EmptyWeight = table.Column<decimal>(type: "TEXT", nullable: false),
                    FuelTankCapacity = table.Column<decimal>(type: "TEXT", nullable: false),
                    Weight = table.Column<decimal>(type: "TEXT", nullable: false),
                    VehicleTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    LoadingCapacity = table.Column<decimal>(type: "TEXT", nullable: false)
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
                    HaulageId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VehicleId = table.Column<int>(type: "INTEGER", nullable: true),
                    EmployeeId = table.Column<int>(type: "INTEGER", nullable: true),
                    PathId = table.Column<int>(type: "INTEGER", nullable: true),
                    Weight = table.Column<decimal>(type: "TEXT", nullable: true),
                    Comments = table.Column<string>(type: "TEXT", nullable: true),
                    Dateofcarries = table.Column<string>(type: "TEXT", nullable: true),
                    materialTypeId = table.Column<int>(type: "INTEGER", nullable: true),
                    LoadPointId = table.Column<int>(type: "INTEGER", nullable: true),
                    UnloadPointId = table.Column<int>(type: "INTEGER", nullable: true),
                    ShiftId = table.Column<int>(type: "INTEGER", nullable: true),
                    LoadPointName = table.Column<string>(type: "TEXT", nullable: true),
                    UnloadPointName = table.Column<string>(type: "TEXT", nullable: true),
                    LawType = table.Column<string>(type: "TEXT", nullable: true),
                    Kilometers = table.Column<decimal>(type: "TEXT", nullable: true),
                    MaterialType = table.Column<string>(type: "TEXT", nullable: true),
                    ServerConfigId = table.Column<int>(type: "INTEGER", nullable: false),
                    VehicleNavigationVehicleId = table.Column<int>(type: "INTEGER", nullable: true),
                    VehicleNavigationServerConfigId = table.Column<int>(type: "INTEGER", nullable: true),
                    EmployeeId1 = table.Column<int>(type: "INTEGER", nullable: true),
                    EmployeeServerConfigId = table.Column<int>(type: "INTEGER", nullable: true),
                    haulagePathId = table.Column<int>(type: "INTEGER", nullable: true),
                    HaulagePathServerConfigId = table.Column<int>(type: "INTEGER", nullable: true),
                    MaterialTypeematerialTypeId = table.Column<int>(type: "INTEGER", nullable: true),
                    MaterialTypeeServerConfigId = table.Column<int>(type: "INTEGER", nullable: true)
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
                    ProgrammingRecordId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerConfigId = table.Column<int>(type: "INTEGER", nullable: false),
                    HaulageId = table.Column<int>(type: "INTEGER", nullable: false),
                    EmployeeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Dateofcarries = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    EmployeeId1 = table.Column<int>(type: "INTEGER", nullable: false),
                    EmployeeServerConfigId = table.Column<int>(type: "INTEGER", nullable: false)
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
                name: "ProgrammingRecords");

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
