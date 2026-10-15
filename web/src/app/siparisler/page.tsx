"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import { useCanliOlaylar } from "@/hooks/useCanliOlaylar";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import { SiparisRozeti } from "@/components/Rozet";
import type { SiparisOzet } from "@/types";

export default function SiparislerSayfasi() {
  const oturum = useKorumaliSayfa();
  const [siparisler, setSiparisler] = useState<SiparisOzet[]>([]);
  const [filtre, setFiltre] = useState<string>("");

  const yenile = useCallback(() => {
    api.siparisler(filtre || undefined).then(setSiparisler).catch(() => setSiparisler([]));
  }, [filtre]);

  useEffect(() => { if (oturum) yenile(); }, [oturum, yenile]);
  useCanliOlaylar({ SiparisDurumDegisti: yenile }, [filtre]);

  if (!oturum) return null;

  return (
    <div className="space-y-6">
      <header className="flex items-end justify-between">
        <div>
          <p className="etiket">Siparişler</p>
          <h1 className="baslik mt-1">Sipariş listesi</h1>
        </div>
        <select className="girdi w-48" value={filtre} onChange={(e) => setFiltre(e.target.value)}>
          <option value="">Tüm durumlar</option>
          <option value="Bekliyor">Bekliyor</option>
          <option value="Hazirlaniyor">Hazırlanıyor</option>
          <option value="Kilitli">Kilitli</option>
          <option value="KismiTamamlandi">Kısmi tamamlandı</option>
          <option value="Tamamlandi">Tamamlandı</option>
          <option value="Iptal">İptal</option>
        </select>
      </header>

      <section className="kart overflow-x-auto">
        <table className="tablo">
          <thead>
            <tr><th>Sipariş no</th><th>Durum</th><th>İlerleme</th><th>Oluşturma</th><th>Tamamlanma</th></tr>
          </thead>
          <tbody>
            {siparisler.map((s) => (
              <tr key={s.id}>
                <td>
                  <Link href={`/siparis/${encodeURIComponent(s.siparisNo)}`}
                    className="font-semibold text-marka-acik hover:underline">{s.siparisNo}</Link>
                </td>
                <td><SiparisRozeti durum={s.durum} /></td>
                <td>{s.alinanKalemSayisi}/{s.kalemSayisi}</td>
                <td className="text-xs text-slate-400">{new Date(s.olusturma).toLocaleString("tr-TR")}</td>
                <td className="text-xs text-slate-400">
                  {s.tamamlanma ? new Date(s.tamamlanma).toLocaleString("tr-TR") : "—"}
                </td>
              </tr>
            ))}
            {siparisler.length === 0 && (
              <tr><td colSpan={5} className="text-slate-500">Kayıt yok.</td></tr>
            )}
          </tbody>
        </table>
      </section>
    </div>
  );
}
