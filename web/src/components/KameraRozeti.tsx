"use client";

import { useEffect, useState } from "react";
import { vision, type BaskinUrun } from "@/lib/vision";

/**
 * Operatör ekranında yanan kutu için kameranın gördüğü ürün.
 *
 * Beklenen ürünle aynıysa yeşil onay, farklıysa kırmızı uyarı gösterir. Görüntü servisi
 * kapalıysa hiçbir şey çizmez: operatör ekranı kamera olmadan da aynı çalışır.
 */
export function KameraRozeti({ beklenenUrunId, beklenenUrunAdi }: { beklenenUrunId?: number; beklenenUrunAdi?: string }) {
  const [baskin, setBaskin] = useState<BaskinUrun | null>(null);
  const [servisVar, setServisVar] = useState(false);

  useEffect(() => {
    let iptal = false;
    const sorgula = async () => {
      const yanit = await vision.tani().catch(() => null);
      if (iptal) return;
      setServisVar(yanit !== null);
      setBaskin(yanit?.baskin ?? null);
    };
    sorgula();
    const t = setInterval(sorgula, 2000);
    return () => { iptal = true; clearInterval(t); };
  }, []);

  if (!servisVar) return null;

  if (!baskin) {
    return (
      <p className="mt-4 inline-flex items-center gap-2 rounded-full bg-slate-600/20 px-3 py-1 text-sm font-semibold text-slate-400"
        data-test="kamera-rozeti">
        <span aria-hidden>◉</span> Kamera: ürün tanınmadı
      </p>
    );
  }

  const dogru = beklenenUrunId === undefined || baskin.urunId === beklenenUrunId;
  return (
    <p className={`mt-4 inline-flex items-center gap-2 rounded-full px-3 py-1 text-sm font-bold ${
      dogru ? "bg-durum-yesil/15 text-durum-yesil" : "bg-durum-kirmizi/15 text-durum-kirmizi nabiz-kirmizi"}`}
      data-test="kamera-rozeti">
      <span aria-hidden>◉</span>
      {dogru
        ? `Kamera: ${baskin.urunAdi} ✓ (%${Math.round(baskin.guven * 100)})`
        : `Kamera ${baskin.urunAdi} görüyor — beklenen ${beklenenUrunAdi ?? "başka ürün"}`}
    </p>
  );
}
