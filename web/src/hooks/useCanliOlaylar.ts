"use client";

import { useEffect, useRef } from "react";
import { hubBaslat, hubBaglantisi } from "@/lib/signalr";

type Isleyici = (veri: unknown) => void;

/**
 * SignalR olaylarına abone olur. Bileşen kaldırıldığında abonelikler temizlenir.
 * Örnek: useCanliOlaylar({ StokGuncellendi: () => yenile() })
 */
export function useCanliOlaylar(isleyiciler: Record<string, Isleyici>, bagimliliklar: unknown[] = []) {
  const ref = useRef(isleyiciler);
  ref.current = isleyiciler;

  useEffect(() => {
    let iptal = false;
    const adlar = Object.keys(ref.current);
    const sarmalayicilar: Record<string, Isleyici> = {};

    hubBaslat()
      .then((c) => {
        if (iptal) return;
        for (const ad of adlar) {
          sarmalayicilar[ad] = (veri: unknown) => ref.current[ad]?.(veri);
          c.on(ad, sarmalayicilar[ad]);
        }
      })
      .catch(() => {
        /* backend kapalıysa sayfa yine de çalışır, sadece canlı güncelleme olmaz */
      });

    return () => {
      iptal = true;
      const c = hubBaglantisi();
      for (const ad of adlar) if (sarmalayicilar[ad]) c.off(ad, sarmalayicilar[ad]);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, bagimliliklar);
}
