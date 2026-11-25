using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace graduatio_project.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientSavedCenters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PatientSavedCenters",
                columns: table => new
                {
                    PatientId = table.Column<int>(type: "int", nullable: false),
                    CenterId = table.Column<int>(type: "int", nullable: false),
                    SavedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientSavedCenters", x => new { x.PatientId, x.CenterId });
                    table.ForeignKey(
                        name: "FK_PatientSavedCenters_Centers_CenterId",
                        column: x => x.CenterId,
                        principalTable: "Centers",
                        principalColumn: "CenterId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientSavedCenters_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "PatientId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PatientSavedCenters_CenterId",
                table: "PatientSavedCenters",
                column: "CenterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PatientSavedCenters");
        }
    }
}
