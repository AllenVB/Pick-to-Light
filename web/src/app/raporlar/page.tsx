"use client";

import { useCallback, useEffect, useState } from "react";
import { api } from "@/lib/api";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import type { GunlukHareket, KutuPerformans, RaporOzet, UrunHareket } from "@/types";

const DONEMLER = [7, 30, 90];

export default function RaporlarSayfasi() {
  const oturum = useKorumaliSayfa();
  const [gun, setGun] = useState(30);
  const [ozet, setOzet] = useState<RaporOzet | null>(null);
  const [urunler, setUrunler] = useState<UrunHareket[]>([]);
  const [kutular, setKutular] = useState<KutuPerformans[]>([]);
  const [gunluk, setGunluk] = useState<GunlukHareket[]>([]);
  const [hata, setHata] = useState<string | null>(null);

  const yenile = useCallback(async () => {
    try {
      const [o, u, k, g] = await Promise.all([
        api.raporOzet(gun), api.raporUrunler(gun), api.raporKutular(gun), api.raporGunluk(Math.min(gun, 30)),
      ]);
      setOzet(o); setUrunler(u); setKutular(k); setGunluk(g); setHata(null);
    } catch {
      setHata("Rapor verileri alınamadı.");
    }
  }, [gun]);

  useEffect(() => { if (oturum) yenile(); }, [oturum, yenile]);

  if (!oturum) return null;

  const enCokCikis = Math.max(1, ...urunler.map((u) => u.toplamCikis));
  const enCokGunluk = Math.max(1, ...gunluk.map((g) => g.cikisAdedi + g.girisAdedi));

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <p className="etiket">Raporlar</p>
          <h1 className="baslik mt-1">Toplama performansı</h1>
          <p className="mt-1 text-sm text-slate-400">
            Tüm değerler değişmez hareket defterinden hesaplanır.
          </p>
        </div>
        <div className="flex gap-1">
          {DONEMLER.map((d) => (
            <button key={d}
              className={d === gun ? "dugme px-3 py-1.5 text-xs" : "dugme-ikincil px-3 py-1.5 text-xs"}
              onClick={() => setGun(d)}>
              Son {d} gün
            </button>
          ))}
        </div>
      </header>

      {hata && <p className="rounded-lg border border-durum-kirmizi/40 bg-durum-kirmizi/10 px-4 py-2 text-sm text-durum-kirmizi">{hata}</p>}

      {ozet && (
        <section className="grid grid-cols-2 gap-4 lg:grid-cols-4">
          <Kutucuk baslik="Sipariş" deger={ozet.toplamSiparis}
            alt={`${ozet.tamamlananSiparis} tamamlandı · ${ozet.kilitliSiparis} kilitli`} />
          <Kutucuk baslik="Toplanan" deger={ozet.toplananAdet} alt={`${ozet.toplananKalem} kalemde`} vurgu />
          <Kutucuk baslik="Ort. toplama"
            deger={ozet.ortalamaToplamaSaniye != null ? `${ozet.ortalamaToplamaSaniye} sn` : "—"}
            alt="ışık yanmasından teyide" />
          <Kutucuk baslik="Kırmızı oranı" deger={`%${ozet.kirmiziOrani}`}
            alt={`${ozet.yetersizKalem} yetersiz kalem`} tehlike={ozet.kirmiziOrani > 10} />
        </section>
      )}

      <div className="grid gap-4 lg:grid-cols-2">
        <section className="kart">
          <h2 className="etiket mb-3">En çok toplanan ürünler</h2>
          <div className="space-y-2">
            {urunler.map((u) => (
              <div key={u.urunId}>
                <div className="flex justify-between text-sm">
                  <span>{u.urunAdi} <span className="text-[11px] text-slate-500">{u.sku}</span></span>
                  <span className="font-bold">{u.toplamCikis} adet</span>
                </div>
                <div className="mt-1 h-2 overflow-hidden rounded-full bg-zemin">
                  <div className="h-full bg-marka" style={{ width: `${(u.toplamCikis / enCokCikis) * 100}%` }} />
                </div>
                <p className="mt-0.5 text-[11px] text-slate-500">{u.toplamaSayisi} toplama işlemi</p>
              </div>
            ))}
            {urunler.length === 0 && <p className="text-sm text-slate-500">Bu dönemde çıkış hareketi yok.</p>}
          </div>
        </section>

        <section className="kart">
          <h2 className="etiket mb-3">Günlük hareket (çıkış / giriş)</h2>
          <div className="space-y-2">
            {gunluk.map((g) => (
              <div key={g.gun} className="flex items-center gap-3">
                <span className="w-20 shrink-0 text-[11px] text-slate-400">
                  {new Date(g.gun).toLocaleDateString("tr-TR", { day: "2-digit", month: "short" })}
                </span>
                <div className="flex h-3 flex-1 overflow-hidden rounded-full bg-zemin">
                  <div className="h-full bg-durum-kirmizi/70"
                    style={{ width: `${(g.cikisAdedi / enCokGunluk) * 100}%` }} title={`${g.cikisAdedi} çıkış`} />
                  <div className="h-full bg-durum-yesil/70"
                    style={{ width: `${(g.girisAdedi / enCokGunluk) * 100}%` }} title={`${g.girisAdedi} giriş`} />
                </div>
                <span className="w-24 shrink-0 text-right text-[11px] text-slate-400">
                  ↓{g.cikisAdedi} ↑{g.girisAdedi}
                </span>
              </div>
            ))}
            {gunluk.length === 0 && <p className="text-sm text-slate-500">Bu dönemde hareket yok.</p>}
          </div>
        </section>
      </div>

      <section className="kart overflow-x-auto">
        <h2 className="etiket mb-3">Kutu performansı</h2>
        <table className="tablo">
          <thead>
            <tr><th>Kutu</th><th>Ürün</th><th>Toplama</th><th>Toplam adet</th>
              <th>Ort. süre</th><th>Mevcut stok</th></tr>
          </thead>
          <tbody>
            {kutular.map((k) => (
              <tr key={k.kutuId}>
                <td className="font-mono text-xs font-bold">{k.kutuKod}</td>
                <td>{k.urunAdi ?? "—"}</td>
                <td>{k.toplamaSayisi}</td>
                <td className="font-bold">{k.toplamAdet}</td>
                <td>{k.ortalamaToplamaSaniye != null ? `${k.ortalamaToplamaSaniye} sn` : "—"}</td>
                <td>{k.mevcutStok}</td>
              </tr>
            ))}
            {kutular.length === 0 && <tr><td colSpan={6} className="text-slate-500">Kutu yok.</td></tr>}
          </tbody>
        </table>
      </section>
    </div>
  );
}

function Kutucuk({ baslik, deger, alt, vurgu, tehlike }: {
  baslik: string; deger: number | string; alt: string; vurgu?: boolean; tehlike?: boolean;
}) {
  return (
    <div className="kart">
      <p className="etiket">{baslik}</p>
      <p className={`mt-1 text-2xl font-extrabold ${tehlike ? "text-durum-kirmizi" : vurgu ? "text-marka-acik" : ""}`}>
        {deger}
      </p>
      <p className="text-[11px] text-slate-400">{alt}</p>
    </div>
  );
}
