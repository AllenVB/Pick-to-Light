"use client";

/**
 * Görüntü işleme servisi (Python/OpenCV) istemcisi.
 *
 * Servis ayrı bir süreçtir ve kapalı olabilir; bu yüzden tüm çağrılar hata yerine `null`
 * döner. Sistem kamera olmadan da (yalnızca baskülle) çalışmaya devam eder.
 */
export const VISION_URL = process.env.NEXT_PUBLIC_VISION_URL ?? "http://localhost:5100";

export interface Tespit {
  urunId: number | null;
  urunAdi: string | null;
  guven: number;
  kutu: [number, number, number, number];
  alan: number;
}

export interface BaskinUrun {
  urunId: number;
  urunAdi: string;
  guven: number;
  adet: number;
}

export interface TaniYaniti {
  kaynak: string;
  tespitler: Tespit[];
  baskin: BaskinUrun | null;
  toplamAdet: number;
}

export interface VisionSaglik {
  durum: string;
  kaynak: string;
  sanal: boolean;
  kareVar: boolean;
  profilSayisi: number;
  /** "donuk": kamera açık ama canlı görüntü yok (gizlilik izni ya da başka uygulama). */
  kameraSorunu?: "donuk" | "bekliyor" | null;
  /** Kurulum uyarıları (boş = sağlıklı): karanlik, patlak, duzensiz_isik, bulanik. */
  goruntuUyarilari?: string[];
  /** Seçilebilir görüntü kaynakları: sim, tarayici, kamera:0 ... */
  kaynaklar?: string[];
}

/** "Fotoğraf çek" sonucu: çekilen kare ve karedeki ürünlerin tespiti (aynı kareden). */
export interface FotografSonucu {
  /** Kaydet bu kimlikle çağrılır (sunucu son fotoğrafları kısa süre saklar). */
  fotografId: string;
  kaynak: string;
  /** data:image/jpeg;base64,... */
  goruntu: string;
  genislik: number;
  yukseklik: number;
  tespitler: Tespit[];
  urunler: { urunId: number; urunAdi: string; adet: number }[];
  bilinmeyen: number;
  nesneSayisi: number;
}

/** Kullanıcının doğruladığı, kaydedilmiş fotoğraf. */
export interface FotografKaydi {
  id: number;
  zaman: number;
  onaylayan: string | null;
  urunler: { urunId: number; urunAdi: string; adet: number }[];
  /** Kullanıcı tespit edilen adetleri düzelttiyse true. */
  duzeltildi: boolean;
  ozet: string;
}

export type DogrulamaDurumu = "dogru" | "yanlis" | "taninmadi" | "bekliyor" | "belirsiz" | "ogretilmemis";

export interface DogrulamaYaniti {
  durum: DogrulamaDurumu;
  beklenenId: number;
  algilanan: { urunId: number; urunAdi: string | null; guven: number } | null;
  nesneSayisi: number;
  guven: number;
}

export interface OgretilenProfil {
  urunId: number;
  ad: string;
  ornekSayisi: number;
  /** Kaç kez "Örnek ekle"ye basıldı (her çekim birkaç örnek üretir). */
  cekimSayisi: number;
}

export interface SahneNesnesi {
  tip: "civata" | "somun" | "rondela" | "vida";
  adet: number;
}

async function cagir<T>(yol: string, secenek: RequestInit = {}, zamanAsimiMs = 3000): Promise<T | null> {
  const iptal = new AbortController();
  const sayac = setTimeout(() => iptal.abort(), zamanAsimiMs);
  try {
    const yanit = await fetch(`${VISION_URL}${yol}`, {
      ...secenek,
      headers: { "Content-Type": "application/json", ...secenek.headers },
      signal: iptal.signal,
      cache: "no-store",
    });
    if (!yanit.ok) {
      const govde = await yanit.json().catch(() => ({}));
      throw new Error((govde as { detail?: string }).detail ?? `İstek başarısız (${yanit.status})`);
    }
    return (await yanit.json()) as T;
  } catch (hata) {
    if (hata instanceof Error && hata.name !== "AbortError" && !(hata instanceof TypeError)) throw hata;
    return null;   // servis kapalı / zaman aşımı
  } finally {
    clearTimeout(sayac);
  }
}

/** Tarayıcı kamerasının karesini (JPEG) servise gönderir; başarısızsa sessizce false döner. */
async function kareGonder(kare: Blob): Promise<boolean> {
  const iptal = new AbortController();
  const sayac = setTimeout(() => iptal.abort(), 2000);
  try {
    const yanit = await fetch(`${VISION_URL}/kare`, {
      method: "POST", headers: { "Content-Type": "image/jpeg" }, body: kare, signal: iptal.signal,
    });
    return yanit.ok;
  } catch {
    return false;
  } finally {
    clearTimeout(sayac);
  }
}

export const vision = {
  kareGonder,
  kaynakSec: (tip: string) =>
    cagir<{ kaynak: string; kaynaklar: string[] }>("/kaynak", { method: "POST", body: JSON.stringify({ tip }) }),
  onizlemeUrl: `${VISION_URL}/onizleme`,
  saglik: () => cagir<VisionSaglik>("/saglik", {}, 1500),
  tani: () => cagir<TaniYaniti>("/tani", {}, 2000),
  fotografCek: () => cagir<FotografSonucu>("/fotograf", { method: "POST" }, 8000),
  /** Kullanıcı ürün ve adetleri doğruladıktan sonra çağrılır; onaysız kayıt oluşmaz. */
  fotografKaydet: (govde: { fotografId: string; onaylayan?: string | null; urunler?: { urunId: number; adet: number }[] }) =>
    cagir<FotografKaydi>("/fotograf/kaydet", { method: "POST", body: JSON.stringify({ ...govde, onaylandi: true }) }, 8000),
  fotografKayitlari: (limit = 5) => cagir<FotografKaydi[]>(`/fotograf/kayitlar?limit=${limit}`),
  fotografKaydiUrl: (id: number) => `${VISION_URL}/fotograf/kayitlar/${id}.jpg`,
  dogrula: (urunId: number) => cagir<DogrulamaYaniti>(`/dogrula/${urunId}`, {}, 2000),
  profiller: () => cagir<OgretilenProfil[]>("/profiller"),
  ogret: (urunId: number, ad: string) =>
    cagir<{ eklenenOrnek: number; toplamOrnek: number; cekimSayisi: number }>(`/ogret/${urunId}`, {
      method: "POST", body: JSON.stringify({ ad }),
    }),
  ogretmeyiGeriAl: (urunId: number) =>
    cagir<{ toplamOrnek: number; cekimSayisi: number }>(`/ogret/${urunId}/son`, { method: "DELETE" }),
  ogretileniSil: (urunId: number) => cagir<{ silindi: number }>(`/ogret/${urunId}`, { method: "DELETE" }),
  simSahne: (nesneler: SahneNesnesi[]) =>
    cagir<{ nesneler: SahneNesnesi[] }>("/sim/sahne", { method: "POST", body: JSON.stringify({ nesneler }) }),
};
