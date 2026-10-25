using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickToLight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BaskulAdetOlcumu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "agirlik_zamani",
                table: "stok",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "baslangic_agirligi_g",
                table: "siparis_kalemi",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "olculen_adet",
                table: "siparis_kalemi",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "olculen_agirlik_g",
                table: "siparis_kalemi",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "agirlik_zamani",
                table: "stok");

            migrationBuilder.DropColumn(
                name: "baslangic_agirligi_g",
                table: "siparis_kalemi");

            migrationBuilder.DropColumn(
                name: "olculen_adet",
                table: "siparis_kalemi");

            migrationBuilder.DropColumn(
                name: "olculen_agirlik_g",
                table: "siparis_kalemi");
        }
    }
}
