using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickToLight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CihazTelemetrisi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "firmware_surumu",
                table: "kutu",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ip_adresi",
                table: "kutu",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "rssi",
                table: "kutu",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "firmware_surumu",
                table: "kutu");

            migrationBuilder.DropColumn(
                name: "ip_adresi",
                table: "kutu");

            migrationBuilder.DropColumn(
                name: "rssi",
                table: "kutu");
        }
    }
}
