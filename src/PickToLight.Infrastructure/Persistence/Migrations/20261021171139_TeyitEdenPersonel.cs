using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickToLight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TeyitEdenPersonel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "teyit_eden_personel_id",
                table: "siparis_kalemi",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_siparis_kalemi_teyit_eden_personel_id",
                table: "siparis_kalemi",
                column: "teyit_eden_personel_id");

            migrationBuilder.AddForeignKey(
                name: "fk_siparis_kalemi_personel_teyit_eden_personel_id",
                table: "siparis_kalemi",
                column: "teyit_eden_personel_id",
                principalTable: "personel",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_siparis_kalemi_personel_teyit_eden_personel_id",
                table: "siparis_kalemi");

            migrationBuilder.DropIndex(
                name: "ix_siparis_kalemi_teyit_eden_personel_id",
                table: "siparis_kalemi");

            migrationBuilder.DropColumn(
                name: "teyit_eden_personel_id",
                table: "siparis_kalemi");
        }
    }
}
