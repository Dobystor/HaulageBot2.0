using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace haulages_bot.Migrations
{
    /// <inheritdoc />
    public partial class AddRouteMaterialTypeName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "materialType",
                table: "Routes",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "materialType",
                table: "Routes");
        }
    }
}
