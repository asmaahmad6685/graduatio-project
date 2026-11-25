using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace graduatio_project.Migrations
{
    /// <inheritdoc />
    public partial class AddCenterAddressArabic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddressArabic",
                table: "Centers",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddressArabic",
                table: "Centers");
        }
    }
}
