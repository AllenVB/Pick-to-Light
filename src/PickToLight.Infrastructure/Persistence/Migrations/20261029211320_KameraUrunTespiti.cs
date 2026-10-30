using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickToLight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KameraUrunTespiti : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "tespit_edilen_urun_id",
                table: "siparis_kalemi",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tespit_guveni",
                table: "siparis_kalemi",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_siparis_kalemi_tespit_edilen_urun_id",
                table: "siparis_kalemi",
                column: "tespit_edilen_urun_id");

            migrationBuilder.AddForeignKey(
                name: "fk_siparis_kalemi_urunler_tespit_edilen_urun_id",
                table: "siparis_kalemi",
                column: "tespit_edilen_urun_id",
                principalTable: "urun",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_siparis_kalemi_urunler_tespit_edilen_urun_id",
                table: "siparis_kalemi");

            migrationBuilder.DropIndex(
                name: "ix_siparis_kalemi_tespit_edilen_urun_id",
                table: "siparis_kalemi");

            migrationBuilder.DropColumn(
                name: "tespit_edilen_urun_id",
                table: "siparis_kalemi");

            migrationBuilder.DropColumn(
                name: "tespit_guveni",
                table: "siparis_kalemi");
        }
    }
}
