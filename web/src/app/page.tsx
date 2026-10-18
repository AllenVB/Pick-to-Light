"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import { useCanliOlaylar } from "@/hooks/useCanliOlaylar";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import { KutuKarti } from "@/components/KutuKarti";
import { SiparisRozeti } from "@/components/Rozet";
import type { Alarm, Kutu, SiparisOzet } from "@/types";

export default function PanoSayfasi() {
  const oturum = useKorumaliSayfa();
  const [kutular, setKutular] = useState<Kutu[]>([]);
  const [siparisler, setSiparisler] = useState<SiparisOzet[]>([]);
  const [alarmlar, setAlarmlar] = useState<Alarm[]>([]);
  const [hata, setHata] = useState<string | null>(null);

  const yenile = useCallback(async () => {
    try {
      const [k, s, a] = await Promise.all([api.kutular(), api.siparisler(), api.alarmlar(false)]);
      setKutular(k);
      setSiparisler(s.slice(0, 6));
      setAlarmlar(a.slice(0, 6));
      setHata(null);
    } catch {
      setHata("Backend'e ulaşılamıyor. API çalışıyor mu? (varsayılan http://localhost:5080)");
    }
  }, []);

  useEffect(() => { if (oturum) yenile(); }, [oturum, yenile]);

  // Canlı akış: stok, ışık, alarm ve cihaz olayları panoyu anında tazeler (FR-10).
  useCanliOlaylar({
    StokGuncellendi: yenile,
    KutuIsikDegisti: yenile,
    AlarmOlustu: yenile,
    CihazDurumDegisti: yenile,
    SiparisDurumDegisti: yenile,
  });

  if (!oturum) return null;

  const cevrimici = kutular.filter((k) => k.baglantiDurumu === "Online").length;
  const yanan = kutular.filter((k) => k.isikYaniyor).length;
  const kritik = kutular.filter((k) => k.anlikRenk !== "Yesil" && k.baglantiDurumu === "Online").length;

  return (
    <div className="space-y-6">
      <header className="flex items-end justify-between">
        <div>
          <p className="etiket">Canlı Pano</p>
          <h1 className="baslik mt-1">Depo durumu</h1>
        </div>
        <Link href="/siparis/yeni" className="dugme">Yeni sipariş</Link>
      </header>

      {hata && (
        <p className="rounded-lg border border-durum-kirmizi/40 bg-durum-kirmizi/10 px-4 py-3 text-sm text-durum-kirmizi">
          {hata}
        </p>
      )}

      <section className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <Kutucuk baslik="Kutu" deger={kutular.length} alt="tanımlı" />
        <Kutucuk baslik="Çevrimiçi" deger={cevrimici} alt={`${kutular.length - cevrimici} çevrimdışı`} />
        <Kutucuk baslik="Yanan ışık" deger={yanan} alt="aktif toplama" vurgu />
        <Kutucuk baslik="Kritik stok" deger={kritik} alt="sarı + kırmızı" tehlike={kritik > 0} />
      </section>

      <section>
        <h2 className="etiket mb-3">Kutular</h2>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          {kutular.map((k) => <KutuKarti key={k.id} kutu={k} />)}
          {kutular.length === 0 && !hata && (
            <p className="text-sm text-slate-400">Henüz kutu tanımlanmamış.</p>
          )}
        </div>
      </section>

      <div className="grid gap-4 lg:grid-cols-2">
        <section className="kart">
          <h2 className="etiket mb-3">Son siparişler</h2>
          <table className="tablo">
            <thead><tr><th>Sipariş</th><th>Durum</th><th>İlerleme</th></tr></thead>
            <tbody>
              {siparisler.map((s) => (
                <tr key={s.id}>
                  <td>
                    <Link href={`/siparis/${encodeURIComponent(s.siparisNo)}`}
                      className="font-semibold text-marka-acik hover:underline">
                      {s.siparisNo}
                    </Link>
                  </td>
                  <td><SiparisRozeti durum={s.durum} /></td>
                  <td className="text-slate-300">{s.alinanKalemSayisi}/{s.kalemSayisi} kalem</td>
                </tr>
              ))}
              {siparisler.length === 0 && (
                <tr><td colSpan={3} className="text-slate-500">Sipariş yok.</td></tr>
              )}
            </tbody>
          </table>
        </section>

        <section className="kart">
          <h2 className="etiket mb-3">Okunmamış alarmlar</h2>
          <ul className="space-y-2">
            {alarmlar.map((a) => (
              <li key={a.id} className="rounded-lg border border-durum-kirmizi/30 bg-durum-kirmizi/10 px-3 py-2 text-sm">
                <span className="font-bold text-durum-kirmizi">{a.tip}</span>{" "}
                <span className="text-slate-200">{a.mesaj}</span>
              </li>
            ))}
            {alarmlar.length === 0 && <li className="text-sm text-slate-500">Alarm yok. ✔</li>}
          </ul>
        </section>
      </div>
    </div>
  );
}

function Kutucuk({ baslik, deger, alt, vurgu, tehlike }: {
  baslik: string; deger: number; alt: string; vurgu?: boolean; tehlike?: boolean;
}) {
  return (
    <div className="kart" data-test="pano-kpi">
      <p className="etiket">{baslik}</p>
      <p className={`mt-1 text-3xl font-extrabold ${
        tehlike ? "text-durum-kirmizi" : vurgu ? "text-marka-acik" : ""}`}>{deger}</p>
      <p className="text-[11px] text-slate-400">{alt}</p>
    </div>
  );
}
