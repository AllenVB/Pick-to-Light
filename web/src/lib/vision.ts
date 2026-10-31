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
  kameraSorunu?: "donuk" | null;
}

export interface OgretilenProfil {
  urunId: number;
  ad: string;
  ornekSayisi: number;
}

export interface SahneNesnesi {
  tip: "civata" | "somun" | "rondela";
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

export const vision = {
  onizlemeUrl: `${VISION_URL}/onizleme`,
  saglik: () => cagir<VisionSaglik>("/saglik", {}, 1500),
  tani: () => cagir<TaniYaniti>("/tani", {}, 2000),
  profiller: () => cagir<OgretilenProfil[]>("/profiller"),
  ogret: (urunId: number, ad: string) =>
    cagir<{ eklenenOrnek: number; toplamOrnek: number }>(`/ogret/${urunId}`, {
      method: "POST", body: JSON.stringify({ ad }),
    }),
  ogretileniSil: (urunId: number) => cagir<{ silindi: number }>(`/ogret/${urunId}`, { method: "DELETE" }),
  simSahne: (nesneler: SahneNesnesi[]) =>
    cagir<{ nesneler: SahneNesnesi[] }>("/sim/sahne", { method: "POST", body: JSON.stringify({ nesneler }) }),
};
