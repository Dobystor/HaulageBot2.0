using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace haulages_bot.Migrations
{
    /// <inheritdoc />
    public partial class AddRouteMaterialTypeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "materialTypeId",
                table: "Routes",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "materialTypeId",
                table: "Routes");
        }
    }
}
