"use client";

import { useState } from "react";
import { useCanliOlaylar } from "@/hooks/useCanliOlaylar";
import type { Kutu } from "@/types";

/**
 * Baskül (kutunun altındaki yük hücresi) gösterimleri.
 *
 * Baskül kutu içeriğinin gramını ölçer; adet = gram ÷ ürünün birim ağırlığı. Ölçümler
 * SignalR ile saniyede bir gelir, sayfalar kutu listesini yeniden çekmeden canlı güncellenir.
 */

export interface CanliOlcum {
  agirlikG: number;
  kararli: boolean;
  zaman: string;
}

/** `AgirlikGuncellendi` olaylarını kutu kimliğine göre biriktirir. */
export function useCanliAgirlik(): Record<number, CanliOlcum> {
  const [olcumler, setOlcumler] = useState<Record<number, CanliOlcum>>({});

  useCanliOlaylar({
    AgirlikGuncellendi: (veri) => {
      const o = veri as { kutuId: number; agirlikG: number; kararli: boolean; zaman: string };
      setOlcumler((onceki) => ({
        ...onceki,
        [o.kutuId]: { agirlikG: o.agirlikG, kararli: o.kararli, zaman: o.zaman },
      }));
    },
  });

  return olcumler;
}

/** Sunucudan gelen kutu listesine canlı ölçümleri uygular (ağırlık, adet ve kararlılık). */
export function canliKutular(kutular: Kutu[], olcumler: Record<number, CanliOlcum>): Kutu[] {
  return kutular.map((k) => {
    const o = olcumler[k.id];
    if (!o) return k;
    const birim = k.birimAgirlikG && k.birimAgirlikG > 0 ? k.birimAgirlikG : null;
    return {
      ...k,
      agirlikG: o.agirlikG,
      agirlikZamani: o.zaman,
      agirlikKararli: o.kararli,
      tahminiAdet: birim ? Math.round(o.agirlikG / birim) : k.tahminiAdet,
    };
  });
}

export function gramMetni(g: number): string {
  return `${g.toLocaleString("tr-TR", { maximumFractionDigits: 1 })} g`;
}

/** Kutuda baskül verisi var mı? */
export const baskulVar = (k: Kutu) => k.agirlikG != null;

/** Baskül ölçümü 60 sn'den eskiyse cihaz sessizdir; değer güncel gibi gösterilmez. */
export function olcumEskiMi(k: Kutu): boolean {
  if (!k.agirlikZamani) return true;
  return Date.now() - new Date(k.agirlikZamani).getTime() > 60_000;
}

/**
 * Baskülün saydığı adet ile sistemdeki (rezerve dahil) adet arasındaki fark.
 * Ölçüm oturmadıysa (ürün alınıyor) fark hesaplanmaz.
 */
export function sayimSapmasi(k: Kutu): number | null {
  if (k.tahminiAdet == null || k.beklenenFizikselAdet == null) return null;
  if (k.agirlikKararli === false || olcumEskiMi(k)) return null;
  const fark = k.tahminiAdet - k.beklenenFizikselAdet;
  return fark === 0 ? null : fark;
}

/** Kutu kartı ve tablolar için tek satırlık baskül özeti: ağırlık · ≈ adet · sapma. */
export function BaskulSatiri({ kutu }: { kutu: Kutu }) {
  if (!baskulVar(kutu)) return null;

  const eski = olcumEskiMi(kutu);
  const sapma = sayimSapmasi(kutu);
  const oturuyor = kutu.agirlikKararli !== false;

  return (
    <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-[11px]" data-test="baskul-satiri">
      <span aria-hidden className="shrink-0">⚖</span>
      <span className={`whitespace-nowrap font-mono font-semibold tabular-nums ${eski ? "text-durum-gri" : ""}`}>
        {gramMetni(kutu.agirlikG!)}
      </span>
      {kutu.tahminiAdet != null && (
        <span className="whitespace-nowrap text-slate-400">≈ {kutu.tahminiAdet} adet</span>
      )}
      {!oturuyor && <span className="text-durum-sari">ölçülüyor…</span>}
      {eski && <span className="text-durum-gri">ölçüm eski</span>}
      {sapma != null && (
        <span className="rozet bg-durum-sari/20 text-durum-sari"
          title="Baskülün saydığı adet, sistemdeki (rezerve dahil) adetle uyuşmuyor">
          sayım farkı {sapma > 0 ? `+${sapma}` : sapma}
        </span>
      )}
    </div>
  );
}

/**
 * Kiosk ekranı: operatör ürün aldıkça düşen gram ve adet. Hedefe ulaşınca yeşil olur,
 * aşılırsa kırmızıya döner (fazla alım) — baskül ağırlık oturunca teyidi kendisi gönderir.
 */
export function AlinanGram({ kutu }: { kutu: Kutu }) {
  const birim = kutu.birimAgirlikG;
  const baslangic = kutu.baslangicAgirligiG;

  if (!baskulVar(kutu) || !birim || birim <= 0 || baslangic == null || !kutu.alinacakMiktar) return null;

  const hedefGram = kutu.alinacakMiktar * birim;
  const alinanGram = Math.max(0, baslangic - kutu.agirlikG!);
  const alinanAdet = alinanGram / birim;
  const oran = Math.min(alinanGram / hedefGram, 1.25);

  const fazla = alinanGram > hedefGram + birim * 0.5;
  const tamam = !fazla && alinanGram >= hedefGram - birim * 0.5;
  const renk = fazla ? "bg-durum-kirmizi" : tamam ? "bg-durum-yesil" : "bg-marka-acik";
  const metin = fazla ? "text-durum-kirmizi" : tamam ? "text-durum-yesil" : "text-slate-300";

  return (
    <div className="mt-5" data-test="alinan-gram">
      <div className="flex items-end justify-between gap-3">
        <p className="text-xs uppercase tracking-widest text-slate-500">baskül · alınan</p>
        <p className={`font-mono text-2xl font-bold tabular-nums ${metin}`}>
          {gramMetni(alinanGram)}
          <span className="ml-2 text-base font-semibold text-slate-400">
            {Math.round(alinanAdet * 10) / 10} / {kutu.alinacakMiktar} adet
          </span>
        </p>
      </div>
      <div className={`mt-2 h-3 overflow-hidden rounded-full bg-slate-50/[0.08] ${fazla ? "nabiz-kirmizi" : ""}`}>
        <div className={`h-full rounded-full transition-all duration-300 ${renk}`}
          style={{ width: `${Math.round((oran / 1.25) * 100)}%` }} />
      </div>
      <p className={`mt-1 text-sm font-semibold ${metin}`}>
        {fazla
          ? `Fazla alım algılandı (~${Math.round(alinanAdet - kutu.alinacakMiktar)} adet) — stok gerçek adede göre düşülür`
          : tamam
            ? "Miktar tamam — ağırlık oturunca otomatik teyit edilir"
            : `Hedef ${gramMetni(hedefGram)} — ürünleri alın`}
      </p>
    </div>
  );
}
