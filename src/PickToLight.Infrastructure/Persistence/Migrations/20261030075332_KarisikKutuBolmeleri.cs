using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickToLight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KarisikKutuBolmeleri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "cihaz_mac",
                table: "kutu",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(17)",
                oldMaxLength: 17);

            migrationBuilder.AddColumn<long>(
                name: "ana_kutu_id",
                table: "kutu",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_kutu_ana_kutu_id",
                table: "kutu",
                column: "ana_kutu_id");

            migrationBuilder.AddForeignKey(
                name: "fk_kutu_kutu_ana_kutu_id",
                table: "kutu",
                column: "ana_kutu_id",
                principalTable: "kutu",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_kutu_kutu_ana_kutu_id",
                table: "kutu");

            migrationBuilder.DropIndex(
                name: "ix_kutu_ana_kutu_id",
                table: "kutu");

            migrationBuilder.DropColumn(
                name: "ana_kutu_id",
                table: "kutu");

            migrationBuilder.AlterColumn<string>(
                name: "cihaz_mac",
                table: "kutu",
                type: "character varying(17)",
                maxLength: 17,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24);
        }
    }
}
