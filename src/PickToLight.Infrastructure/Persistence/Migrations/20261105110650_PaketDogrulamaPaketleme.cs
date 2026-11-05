using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PickToLight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaketDogrulamaPaketleme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "paketleme_istisna_nedeni",
                table: "siparis",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "paketlenme_zamani",
                table: "siparis",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "paketleyen_personel_id",
                table: "siparis",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "siparis_dogrulama",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    siparis_id = table.Column<long>(type: "bigint", nullable: false),
                    zaman = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    personel_id = table.Column<long>(type: "bigint", nullable: true),
                    onaylandi = table.Column<bool>(type: "boolean", nullable: false),
                    kamera_kullanildi = table.Column<bool>(type: "boolean", nullable: false),
                    beklenen_agirlik_g = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    net_agirlik_g = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    hata_ozeti = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    detay = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_siparis_dogrulama", x => x.id);
                    table.ForeignKey(
                        name: "fk_siparis_dogrulama_personel_personel_id",
                        column: x => x.personel_id,
                        principalTable: "personel",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_siparis_dogrulama_siparis_siparis_id",
                        column: x => x.siparis_id,
                        principalTable: "siparis",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_siparis_paketleyen_personel_id",
                table: "siparis",
                column: "paketleyen_personel_id");

            migrationBuilder.CreateIndex(
                name: "ix_siparis_dogrulama_personel_id",
                table: "siparis_dogrulama",
                column: "personel_id");

            migrationBuilder.CreateIndex(
                name: "ix_siparis_dogrulama_siparis_id_zaman",
                table: "siparis_dogrulama",
                columns: new[] { "siparis_id", "zaman" });

            migrationBuilder.AddForeignKey(
                name: "fk_siparis_personel_paketleyen_personel_id",
                table: "siparis",
                column: "paketleyen_personel_id",
                principalTable: "personel",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_siparis_personel_paketleyen_personel_id",
                table: "siparis");

            migrationBuilder.DropTable(
                name: "siparis_dogrulama");

            migrationBuilder.DropIndex(
                name: "ix_siparis_paketleyen_personel_id",
                table: "siparis");

            migrationBuilder.DropColumn(
                name: "paketleme_istisna_nedeni",
                table: "siparis");

            migrationBuilder.DropColumn(
                name: "paketlenme_zamani",
                table: "siparis");

            migrationBuilder.DropColumn(
                name: "paketleyen_personel_id",
                table: "siparis");
        }
    }
}
