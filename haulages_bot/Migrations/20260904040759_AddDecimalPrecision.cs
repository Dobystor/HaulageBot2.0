using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace haulages_bot.Migrations
{
    /// <inheritdoc />
    public partial class AddDecimalPrecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Weight",
                table: "Vehicles",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "LoadingCapacity",
                table: "Vehicles",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "FuelTankCapacity",
                table: "Vehicles",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "EmptyWeight",
                table: "Vehicles",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "timeInHour",
                table: "Routes",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "distance",
                table: "Routes",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "LawMinPercent",
                table: "ProductionPlanBotConfigs",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "LawMinGrTon",
                table: "ProductionPlanBotConfigs",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "LawMaxPercent",
                table: "ProductionPlanBotConfigs",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "LawMaxGrTon",
                table: "ProductionPlanBotConfigs",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Weight",
                table: "Haulages",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Kilometers",
                table: "Haulages",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "NoEmployee",
                table: "Employees",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.CreateTable(
                name: "Historic",
                columns: table => new
                {
                    HistoricId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TokenRegistryId = table.Column<int>(type: "int", nullable: false),
                    vehicle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    loadPointName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    unLoadPointName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    materialTypeId = table.Column<int>(type: "int", nullable: false),
                    Dateofcarries = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WorkShiftId = table.Column<int>(type: "int", nullable: false),
                    VehicleNavigationVehicleId = table.Column<int>(type: "int", nullable: false),
                    VehicleNavigationServerConfigId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    EmployeeServerConfigId = table.Column<int>(type: "int", nullable: false),
                    haulagePathId = table.Column<int>(type: "int", nullable: false),
                    HaulagePathServerConfigId = table.Column<int>(type: "int", nullable: false),
                    materialTypeId1 = table.Column<int>(type: "int", nullable: false),
                    MaterialTypeServerConfigId = table.Column<int>(type: "int", nullable: false),
                    WorkShiftId1 = table.Column<int>(type: "int", nullable: false),
                    WorkShiftServerConfigId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Historic", x => x.HistoricId);
                    table.ForeignKey(
                        name: "FK_Historic_Employees_EmployeeId_EmployeeServerConfigId",
                        columns: x => new { x.EmployeeId, x.EmployeeServerConfigId },
                        principalTable: "Employees",
                        principalColumns: new[] { "EmployeeId", "ServerConfigId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Historic_Materials_materialTypeId1_MaterialTypeServerConfigId",
                        columns: x => new { x.materialTypeId1, x.MaterialTypeServerConfigId },
                        principalTable: "Materials",
                        principalColumns: new[] { "materialTypeId", "ServerConfigId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Historic_Routes_haulagePathId_HaulagePathServerConfigId",
                        columns: x => new { x.haulagePathId, x.HaulagePathServerConfigId },
                        principalTable: "Routes",
                        principalColumns: new[] { "haulagePathId", "ServerConfigId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Historic_Shifts_WorkShiftId1_WorkShiftServerConfigId",
                        columns: x => new { x.WorkShiftId1, x.WorkShiftServerConfigId },
                        principalTable: "Shifts",
                        principalColumns: new[] { "WorkShiftId", "ServerConfigId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Historic_TokenRegistries_TokenRegistryId",
                        column: x => x.TokenRegistryId,
                        principalTable: "TokenRegistries",
                        principalColumn: "TokenRegistryId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Historic_Vehicles_VehicleNavigationVehicleId_VehicleNavigationServerConfigId",
                        columns: x => new { x.VehicleNavigationVehicleId, x.VehicleNavigationServerConfigId },
                        principalTable: "Vehicles",
                        principalColumns: new[] { "VehicleId", "ServerConfigId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Historic_EmployeeId_EmployeeServerConfigId",
                table: "Historic",
                columns: new[] { "EmployeeId", "EmployeeServerConfigId" });

            migrationBuilder.CreateIndex(
                name: "IX_Historic_haulagePathId_HaulagePathServerConfigId",
                table: "Historic",
                columns: new[] { "haulagePathId", "HaulagePathServerConfigId" });

            migrationBuilder.CreateIndex(
                name: "IX_Historic_materialTypeId1_MaterialTypeServerConfigId",
                table: "Historic",
                columns: new[] { "materialTypeId1", "MaterialTypeServerConfigId" });

            migrationBuilder.CreateIndex(
                name: "IX_Historic_TokenRegistryId",
                table: "Historic",
                column: "TokenRegistryId");

            migrationBuilder.CreateIndex(
                name: "IX_Historic_VehicleNavigationVehicleId_VehicleNavigationServerConfigId",
                table: "Historic",
                columns: new[] { "VehicleNavigationVehicleId", "VehicleNavigationServerConfigId" });

            migrationBuilder.CreateIndex(
                name: "IX_Historic_WorkShiftId1_WorkShiftServerConfigId",
                table: "Historic",
                columns: new[] { "WorkShiftId1", "WorkShiftServerConfigId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Historic");

            migrationBuilder.AlterColumn<decimal>(
                name: "Weight",
                table: "Vehicles",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "LoadingCapacity",
                table: "Vehicles",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "FuelTankCapacity",
                table: "Vehicles",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "EmptyWeight",
                table: "Vehicles",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "timeInHour",
                table: "Routes",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "distance",
                table: "Routes",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "LawMinPercent",
                table: "ProductionPlanBotConfigs",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "LawMinGrTon",
                table: "ProductionPlanBotConfigs",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "LawMaxPercent",
                table: "ProductionPlanBotConfigs",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "LawMaxGrTon",
                table: "ProductionPlanBotConfigs",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "Weight",
                table: "Haulages",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Kilometers",
                table: "Haulages",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "NoEmployee",
                table: "Employees",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,0)",
                oldPrecision: 18,
                oldScale: 0);
        }
    }
}
