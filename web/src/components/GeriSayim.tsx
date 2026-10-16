"use client";

import { useEffect, useState } from "react";

/**
 * Işığı yanan kalem için kalan teyit süresi. Süre dolduğunda sunucu ışığı söndürür
 * ve rezerve stoğu iade eder (Bölüm 7.3) — operatör bunu önceden görsün.
 */
export function GeriSayim({ sonTarih }: { sonTarih?: string }) {
  const [kalanSn, setKalanSn] = useState<number | null>(null);

  useEffect(() => {
    if (!sonTarih) { setKalanSn(null); return; }

    const hesapla = () =>
      setKalanSn(Math.max(0, Math.round((new Date(sonTarih).getTime() - Date.now()) / 1000)));

    hesapla();
    const zamanlayici = setInterval(hesapla, 1000);
    return () => clearInterval(zamanlayici);
  }, [sonTarih]);

  if (kalanSn === null) return null;

  const dakika = Math.floor(kalanSn / 60);
  const saniye = kalanSn % 60;
  const metin = `${dakika}:${String(saniye).padStart(2, "0")}`;

  const sinif =
    kalanSn === 0 ? "text-durum-kirmizi"
    : kalanSn < 60 ? "text-durum-sari nabiz-sari"
    : "text-slate-400";

  return (
    <span className={`font-mono text-[11px] ${sinif}`}
      title="Bu süre dolunca ışık söner ve rezerve stok iade edilir">
      {kalanSn === 0 ? "süre doldu" : `${metin} kaldı`}
    </span>
  );
}
