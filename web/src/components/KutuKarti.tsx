"use client";

import { GecenSure, Sinyal } from "@/components/Cihaz";
import type { Kutu, LedRengi } from "@/types";

const CERCEVE: Record<LedRengi, string> = {
  Yesil: "border-durum-yesil/35",
  Sari: "border-durum-sari/60 nabiz-sari",
  Kirmizi: "border-durum-kirmizi/70 nabiz-kirmizi",
};

// Kutudaki ışığı gösteren lamba ve stok çubuğu fiziksel LED rengini kullanır:
// açık temada koyulaştırılırsa sarı kahverengiye döner ve "sarı LED" anlamını yitirir.
const LAMBA: Record<LedRengi, string> = {
  Yesil: "bg-led-yesil",
  Sari: "bg-led-sari",
  Kirmizi: "bg-led-kirmizi",
};

const DOLGU: Record<LedRengi, string> = {
  Yesil: "bg-led-yesil",
  Sari: "bg-led-sari",
  Kirmizi: "bg-led-kirmizi",
};

const METIN: Record<LedRengi, string> = {
  Yesil: "Yeterli",
  Sari: "Azaldı",
  Kirmizi: "Tükendi",
};

/**
 * Panodaki kutu kartı.
 *
 * Bir bakışta dört soru cevaplanmalı: kutuda ne var, ne kadar kaldı, ışığı
 * yanıyor mu, cihazı sağlıklı mı. Cihaz bilgisi (sinyal, son görülme) ESP32'ler
 * sahaya kurulduğunda arızayı panodan fark etmeyi sağlar.
 */
export function KutuKarti({ kutu }: { kutu: Kutu }) {
  const cevrimdisi = kutu.baglantiDurumu === "Offline";
  const renk = kutu.anlikRenk ?? "Yesil";

  // Stok çubuğu: güvenlik stoğunun iki katı "dolu" sayılır (yoksa 20 adet referans).
  const referans = Math.max(kutu.guvenlikStogu * 2, 20);
  const doluluk = Math.min(100, Math.round((kutu.adet / referans) * 100));

  return (
    <div
      data-test="kutu-karti"
      data-kutu={kutu.kod}
      className={`kart relative space-y-3 ${
        // Çevrimdışı kart sönük görünmeli ama okunur kalmalı: kartın tamamını
        // soluklaştırmak yerine yalnızca zemin ve çerçeve kısılır.
        cevrimdisi ? "border-zemin-cizgi bg-zemin-kart/40" : CERCEVE[renk]} ${
        kutu.isikYaniyor ? "ring-2 ring-marka-acik" : ""
      }`}
    >
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="etiket">{kutu.kod}</p>
          <p className="mt-0.5 truncate text-sm font-semibold">
            {kutu.urunAdi ?? <span className="text-slate-500">ürün atanmamış</span>}
          </p>
          <p className="ipucu">{kutu.rafKonumu ?? "konum yok"}</p>
        </div>

        <span
          className={`h-6 w-6 shrink-0 rounded-full ring-2 ring-black/20 ${
            cevrimdisi ? "bg-durum-gri" : LAMBA[renk]}`}
          title={cevrimdisi ? "Bağlantı yok" : METIN[renk]}
        />
      </div>

      {/* Stok: sayı + doluluk çubuğu */}
      <div>
        <div className="flex items-end justify-between">
          <p className="text-3xl font-extrabold leading-none tabular-nums">{kutu.adet}</p>
          <p className="ipucu">güvenlik {kutu.guvenlikStogu}</p>
        </div>
        <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-slate-50/[0.07]">
          <div className={`h-full rounded-full transition-all ${DOLGU[renk]}`}
            style={{ width: `${Math.max(doluluk, kutu.adet > 0 ? 4 : 0)}%` }} />
        </div>
      </div>

      {/* Durum satırı: toplama talimatı veya cihaz sağlığı */}
      <div className="flex items-center justify-between border-t border-zemin-cizgi/60 pt-2.5">
        {kutu.isikYaniyor ? (
          <span className="rozet bg-marka/25 text-marka-metin">
            {kutu.alinacakMiktar ? `${kutu.alinacakMiktar} ADET AL` : "IŞIK YANIYOR"}
          </span>
        ) : (
          <span className={`text-[11px] font-bold ${
            cevrimdisi ? "text-durum-gri"
            : renk === "Kirmizi" ? "text-durum-kirmizi"
            : renk === "Sari" ? "text-durum-sari"
            : "text-slate-400"}`}>
            {cevrimdisi ? "⚡ BAĞLANTI YOK" : METIN[renk]}
          </span>
        )}

        <span className="flex items-center gap-2">
          {!cevrimdisi && <Sinyal rssi={kutu.rssi} />}
          <GecenSure zaman={kutu.sonGorulme} />
        </span>
      </div>
    </div>
  );
}
