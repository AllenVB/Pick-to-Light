"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import { useCanliOlaylar } from "@/hooks/useCanliOlaylar";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import { OncelikRozeti, SiparisRozeti } from "@/components/Rozet";
import { IskeletSatir } from "@/components/Durumlar";
import type { SiparisOzet } from "@/types";

export default function SiparislerSayfasi() {
  const oturum = useKorumaliSayfa();
  const [siparisler, setSiparisler] = useState<SiparisOzet[]>([]);
  const [toplam, setToplam] = useState(0);
  const [yukleniyor, setYukleniyor] = useState(true);
  const [filtre, setFiltre] = useState<string>("");
  const [arama, setArama] = useState("");
  const [sayfa, setSayfa] = useState(1);

  const BOYUT = 25;

  const yenile = useCallback(() => {
    api.siparisler({ durum: filtre || undefined, ara: arama.trim() || undefined, sayfa, boyut: BOYUT })
      .then((s) => { setSiparisler(s.liste); setToplam(s.toplam); })
      .finally(() => setYukleniyor(false))
      .catch(() => { setSiparisler([]); setToplam(0); });
  }, [filtre, arama, sayfa]);

  // Filtre veya arama değişince ilk sayfaya dön (boş sayfada kalmamak için)
  useEffect(() => { setSayfa(1); }, [filtre, arama]);

  useEffect(() => { if (oturum) yenile(); }, [oturum, yenile]);
  useCanliOlaylar({ SiparisDurumDegisti: yenile }, [filtre, arama, sayfa]);

  const sonSayfa = Math.max(1, Math.ceil(toplam / BOYUT));

  if (!oturum) return null;

  return (
    <div className="space-y-6">
      <header className="flex items-end justify-between">
        <div>
          <p className="etiket">Siparişler</p>
          <h1 className="baslik mt-1">Sipariş listesi</h1>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <input
            className="girdi w-64"
            placeholder="Sipariş no ara…"
            value={arama}
            onChange={(e) => setArama(e.target.value)}
            data-test="siparis-arama" />
          <select className="girdi w-48" value={filtre} onChange={(e) => setFiltre(e.target.value)}>
            <option value="">Tüm durumlar</option>
            <option value="Bekliyor">Bekliyor</option>
            <option value="Hazirlaniyor">Hazırlanıyor</option>
            <option value="Kilitli">Kilitli</option>
            <option value="KismiTamamlandi">Kısmi tamamlandı</option>
            <option value="Tamamlandi">Tamamlandı</option>
              <option value="Iptal">İptal</option>
          </select>
        </div>
      </header>

      <section className="kart overflow-x-auto">
        <table className="tablo">
          <thead>
            <tr><th>Sipariş no</th><th>Durum</th><th>İlerleme</th><th>Oluşturma</th><th>Tamamlanma</th></tr>
          </thead>
          <tbody>
            {yukleniyor && <><IskeletSatir sutun={5} /><IskeletSatir sutun={5} /><IskeletSatir sutun={5} /></>}

            {!yukleniyor && siparisler.map((s) => (
              <tr key={s.id}>
                <td>
                  <Link href={`/siparis/${encodeURIComponent(s.siparisNo)}`}
                    className="font-semibold text-marka-acik hover:underline">{s.siparisNo}</Link>
                </td>
                <td className="space-x-1 whitespace-nowrap">
                  <SiparisRozeti durum={s.durum} />
                  <OncelikRozeti oncelik={s.oncelik} />
                </td>
                <td>{s.alinanKalemSayisi}/{s.kalemSayisi}</td>
                <td className="text-xs text-slate-400">{new Date(s.olusturma).toLocaleString("tr-TR")}</td>
                <td className="text-xs text-slate-400">
                  {s.tamamlanma ? new Date(s.tamamlanma).toLocaleString("tr-TR") : "—"}
                </td>
              </tr>
            ))}
            {siparisler.length === 0 && (
              <tr><td colSpan={5} className="text-slate-500">
                {arama ? `"${arama}" ile eşleşen sipariş yok.` : "Kayıt yok."}
              </td></tr>
            )}
          </tbody>
        </table>
      </section>

      {/* Sayfalama — liste sessizce kesilmesin */}
      <section className="flex flex-wrap items-center justify-between gap-3 text-sm text-slate-400">
        <span data-test="siparis-sayaci">
          {toplam === 0
            ? "0 kayıt"
            : `${(sayfa - 1) * BOYUT + 1}–${Math.min(sayfa * BOYUT, toplam)} / ${toplam} sipariş`}
        </span>

        {sonSayfa > 1 && (
          <div className="flex items-center gap-2">
            <button className="dugme-ikincil px-3 py-1 text-xs disabled:opacity-40"
              disabled={sayfa <= 1} onClick={() => setSayfa((n) => n - 1)}>
              ‹ Önceki
            </button>
            <span className="tabular-nums">sayfa {sayfa} / {sonSayfa}</span>
            <button className="dugme-ikincil px-3 py-1 text-xs disabled:opacity-40"
              disabled={sayfa >= sonSayfa} onClick={() => setSayfa((n) => n + 1)}>
              Sonraki ›
            </button>
          </div>
        )}
      </section>
    </div>
  );
}
