using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickToLight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SiparisOnceligi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "oncelik",
                table: "siparis",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                // Mevcut siparişler Normal önceliklidir: boş değer enum'a çevrilemez
                // ve eski satırlar okunurken hataya yol açardı.
                defaultValue: "Normal");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "oncelik",
                table: "siparis");
        }
    }
}
