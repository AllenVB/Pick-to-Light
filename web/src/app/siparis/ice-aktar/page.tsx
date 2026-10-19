"use client";

import { useMemo, useRef, useState } from "react";
import Link from "next/link";
import { ApiHata, api } from "@/lib/api";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import type { IceAktarSonucu, SiparisOnceligi } from "@/types";

const ORNEK = `siparisNo;sku;miktar
SIP-2026-001;KBL-2M;3
SIP-2026-001;ESP-32;1
SIP-2026-002;LED-RGB;10`;

export default function IceAktarSayfasi() {
  const oturum = useKorumaliSayfa();
  const [csv, setCsv] = useState("");
  const [hemenBaslat, setHemenBaslat] = useState(false);
  const [oncelik, setOncelik] = useState<SiparisOnceligi>("Normal");
  const [sonuc, setSonuc] = useState<IceAktarSonucu | null>(null);
  const [hata, setHata] = useState<string | null>(null);
  const [calisiyor, setCalisiyor] = useState(false);
  const dosyaRef = useRef<HTMLInputElement>(null);

  // Gönderilmeden önce kaç sipariş/satır olduğu gösterilir: yanlış dosyayı fark etmek kolay olsun.
  const onizleme = useMemo(() => {
    const satirlar = csv.split(/\r?\n/)
      .map((s) => s.trim())
      .filter((s) => s.length > 0 && !s.startsWith("#"));

    const numaralar = new Set<string>();
    let gecerli = 0;

    for (const [i, satir] of satirlar.entries()) {
      const p = satir.split(/[;,\t]/).map((x) => x.trim());
      if (p.length < 3) continue;
      if (i === 0 && !Number.isInteger(Number(p[2]))) continue;   // başlık
      if (!Number.isInteger(Number(p[2])) || Number(p[2]) <= 0) continue;
      if (!p[0] || !p[1]) continue;
      numaralar.add(p[0]);
      gecerli++;
    }

    return { siparis: numaralar.size, satir: gecerli };
  }, [csv]);

  async function dosyaSec(dosya: File) {
    setCsv(await dosya.text());
    setSonuc(null);
    setHata(null);
  }

  async function aktar() {
    setCalisiyor(true);
    setHata(null);
    setSonuc(null);
    try {
      setSonuc(await api.siparisIceAktar(csv, hemenBaslat, oncelik));
    } catch (h) {
      setHata(h instanceof ApiHata ? h.message : "Aktarım başarısız.");
    } finally {
      setCalisiyor(false);
    }
  }

  if (!oturum) return null;

  return (
    <div className="space-y-6">
      <header>
        <p className="etiket">Sipariş</p>
        <h1 className="baslik mt-1">Toplu sipariş aktarma</h1>
        <p className="mt-1 text-sm text-slate-400">
          ERP veya Excel çıktısını CSV olarak aktarın. Biçim:{" "}
          <code className="rounded bg-zemin-cizgi/60 px-1 text-xs">siparisNo;sku;miktar</code> — noktalı virgül,
          virgül veya sekme ayracı kabul edilir. Hatalı sipariş atlanır, kalanlar oluşturulur.
        </p>
      </header>

      <section className="kart space-y-4">
        <div className="flex flex-wrap items-center gap-3">
          <input ref={dosyaRef} type="file" accept=".csv,.txt,text/csv" className="hidden"
            onChange={(e) => e.target.files?.[0] && dosyaSec(e.target.files[0])} />
          <button className="dugme-ikincil" onClick={() => dosyaRef.current?.click()}>
            Dosya seç (.csv)
          </button>
          <button className="dugme-ikincil text-xs" onClick={() => { setCsv(ORNEK); setSonuc(null); }}>
            Örnek doldur
          </button>
          {csv && (
            <span className="text-sm text-slate-400" data-test="ice-aktar-onizleme">
              {onizleme.siparis} sipariş · {onizleme.satir} geçerli satır
            </span>
          )}
        </div>

        <textarea
          className="girdi h-56 w-full font-mono text-xs"
          data-test="ice-aktar-metin"
          placeholder={ORNEK}
          value={csv}
          onChange={(e) => { setCsv(e.target.value); setSonuc(null); }} />

        <div className="flex flex-wrap items-center gap-4">
          <label className="flex items-center gap-2 text-sm text-slate-300">
            <input type="checkbox" checked={hemenBaslat} onChange={(e) => setHemenBaslat(e.target.checked)} />
            Aktarır aktarmaz başlat (ışıkları yak)
          </label>

          <select className="girdi w-56" value={oncelik}
            onChange={(e) => setOncelik(e.target.value as SiparisOnceligi)}>
            <option value="Normal">Normal öncelik</option>
            <option value="Acil">Acil — hepsi öne geçsin</option>
          </select>

          <button className="dugme" disabled={calisiyor || onizleme.satir === 0}
            data-test="ice-aktar-dugme" onClick={aktar}>
            {calisiyor ? "Aktarılıyor…" : `${onizleme.siparis || ""} siparişi aktar`}
          </button>
        </div>

        {hata && (
          <p className="rounded-lg border border-durum-kirmizi/40 bg-durum-kirmizi/10 px-3 py-2 text-sm text-durum-kirmizi">
            {hata}
          </p>
        )}
      </section>

      {sonuc && (
        <section className="kart space-y-3" data-test="ice-aktar-sonuc">
          <div className="flex flex-wrap gap-6">
            <div>
              <p className="etiket">Oluşturulan</p>
              <p className="text-2xl font-black text-durum-yesil">{sonuc.olusturulan}</p>
            </div>
            <div>
              <p className="etiket">Hatalı</p>
              <p className={`text-2xl font-black ${sonuc.basarisiz ? "text-durum-kirmizi" : "text-slate-500"}`}>
                {sonuc.basarisiz}
              </p>
            </div>
          </div>

          <table className="tablo">
            <thead><tr><th>Sipariş no</th><th>Satırlar</th><th>Sonuç</th></tr></thead>
            <tbody>
              {sonuc.sonuclar.map((s) => (
                <tr key={s.siparisNo}>
                  <td className="font-semibold">
                    {s.basarili
                      ? <Link href={`/siparis/${encodeURIComponent(s.siparisNo)}`}
                          className="text-marka-acik hover:underline">{s.siparisNo}</Link>
                      : s.siparisNo}
                  </td>
                  <td className="text-xs text-slate-400">{s.satirNumaralari.join(", ")}</td>
                  <td className={s.basarili ? "text-durum-yesil" : "text-durum-kirmizi"}>
                    {s.basarili ? `✔ ${s.kalemSayisi} kalem oluşturuldu` : `✘ ${s.hata}`}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}
    </div>
  );
}
