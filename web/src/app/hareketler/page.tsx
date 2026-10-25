"use client";

import { useCallback, useEffect, useState } from "react";
import { api } from "@/lib/api";
import { useCanliOlaylar } from "@/hooks/useCanliOlaylar";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import { IskeletSatir } from "@/components/Durumlar";
import type { Kutu, StokHareket } from "@/types";

const TIP_SINIF: Record<string, string> = {
  Cikis: "text-durum-kirmizi",
  Giris: "text-durum-yesil",
  Sayim: "text-durum-sari",
};

export default function HareketlerSayfasi() {
  const oturum = useKorumaliSayfa("Admin");
  const [hareketler, setHareketler] = useState<StokHareket[]>([]);
  const [kutular, setKutular] = useState<Kutu[]>([]);
  const [toplam, setToplam] = useState(0);
  const [yukleniyor, setYukleniyor] = useState(true);

  const [kutuId, setKutuId] = useState("");
  const [tip, setTip] = useState("");
  const [baslangic, setBaslangic] = useState("");
  const [bitis, setBitis] = useState("");
  const [sayfa, setSayfa] = useState(1);

  const BOYUT = 50;

  const yenile = useCallback(() => {
    api.hareketler({
      kutuId: kutuId ? Number(kutuId) : undefined,
      tip: tip || undefined,
      baslangic: baslangic ? new Date(baslangic).toISOString() : undefined,
      // Bitiş tarihi dahil olsun diye ertesi günün başlangıcı gönderilir.
      bitis: bitis ? new Date(new Date(bitis).getTime() + 86_400_000).toISOString() : undefined,
      sayfa,
      boyut: BOYUT,
    })
      .then((s) => { setHareketler(s.liste); setToplam(s.toplam); })
      .finally(() => setYukleniyor(false))
      .catch(() => { setHareketler([]); setToplam(0); });
  }, [kutuId, tip, baslangic, bitis, sayfa]);

  useEffect(() => { setSayfa(1); }, [kutuId, tip, baslangic, bitis]);
  useEffect(() => { if (oturum) api.kutular().then(setKutular).catch(() => setKutular([])); }, [oturum]);
  useEffect(() => { if (oturum) yenile(); }, [oturum, yenile]);
  useCanliOlaylar({ StokGuncellendi: yenile }, [kutuId, tip, baslangic, bitis, sayfa]);

  const sonSayfa = Math.max(1, Math.ceil(toplam / BOYUT));
  const filtreVar = Boolean(kutuId || tip || baslangic || bitis);

  if (!oturum) return null;

  return (
    <div className="space-y-6">
      <header>
        <p className="etiket">İzlenebilirlik</p>
        <h1 className="baslik mt-1">Stok hareket defteri</h1>
        <p className="mt-1 text-sm text-slate-400">
          Değişmez kayıt: her stok değişimi kim, ne zaman, hangi sipariş için sorularıyla geri izlenebilir.
        </p>
      </header>

      <section className="kart flex flex-wrap items-end gap-3">
        <div>
          <label className="etiket">Kutu</label>
          <select className="girdi mt-1 w-44" value={kutuId} onChange={(e) => setKutuId(e.target.value)}
            data-test="hareket-kutu">
            <option value="">Tüm kutular</option>
            {kutular.map((k) => (
              <option key={k.id} value={k.id}>{k.kod} · {k.urunAdi ?? "ürünsüz"}</option>
            ))}
          </select>
        </div>

        <div>
          <label className="etiket">Hareket tipi</label>
          <select className="girdi mt-1 w-40" value={tip} onChange={(e) => setTip(e.target.value)}
            data-test="hareket-tip">
            <option value="">Tüm tipler</option>
            <option value="Cikis">Çıkış (toplama)</option>
            <option value="Giris">Giriş (mal kabul / iade)</option>
            <option value="Sayim">Sayım düzeltmesi</option>
          </select>
        </div>

        <div>
          <label className="etiket">Başlangıç</label>
          <input type="date" className="girdi mt-1 w-40" value={baslangic}
            onChange={(e) => setBaslangic(e.target.value)} />
        </div>

        <div>
          <label className="etiket">Bitiş</label>
          <input type="date" className="girdi mt-1 w-40" value={bitis}
            onChange={(e) => setBitis(e.target.value)} />
        </div>

        {filtreVar && (
          <button className="dugme-ikincil text-xs"
            onClick={() => { setKutuId(""); setTip(""); setBaslangic(""); setBitis(""); }}>
            Filtreleri temizle
          </button>
        )}
      </section>

      <section className="kart overflow-x-auto">
        <table className="tablo">
          <thead>
            <tr><th>Zaman</th><th>Kutu</th><th>Ürün</th><th>Tip</th><th>Miktar</th>
              <th>Önce → Sonra</th><th>Personel</th><th>Açıklama</th></tr>
          </thead>
          <tbody>
            {yukleniyor && <><IskeletSatir sutun={8} /><IskeletSatir sutun={8} /><IskeletSatir sutun={8} /></>}

            {!yukleniyor && hareketler.map((h) => (
              <tr key={h.id}>
                <td className="whitespace-nowrap text-xs text-slate-400">
                  {new Date(h.zaman).toLocaleString("tr-TR")}
                </td>
                <td className="font-mono text-xs">{h.kutuKod}</td>
                <td>{h.urunAdi ?? "—"}</td>
                <td className={`font-bold ${TIP_SINIF[h.tip]}`}>{h.tip}</td>
                <td>{h.miktar}</td>
                <td className="font-mono text-xs">{h.oncekiAdet} → {h.sonrakiAdet}</td>
                <td className="text-xs text-slate-400">{h.personelAdi ?? "sistem"}</td>
                <td className="text-xs text-slate-400">{h.aciklama ?? "—"}</td>
              </tr>
            ))}
            {hareketler.length === 0 && (
              <tr><td colSpan={8} className="text-slate-500">
                {filtreVar ? "Bu filtrelerle hareket bulunamadı." : "Hareket yok."}
              </td></tr>
            )}
          </tbody>
        </table>
      </section>

      <section className="flex flex-wrap items-center justify-between gap-3 text-sm text-slate-400">
        <span data-test="hareket-sayaci">
          {toplam === 0
            ? "0 kayıt"
            : `${(sayfa - 1) * BOYUT + 1}–${Math.min(sayfa * BOYUT, toplam)} / ${toplam} hareket`}
        </span>

        {sonSayfa > 1 && (
          <div className="flex items-center gap-2">
            <button className="dugme-ikincil px-3 py-1 text-xs disabled:opacity-40"
              disabled={sayfa <= 1} onClick={() => setSayfa((n) => n - 1)}>‹ Önceki</button>
            <span className="tabular-nums">sayfa {sayfa} / {sonSayfa}</span>
            <button className="dugme-ikincil px-3 py-1 text-xs disabled:opacity-40"
              disabled={sayfa >= sonSayfa} onClick={() => setSayfa((n) => n + 1)}>Sonraki ›</button>
          </div>
        )}
      </section>
    </div>
  );
}
