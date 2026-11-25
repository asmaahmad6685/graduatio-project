using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace graduatio_project.Migrations
{
    /// <inheritdoc />
    public partial class AddAssessmentAnswerDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NumericValue",
                table: "AssessmentAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OtherDescription",
                table: "AssessmentAnswers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NumericValue",
                table: "AssessmentAnswers");

            migrationBuilder.DropColumn(
                name: "OtherDescription",
                table: "AssessmentAnswers");
        }
    }
}
