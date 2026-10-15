using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PickToLight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Baslangic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "personel",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ad_soyad = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kullanici_adi = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    parola_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    rol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    nfc_uid = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    aktif = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personel", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "urun",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sku = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ad = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    birim_agirlik_g = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    guvenlik_stogu = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    olusturma = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_urun", x => x.id);
                    table.CheckConstraint("ck_urun_guvenlik_stogu", "guvenlik_stogu >= 0");
                });

            migrationBuilder.CreateTable(
                name: "siparis",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    siparis_no = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    durum = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    personel_id = table.Column<long>(type: "bigint", nullable: true),
                    olusturma = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tamamlanma = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_siparis", x => x.id);
                    table.ForeignKey(
                        name: "fk_siparis_personel_personel_id",
                        column: x => x.personel_id,
                        principalTable: "personel",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "kutu",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    urun_id = table.Column<long>(type: "bigint", nullable: true),
                    cihaz_mac = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: false),
                    mqtt_topic = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    raf_konumu = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    baglanti_durumu = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    son_gorulme = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kutu", x => x.id);
                    table.ForeignKey(
                        name: "fk_kutu_urunler_urun_id",
                        column: x => x.urun_id,
                        principalTable: "urun",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tedarik_talebi",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    urun_id = table.Column<long>(type: "bigint", nullable: false),
                    siparis_id = table.Column<long>(type: "bigint", nullable: true),
                    talep_miktari = table.Column<int>(type: "integer", nullable: false),
                    durum = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    olusturma = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tedarik_talebi", x => x.id);
                    table.ForeignKey(
                        name: "fk_tedarik_talebi_siparis_siparis_id",
                        column: x => x.siparis_id,
                        principalTable: "siparis",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_tedarik_talebi_urunler_urun_id",
                        column: x => x.urun_id,
                        principalTable: "urun",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "alarm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tip = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    mesaj = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    kutu_id = table.Column<long>(type: "bigint", nullable: true),
                    siparis_id = table.Column<long>(type: "bigint", nullable: true),
                    okundu = table.Column<bool>(type: "boolean", nullable: false),
                    zaman = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alarm", x => x.id);
                    table.ForeignKey(
                        name: "fk_alarm_kutular_kutu_id",
                        column: x => x.kutu_id,
                        principalTable: "kutu",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_alarm_siparisler_siparis_id",
                        column: x => x.siparis_id,
                        principalTable: "siparis",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cihaz_olayi",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kutu_id = table.Column<long>(type: "bigint", nullable: true),
                    topic = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    zaman = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cihaz_olayi", x => x.id);
                    table.ForeignKey(
                        name: "fk_cihaz_olayi_kutular_kutu_id",
                        column: x => x.kutu_id,
                        principalTable: "kutu",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "siparis_kalemi",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    siparis_id = table.Column<long>(type: "bigint", nullable: false),
                    urun_id = table.Column<long>(type: "bigint", nullable: false),
                    kutu_id = table.Column<long>(type: "bigint", nullable: true),
                    miktar = table.Column<int>(type: "integer", nullable: false),
                    led_rengi = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    durum = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    teyit_yontemi = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    mesaj_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sira = table.Column<int>(type: "integer", nullable: true),
                    isik_yanma_zamani = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    alinma_zamani = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    stok_rezerve_edildi = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_siparis_kalemi", x => x.id);
                    table.CheckConstraint("ck_kalem_miktar_pozitif", "miktar > 0");
                    table.ForeignKey(
                        name: "fk_siparis_kalemi_kutu_kutu_id",
                        column: x => x.kutu_id,
                        principalTable: "kutu",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_siparis_kalemi_siparis_siparis_id",
                        column: x => x.siparis_id,
                        principalTable: "siparis",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_siparis_kalemi_urunler_urun_id",
                        column: x => x.urun_id,
                        principalTable: "urun",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stok",
                columns: table => new
                {
                    kutu_id = table.Column<long>(type: "bigint", nullable: false),
                    adet = table.Column<int>(type: "integer", nullable: false),
                    son_agirlik_g = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    versiyon = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    guncelleme = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stok", x => x.kutu_id);
                    table.CheckConstraint("ck_stok_adet_negatif_olamaz", "adet >= 0");
                    table.ForeignKey(
                        name: "fk_stok_kutu_kutu_id",
                        column: x => x.kutu_id,
                        principalTable: "kutu",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stok_hareketi",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kutu_id = table.Column<long>(type: "bigint", nullable: false),
                    siparis_kalemi_id = table.Column<long>(type: "bigint", nullable: true),
                    personel_id = table.Column<long>(type: "bigint", nullable: true),
                    tip = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    miktar = table.Column<int>(type: "integer", nullable: false),
                    onceki_adet = table.Column<int>(type: "integer", nullable: false),
                    sonraki_adet = table.Column<int>(type: "integer", nullable: false),
                    aciklama = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    zaman = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stok_hareketi", x => x.id);
                    table.ForeignKey(
                        name: "fk_stok_hareketi_kutu_kutu_id",
                        column: x => x.kutu_id,
                        principalTable: "kutu",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_stok_hareketi_personel_personel_id",
                        column: x => x.personel_id,
                        principalTable: "personel",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_stok_hareketi_siparis_kalemi_siparis_kalemi_id",
                        column: x => x.siparis_kalemi_id,
                        principalTable: "siparis_kalemi",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alarm_kutu_id",
                table: "alarm",
                column: "kutu_id");

            migrationBuilder.CreateIndex(
                name: "ix_alarm_okundu",
                table: "alarm",
                column: "okundu",
                filter: "okundu = false");

            migrationBuilder.CreateIndex(
                name: "ix_alarm_siparis_id",
                table: "alarm",
                column: "siparis_id");

            migrationBuilder.CreateIndex(
                name: "ix_alarm_zaman",
                table: "alarm",
                column: "zaman",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_cihaz_olayi_kutu_id",
                table: "cihaz_olayi",
                column: "kutu_id");

            migrationBuilder.CreateIndex(
                name: "ix_cihaz_olayi_zaman",
                table: "cihaz_olayi",
                column: "zaman",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_kutu_cihaz_mac",
                table: "kutu",
                column: "cihaz_mac",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_kutu_kod",
                table: "kutu",
                column: "kod",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_kutu_urun_id",
                table: "kutu",
                column: "urun_id");

            migrationBuilder.CreateIndex(
                name: "ix_personel_kullanici_adi",
                table: "personel",
                column: "kullanici_adi",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_siparis_durum",
                table: "siparis",
                column: "durum");

            migrationBuilder.CreateIndex(
                name: "ix_siparis_personel_id",
                table: "siparis",
                column: "personel_id");

            migrationBuilder.CreateIndex(
                name: "ix_siparis_siparis_no",
                table: "siparis",
                column: "siparis_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_siparis_kalemi_durum",
                table: "siparis_kalemi",
                column: "durum",
                filter: "durum = 'IsikYandi'");

            migrationBuilder.CreateIndex(
                name: "ix_siparis_kalemi_kutu_id",
                table: "siparis_kalemi",
                column: "kutu_id");

            migrationBuilder.CreateIndex(
                name: "ix_siparis_kalemi_mesaj_id",
                table: "siparis_kalemi",
                column: "mesaj_id",
                unique: true,
                filter: "mesaj_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_siparis_kalemi_siparis_id",
                table: "siparis_kalemi",
                column: "siparis_id");

            migrationBuilder.CreateIndex(
                name: "ix_siparis_kalemi_urun_id",
                table: "siparis_kalemi",
                column: "urun_id");

            migrationBuilder.CreateIndex(
                name: "ix_stok_hareketi_kutu_id_zaman",
                table: "stok_hareketi",
                columns: new[] { "kutu_id", "zaman" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_stok_hareketi_personel_id",
                table: "stok_hareketi",
                column: "personel_id");

            migrationBuilder.CreateIndex(
                name: "ix_stok_hareketi_siparis_kalemi_id",
                table: "stok_hareketi",
                column: "siparis_kalemi_id");

            migrationBuilder.CreateIndex(
                name: "ix_stok_hareketi_zaman",
                table: "stok_hareketi",
                column: "zaman",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_tedarik_talebi_siparis_id",
                table: "tedarik_talebi",
                column: "siparis_id");

            migrationBuilder.CreateIndex(
                name: "ix_tedarik_talebi_urun_id",
                table: "tedarik_talebi",
                column: "urun_id",
                unique: true,
                filter: "durum = 'Acik'");

            migrationBuilder.CreateIndex(
                name: "ix_urun_sku",
                table: "urun",
                column: "sku",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alarm");

            migrationBuilder.DropTable(
                name: "cihaz_olayi");

            migrationBuilder.DropTable(
                name: "stok");

            migrationBuilder.DropTable(
                name: "stok_hareketi");

            migrationBuilder.DropTable(
                name: "tedarik_talebi");

            migrationBuilder.DropTable(
                name: "siparis_kalemi");

            migrationBuilder.DropTable(
                name: "kutu");

            migrationBuilder.DropTable(
                name: "siparis");

            migrationBuilder.DropTable(
                name: "urun");

            migrationBuilder.DropTable(
                name: "personel");
        }
    }
}
