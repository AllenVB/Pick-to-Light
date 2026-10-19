"use client";

import { useCallback, useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { ApiHata, api } from "@/lib/api";
import { useCanliOlaylar } from "@/hooks/useCanliOlaylar";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import { GeriSayim } from "@/components/GeriSayim";
import { KalemRozeti, RenkRozeti, SiparisRozeti } from "@/components/Rozet";
import type { SiparisDetay } from "@/types";

export default function SiparisDetaySayfasi() {
  const oturum = useKorumaliSayfa();
  const { no } = useParams<{ no: string }>();
  const [detay, setDetay] = useState<SiparisDetay | null>(null);
  const [hata, setHata] = useState<string | null>(null);
  const [islem, setIslem] = useState(false);

  const yenile = useCallback(async () => {
    try {
      setDetay(await api.siparisDetay(decodeURIComponent(no)));
    } catch (h) {
      setHata(h instanceof ApiHata ? h.message : "Sipariş yüklenemedi.");
    }
  }, [no]);

  useEffect(() => { if (oturum) yenile(); }, [oturum, yenile]);

  useCanliOlaylar({
    KalemTeyitEdildi: yenile,
    SiparisDurumDegisti: yenile,
    StokGuncellendi: yenile,
    KutuIsikDegisti: yenile,
  }, [no]);

  async function calistir(f: () => Promise<unknown>) {
    setIslem(true);
    setHata(null);
    try { await f(); await yenile(); }
    catch (h) { setHata(h instanceof ApiHata ? h.message : "İşlem başarısız."); }
    finally { setIslem(false); }
  }

  if (!oturum) return null;
  if (!detay) return <p className="text-sm text-slate-400">{hata ?? "Yükleniyor…"}</p>;

  const alinan = detay.kalemler.filter((k) => k.durum === "Alindi").length;
  const bitti = detay.durum === "Tamamlandi" || detay.durum === "Iptal";

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <p className="etiket">Toplama ekranı</p>
          <h1 className="baslik mt-1 flex items-center gap-3" data-test="siparis-basligi">
            {detay.siparisNo} <SiparisRozeti durum={detay.durum} />
          </h1>
          <p className="mt-1 text-sm text-slate-400">
            {alinan}/{detay.kalemler.length} kalem alındı ·{" "}
            {new Date(detay.olusturma).toLocaleString("tr-TR")}
          </p>
        </div>

        <div className="flex gap-2">
          {detay.durum !== "Tamamlandi" && detay.durum !== "Iptal" && (
            <button className="dugme" disabled={islem}
              onClick={() => calistir(() => api.siparisBaslat(detay.id))}>
              {detay.durum === "Bekliyor" ? "Başlat (ışıkları yak)" : "Yeniden dene"}
            </button>
          )}
          {!bitti && (
            <button className="dugme-ikincil" disabled={islem}
              onClick={() => calistir(() => api.siparisIptal(detay.id))}>
              İptal et (stoğu iade et)
            </button>
          )}
        </div>
      </header>

      <div className="h-2 w-full overflow-hidden rounded-full bg-zemin-kart">
        <div className="h-full bg-durum-yesil transition-all"
          style={{ width: `${(alinan / Math.max(detay.kalemler.length, 1)) * 100}%` }} />
      </div>

      {hata && (
        <p className="rounded-lg border border-durum-kirmizi/40 bg-durum-kirmizi/10 px-4 py-3 text-sm text-durum-kirmizi">
          {hata}
        </p>
      )}

      <section className="kart overflow-x-auto">
        <table className="tablo">
          <thead>
            <tr>
              <th>#</th><th>Ürün</th><th>Kutu</th><th>Raf</th><th>Miktar</th>
              <th>Kalan stok</th><th>Işık</th><th>Durum</th><th></th>
            </tr>
          </thead>
          <tbody>
            {detay.kalemler.map((k, i) => (
              <tr key={k.kalemId} data-test="kalem-satiri" data-durum={k.durum}
                className={k.durum === "IsikYandi" ? "bg-marka/5" : ""}>
                <td className="text-slate-500">{k.sira ?? i + 1}</td>
                <td>
                  <span className="font-semibold">{k.urunAdi}</span>
                  <span className="block text-[11px] text-slate-500">{k.sku}</span>
                  {k.uyari && <span className="block text-[11px] text-durum-sari">⚠ {k.uyari}</span>}
                  {k.hata && <span className="block text-[11px] text-durum-kirmizi">✕ {k.hata}</span>}
                </td>
                <td className="font-mono text-xs">{k.kutuKod ?? "—"}</td>
                <td className="text-xs text-slate-400">{k.rafKonumu ?? "—"}</td>
                <td className="font-bold">{k.miktar}</td>
                <td className="text-slate-300">{k.kalanStok ?? "—"}</td>
                <td><RenkRozeti renk={k.ledRengi} /></td>
                <td>
                  <KalemRozeti durum={k.durum} />
                  {k.durum === "Sirada" && (
                    <p className="mt-1 text-[11px] text-durum-sari">
                      Kutu başka bir sipariş için yanıyor; sırası gelince otomatik yanacak.
                    </p>
                  )}
                  {k.durum === "IsikYandi" && (
                    <span className="mt-1 block"><GeriSayim sonTarih={k.teyitSonTarihi} /></span>
                  )}
                </td>
                <td>
                  {k.durum === "IsikYandi" && (
                    <button className="dugme-ikincil px-3 py-1 text-xs" disabled={islem}
                      onClick={() => calistir(() => api.kalemTeyit(k.kalemId))}>
                      Manuel teyit
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      {detay.alarmlar.length > 0 && (
        <section className="kart" data-test="siparis-alarmlari">
          <h2 className="etiket mb-3">Bu siparişin alarmları</h2>
          <ul className="space-y-2">
            {detay.alarmlar.map((a) => (
              <li key={a.id} className="rounded-lg border border-durum-kirmizi/30 bg-durum-kirmizi/10 px-3 py-2 text-sm">
                <b className="text-durum-kirmizi">{a.tip}</b> · {a.mesaj}
                <span className="ml-2 text-[11px] text-slate-500">
                  {new Date(a.zaman).toLocaleTimeString("tr-TR")}
                </span>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}
