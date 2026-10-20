using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickToLight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Arsivleme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "aktif",
                table: "urun",
                type: "boolean",
                nullable: false,
                // Mevcut kutu ve ürünler aktiftir; varsayılan false olsaydı
                // migration tüm envanteri arşivler ve hiçbir ışık yanmazdı.
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "aktif",
                table: "kutu",
                type: "boolean",
                nullable: false,
                // Mevcut kutu ve ürünler aktiftir; varsayılan false olsaydı
                // migration tüm envanteri arşivler ve hiçbir ışık yanmazdı.
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "aktif",
                table: "urun");

            migrationBuilder.DropColumn(
                name: "aktif",
                table: "kutu");
        }
    }
}
