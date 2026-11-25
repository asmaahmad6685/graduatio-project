using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace graduatio_project.Migrations
{
    /// <inheritdoc />
    public partial class AddTherapistArabicNameAndBio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BioAr",
                table: "Therapists",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FullNameAr",
                table: "Therapists",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BioAr",
                table: "Therapists");

            migrationBuilder.DropColumn(
                name: "FullNameAr",
                table: "Therapists");
        }
    }
}
