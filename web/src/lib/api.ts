"use client";

import type {
  Alarm, ApiHatasi, Kutu, Oturum, SiparisDetay, SiparisOzet,
  StokHareket, TedarikTalebi, Urun,
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

  alarmlar: (okundu?: boolean) =>
    istek<Alarm[]>(`/api/alarmlar${okundu === undefined ? "" : `?okundu=${okundu}`}`),
  alarmOkundu: (id: number) => istek<void>(`/api/alarmlar/${id}/okundu`, { method: "POST" }),
  tedarikTalepleri: () => istek<TedarikTalebi[]>("/api/tedarik-talepleri"),

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
  ledTest: (kutuId: number, renk = "Yesil") =>
    istek<unknown>(`/api/kutular/${kutuId}/led-test?renk=${renk}`, { method: "POST" }),
};
