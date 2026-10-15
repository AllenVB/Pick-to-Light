"use client";

import { useCallback, useEffect, useState } from "react";
import { api } from "@/lib/api";
import { useCanliOlaylar } from "@/hooks/useCanliOlaylar";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import type { StokHareket } from "@/types";

const TIP_SINIF: Record<string, string> = {
  Cikis: "text-durum-kirmizi",
  Giris: "text-durum-yesil",
  Sayim: "text-durum-sari",
};

export default function HareketlerSayfasi() {
  const oturum = useKorumaliSayfa();
  const [hareketler, setHareketler] = useState<StokHareket[]>([]);

  const yenile = useCallback(() => {
    api.hareketler().then(setHareketler).catch(() => setHareketler([]));
  }, []);

  useEffect(() => { if (oturum) yenile(); }, [oturum, yenile]);
  useCanliOlaylar({ StokGuncellendi: yenile });

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

      <section className="kart overflow-x-auto">
        <table className="tablo">
          <thead>
            <tr><th>Zaman</th><th>Kutu</th><th>Ürün</th><th>Tip</th><th>Miktar</th>
              <th>Önce → Sonra</th><th>Personel</th><th>Açıklama</th></tr>
          </thead>
          <tbody>
            {hareketler.map((h) => (
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
            {hareketler.length === 0 && <tr><td colSpan={8} className="text-slate-500">Hareket yok.</td></tr>}
          </tbody>
        </table>
      </section>
    </div>
  );
}
