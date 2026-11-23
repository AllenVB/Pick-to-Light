import type { PersonelRolu } from "@/types";

/**
 * Uygulamanın gezinme yapısı: her şey dört bölümde toplanır. Yan menüde yalnızca bu dört bölüm görünür;
 * bölümün sayfaları sayfa üstündeki sekme çubuğunda listelenir. Yönetici sayfaları operatöre gösterilmez
 * (backend yetkileriyle aynı çizgi: `[Authorize(Roles = "ADMIN")]`).
 */
export interface AltSayfa {
  yol: string;
  ad: string;
  /** true: yalnızca yönetici görür. */
  yonetici?: boolean;
}

export interface Bolum {
  id: "siparis" | "pano" | "stok" | "paketleme";
  ad: string;
  aciklama: string;
  ikon: string;
  /** Yan menüde bölüme tıklanınca açılan sayfa. */
  giris: string;
  /** Bölüme ait dinamik/alt yollar (ör. /siparis/E2E-1 sipariş detayı). */
  onekler: string[];
  sayfalar: AltSayfa[];
}

export const BOLUMLER: Bolum[] = [
  {
    id: "siparis",
    ad: "Sipariş Kontrol Paneli",
    aciklama: "Sipariş oluştur, takip et ve topla",
    ikon: "☰",
    giris: "/siparisler",
    onekler: ["/siparis"],
    sayfalar: [
      { yol: "/siparisler", ad: "Siparişler" },
      { yol: "/siparis/yeni", ad: "Yeni Sipariş" },
      { yol: "/operator", ad: "Operatör Ekranı" },
      { yol: "/siparis/ice-aktar", ad: "Toplu Aktarma", yonetici: true },
    ],
  },
  {
    id: "pano",
    ad: "Canlı Pano",
    aciklama: "Kutular, alarmlar, raporlar ve personel",
    ikon: "▦",
    giris: "/",
    onekler: [],
    sayfalar: [
      { yol: "/", ad: "Pano" },
      { yol: "/alarmlar", ad: "Alarmlar" },
      { yol: "/kutular", ad: "Kutu & Cihaz", yonetici: true },
      { yol: "/cihaz-olaylari", ad: "Cihaz Olayları", yonetici: true },
      { yol: "/raporlar", ad: "Raporlar", yonetici: true },
      { yol: "/personeller", ad: "Personel", yonetici: true },
    ],
  },
  {
    id: "stok",
    ad: "Stok Durumu",
    aciklama: "Stok, ürünler, hareketler ve sayım",
    ikon: "▤",
    giris: "/stok",
    onekler: [],
    sayfalar: [
      { yol: "/stok", ad: "Stok" },
      { yol: "/urunler", ad: "Ürünler", yonetici: true },
      { yol: "/hareketler", ad: "Hareket Defteri", yonetici: true },
      { yol: "/tarti", ad: "Tartı & Sayım", yonetici: true },
    ],
  },
  {
    id: "paketleme",
    ad: "Paketleme İşlemleri",
    aciklama: "Paket doğrulama ve kamera tanıma",
    ikon: "▧",
    giris: "/paketleme",
    onekler: [],
    sayfalar: [
      { yol: "/paketleme", ad: "Paketleme" },
      { yol: "/kamera", ad: "Kamera & Tanıma", yonetici: true },
    ],
  },
];

function eslesir(yol: string, onek: string): boolean {
  return onek === "/" ? yol === "/" : yol === onek || yol.startsWith(onek + "/");
}

/** Yolun ait olduğu bölüm: en uzun eşleşen önek kazanır (/siparis/yeni → Sipariş, / → Canlı Pano). */
export function bolumBul(yol: string): Bolum | null {
  let en: { bolum: Bolum; uzunluk: number } | null = null;
  for (const b of BOLUMLER) {
    for (const onek of [...b.sayfalar.map((s) => s.yol), ...b.onekler]) {
      if (eslesir(yol, onek) && (!en || onek.length > en.uzunluk)) en = { bolum: b, uzunluk: onek.length };
    }
  }
  return en?.bolum ?? null;
}

export function gorunenSayfalar(bolum: Bolum, rol: PersonelRolu | null): AltSayfa[] {
  const yonetici = rol === "Admin";
  return bolum.sayfalar.filter((s) => !s.yonetici || yonetici);
}

/** Sekme çubuğunda hangi sayfa seçili: en uzun eşleşen sayfa yolu. */
export function seciliSayfa(bolum: Bolum, yol: string): string | null {
  let secili: string | null = null;
  for (const s of bolum.sayfalar) {
    if (eslesir(yol, s.yol) && (!secili || s.yol.length > secili.length)) secili = s.yol;
  }
  return secili;
}
