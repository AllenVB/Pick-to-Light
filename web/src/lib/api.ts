"use client";

import type {
  Alarm, ApiHatasi, CihazOlay, GunlukHareket, Kutu, KutuPerformans, Oturum, RaporOzet,
  Personel, SiparisDetay, SiparisOzet, StokHareket, TedarikTalebi, Urun, UrunHareket,
} from "@/types";

export const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5080";

const OTURUM_ANAHTARI = "ptl.oturum";

export function oturumOku(): Oturum | null {
  if (typeof window === "undefined") return null;
  try {
    const ham = localStorage.getItem(OTURUM_ANAHTARI);
    if (!ham) return null;
    const oturum = JSON.parse(ham) as Oturum;
    return new Date(oturum.gecerlilikSonu) > new Date() ? oturum : null;
  } catch {
    return null;
  }
}

export function oturumYaz(oturum: Oturum) {
  localStorage.setItem(OTURUM_ANAHTARI, JSON.stringify(oturum));
}

export function oturumSil() {
  localStorage.removeItem(OTURUM_ANAHTARI);
}

/** Backend hatalarını (ProblemDetails) taşıyan hata tipi. */
export class ApiHata extends Error {
  constructor(public readonly durum: number, public readonly govde: ApiHatasi) {
    super(govde.detail ?? govde.title ?? `İstek başarısız (${durum})`);
  }
}

async function istek<T>(yol: string, secenek: RequestInit = {}): Promise<T> {
  const oturum = oturumOku();

  const yanit = await fetch(`${API_URL}${yol}`, {
    ...secenek,
    headers: {
      "Content-Type": "application/json",
      ...(oturum ? { Authorization: `Bearer ${oturum.token}` } : {}),
      ...secenek.headers,
    },
    cache: "no-store",
  });

  if (yanit.status === 401) {
    oturumSil();
    if (typeof window !== "undefined") window.location.href = "/giris";
    throw new ApiHata(401, { title: "Oturum sona erdi" });
  }

  if (!yanit.ok) {
    let govde: ApiHatasi = { title: yanit.statusText };
    try {
      govde = (await yanit.json()) as ApiHatasi;
    } catch {
      /* gövde JSON değilse varsayılanı kullan */
    }
    throw new ApiHata(yanit.status, govde);
  }

  return yanit.status === 204 ? (undefined as T) : ((await yanit.json()) as T);
}

export const api = {
  giris: (kullaniciAdi: string, parola: string) =>
    istek<Oturum>("/api/auth/giris", {
      method: "POST",
      body: JSON.stringify({ kullaniciAdi, parola }),
    }),

  kutular: () => istek<Kutu[]>("/api/kutular"),
  stokDurumu: () => istek<Kutu[]>("/api/stok"),
  urunler: () => istek<Urun[]>("/api/urunler"),

  siparisler: (durum?: string) =>
    istek<SiparisOzet[]>(`/api/siparisler${durum ? `?durum=${durum}` : ""}`),
  siparisDetay: (siparisNo: string) =>
    istek<SiparisDetay>(`/api/siparisler/${encodeURIComponent(siparisNo)}`),
  siparisOlustur: (siparisNo: string, kalemler: { urunId: number; miktar: number }[]) =>
    istek<SiparisDetay>("/api/siparisler", {
      method: "POST",
      body: JSON.stringify({ siparisNo, kalemler }),
    }),
  siparisBaslat: (id: number) =>
    istek<SiparisDetay>(`/api/siparisler/${id}/baslat`, { method: "POST" }),
  siparisIptal: (id: number) =>
    istek<SiparisDetay>(`/api/siparisler/${id}/iptal`, { method: "POST" }),
  kalemTeyit: (kalemId: number, yontem = "Manuel") =>
    istek<{ islendi: boolean; aciklama: string }>(
      `/api/kalemler/${kalemId}/teyit?yontem=${yontem}`, { method: "POST" }),

  alarmlar: (okundu?: boolean, tip?: string) => {
    const p = new URLSearchParams();
    if (okundu !== undefined) p.set("okundu", String(okundu));
    if (tip) p.set("tip", tip);
    const q = p.toString();
    return istek<Alarm[]>(`/api/alarmlar${q ? `?${q}` : ""}`);
  },
  alarmTumunuOkundu: () =>
    istek<{ isaretlenen: number }>("/api/alarmlar/tumunu-okundu", { method: "POST" }),
  alarmOkundu: (id: number) => istek<void>(`/api/alarmlar/${id}/okundu`, { method: "POST" }),
  tedarikTalepleri: () => istek<TedarikTalebi[]>("/api/tedarik-talepleri"),

  raporOzet: (gun = 30) => istek<RaporOzet>(`/api/raporlar/ozet?gun=${gun}`),
  raporUrunler: (gun = 30, limit = 10) =>
    istek<UrunHareket[]>(`/api/raporlar/urunler?gun=${gun}&limit=${limit}`),
  raporKutular: (gun = 30) => istek<KutuPerformans[]>(`/api/raporlar/kutular?gun=${gun}`),
  raporGunluk: (gun = 14) => istek<GunlukHareket[]>(`/api/raporlar/gunluk?gun=${gun}`),

  hareketler: (kutuId?: number) =>
    istek<StokHareket[]>(`/api/hareketler${kutuId ? `?kutuId=${kutuId}` : ""}`),
  stokGiris: (kutuId: number, miktar: number, aciklama?: string) =>
    istek<{ kutuId: number; yeniAdet: number }>(`/api/stok/${kutuId}/giris`, {
      method: "POST",
      body: JSON.stringify({ miktar, aciklama }),
    }),
  stokSayim: (kutuId: number, yeniAdet: number, aciklama?: string) =>
    istek<{ kutuId: number; yeniAdet: number }>(`/api/stok/${kutuId}/sayim`, {
      method: "POST",
      body: JSON.stringify({ yeniAdet, aciklama }),
    }),
  personeller: () => istek<Personel[]>("/api/personeller"),
  personelEkle: (p: { adSoyad: string; kullaniciAdi: string; parola: string; rol: string }) =>
    istek<Personel>("/api/personeller", { method: "POST", body: JSON.stringify(p) }),
  personelGuncelle: (id: number, p: { adSoyad: string; rol: string; aktif: boolean }) =>
    istek<void>(`/api/personeller/${id}`, { method: "PUT", body: JSON.stringify(p) }),
  personelParola: (id: number, yeniParola: string) =>
    istek<void>(`/api/personeller/${id}/parola`, { method: "POST", body: JSON.stringify({ yeniParola }) }),

  urunEkle: (urun: { sku: string; ad: string; birimAgirlikG?: number; guvenlikStogu: number }) =>
    istek<Urun>("/api/urunler", { method: "POST", body: JSON.stringify(urun) }),
  urunGuncelle: (id: number, urun: { sku: string; ad: string; birimAgirlikG?: number; guvenlikStogu: number }) =>
    istek<void>(`/api/urunler/${id}`, { method: "PUT", body: JSON.stringify(urun) }),

  kutuEkle: (kutu: {
    kod: string; urunId?: number; cihazMac: string; rafKonumu?: string; baslangicStogu: number;
  }) => istek<Kutu>("/api/kutular", { method: "POST", body: JSON.stringify(kutu) }),

  kutuGuncelle: (id: number, kutu: {
    kod: string; urunId?: number; cihazMac: string; rafKonumu?: string; baslangicStogu: number;
  }) => istek<void>(`/api/kutular/${id}`, { method: "PUT", body: JSON.stringify(kutu) }),

  cihazOlaylari: (kutuId?: number, limit = 100) =>
    istek<CihazOlay[]>(`/api/kutular/olaylar?limit=${limit}${kutuId ? `&kutuId=${kutuId}` : ""}`),

  ledTest: (kutuId: number, renk = "Yesil") =>
    istek<unknown>(`/api/kutular/${kutuId}/led-test?renk=${renk}`, { method: "POST" }),
};
