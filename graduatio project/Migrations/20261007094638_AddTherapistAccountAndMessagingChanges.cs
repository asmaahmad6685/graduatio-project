using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace graduatio_project.Migrations
{
    /// <inheritdoc />
    public partial class AddTherapistAccountAndMessagingChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_Centers_CenterId",
                table: "Conversations");

            migrationBuilder.RenameColumn(
                name: "CenterId",
                table: "Conversations",
                newName: "TherapistId");

            migrationBuilder.RenameIndex(
                name: "IX_Conversations_PatientId_CenterId",
                table: "Conversations",
                newName: "IX_Conversations_PatientId_TherapistId");

            migrationBuilder.RenameIndex(
                name: "IX_Conversations_CenterId",
                table: "Conversations",
                newName: "IX_Conversations_TherapistId");

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Therapists",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Therapists_UserId",
                table: "Therapists",
                column: "UserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_Therapists_TherapistId",
                table: "Conversations",
                column: "TherapistId",
                principalTable: "Therapists",
                principalColumn: "TherapistId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Therapists_Users_UserId",
                table: "Therapists",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_Therapists_TherapistId",
                table: "Conversations");

            migrationBuilder.DropForeignKey(
                name: "FK_Therapists_Users_UserId",
                table: "Therapists");

            migrationBuilder.DropIndex(
                name: "IX_Therapists_UserId",
                table: "Therapists");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Therapists");

            migrationBuilder.RenameColumn(
                name: "TherapistId",
                table: "Conversations",
                newName: "CenterId");

            migrationBuilder.RenameIndex(
                name: "IX_Conversations_TherapistId",
                table: "Conversations",
                newName: "IX_Conversations_CenterId");

            migrationBuilder.RenameIndex(
                name: "IX_Conversations_PatientId_TherapistId",
                table: "Conversations",
                newName: "IX_Conversations_PatientId_CenterId");

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_Centers_CenterId",
                table: "Conversations",
                column: "CenterId",
                principalTable: "Centers",
                principalColumn: "CenterId",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
