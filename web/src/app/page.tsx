"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import { useCanliOlaylar } from "@/hooks/useCanliOlaylar";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import { KutuKarti } from "@/components/KutuKarti";
import { canliKutular, useCanliAgirlik } from "@/components/Baskul";
import { OncelikRozeti, SiparisRozeti } from "@/components/Rozet";
import { BosDurum, IskeletKart } from "@/components/Durumlar";
import type { Alarm, Kutu, SiparisOzet } from "@/types";

export default function PanoSayfasi() {
  const oturum = useKorumaliSayfa();
  const [hamKutular, setKutular] = useState<Kutu[]>([]);
  const kutular = canliKutular(hamKutular, useCanliAgirlik());
  const [siparisler, setSiparisler] = useState<SiparisOzet[]>([]);
  const [alarmlar, setAlarmlar] = useState<Alarm[]>([]);
  const [hata, setHata] = useState<string | null>(null);
  const [yukleniyor, setYukleniyor] = useState(true);

  const yenile = useCallback(async () => {
    try {
      const [k, s, a] = await Promise.all([api.kutular(), api.siparisler({ boyut: 5 }), api.alarmlar(false)]);
      setKutular(k);
      setSiparisler(s.liste.slice(0, 6));
      setAlarmlar(a.slice(0, 6));
      setHata(null);
    } catch {
      setHata("Backend'e ulaşılamıyor. API çalışıyor mu? (varsayılan http://localhost:5080)");
    } finally {
      setYukleniyor(false);
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

  // Cihaz sağlığı: çevrimdışı olanlar + sinyali zayıf olanlar (ESP32'ler sahaya
  // kurulduğunda arıza panodan görülsün).
  const cevrimdisi = kutular.length - cevrimici;
  const zayifSinyal = kutular.filter((k) => k.baglantiDurumu === "Online" && (k.rssi ?? 0) < -78).length;
  const cihazSorunu = cevrimdisi + zayifSinyal;

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
        <Kutucuk baslik="Yanan ışık" deger={yanan} alt="aktif toplama" vurgu bag="/operator" />
        <Kutucuk baslik="Kritik stok" deger={kritik} alt="sarı + kırmızı kutu"
          tehlike={kritik > 0} bag="/stok" />
        <Kutucuk baslik="Cihaz sorunu" deger={cihazSorunu}
          alt={cihazSorunu === 0 ? "tüm kutular sağlıklı" : `${cevrimdisi} çevrimdışı · ${zayifSinyal} zayıf sinyal`}
          tehlike={cihazSorunu > 0} bag="/kutular" />
        <Kutucuk baslik="Okunmamış alarm" deger={alarmlar.length} alt="son 6 gösteriliyor"
          tehlike={alarmlar.length > 0} bag="/alarmlar" />
      </section>

      <section>
        <div className="mb-3 flex items-end justify-between">
          <h2 className="etiket">Kutular</h2>
          <p className="ipucu">{kutular.length} kutu · {cevrimici} çevrimiçi</p>
        </div>

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          {yukleniyor && <IskeletKart adet={4} />}
          {!yukleniyor && kutular.map((k) => <KutuKarti key={k.id} kutu={k} />)}
        </div>

        {!yukleniyor && kutular.length === 0 && !hata && (
          <div className="kart">
            <BosDurum simge="▣" baslik="Henüz kutu tanımlanmamış"
              aciklama="Her kutu bir ESP32 kontrolcüsüne karşılık gelir."
              eylem={<Link href="/kutular" className="dugme">Kutu ekle</Link>} />
          </div>
        )}
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
                      className="font-semibold text-marka-metin hover:underline">
                      {s.siparisNo}
                    </Link>
                  </td>
                  <td className="space-x-1 whitespace-nowrap">
                    <SiparisRozeti durum={s.durum} />
                    <OncelikRozeti oncelik={s.oncelik} />
                  </td>
                  <td className="text-slate-300">{s.alinanKalemSayisi}/{s.kalemSayisi} kalem</td>
                </tr>
              ))}
              {!yukleniyor && siparisler.length === 0 && (
                <tr><td colSpan={3}>
                  <BosDurum simge="☰" baslik="Henüz sipariş yok"
                    eylem={<Link href="/siparis/yeni" className="dugme dugme-kucuk">Sipariş gir</Link>} />
                </td></tr>
              )}
            </tbody>
          </table>
        </section>

        <section className="kart">
          <h2 className="etiket mb-3">Okunmamış alarmlar</h2>
          <ul className="space-y-2">
            {alarmlar.map((a) => {
              // Stok tükenmesi ve cihaz kopması acil; azalma ve zaman aşımı uyarı düzeyinde.
              const acil = a.tip === "StokYok" || a.tip === "CihazOffline";
              return (
                <li key={a.id}
                  className={`rounded-lg border px-3 py-2 text-sm ${acil
                    ? "border-durum-kirmizi/30 bg-durum-kirmizi/10"
                    : "border-durum-sari/30 bg-durum-sari/10"}`}>
                  <span className={`font-bold ${acil ? "text-durum-kirmizi" : "text-durum-sari"}`}>
                    {a.tip}
                  </span>{" "}
                  <span className="text-slate-200">{a.mesaj}</span>
                </li>
              );
            })}
            {!yukleniyor && alarmlar.length === 0 && (
              <li><BosDurum simge="✓" baslik="Okunmamış alarm yok"
                aciklama="Stok tükenmesi, cihaz kopması ve teyit zaman aşımları burada görünür." /></li>
            )}
          </ul>
        </section>
      </div>
    </div>
  );
}

/** Panodaki sayaç. İlgili ekrana bağlanır: sayıyı gören kişi oraya gitmek ister. */
function Kutucuk({ baslik, deger, alt, vurgu, tehlike, bag }: {
  baslik: string; deger: number; alt: string; vurgu?: boolean; tehlike?: boolean; bag?: string;
}) {
  const icerik = (
    <>
      <p className="etiket">{baslik}</p>
      <p className={`mt-1 text-3xl font-extrabold tabular-nums ${
        tehlike ? "text-durum-kirmizi" : vurgu ? "text-marka-metin" : ""}`}>{deger}</p>
      <p className="ipucu mt-0.5">{alt}</p>
    </>
  );

  if (!bag) return <div className="kart" data-test="pano-kpi">{icerik}</div>;

  return (
    <Link href={bag} className="kart kart-tiklanabilir block" data-test="pano-kpi">
      {icerik}
    </Link>
  );
}
