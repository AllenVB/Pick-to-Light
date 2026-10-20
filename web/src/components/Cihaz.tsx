"use client";

import { useState } from "react";

/**
 * Cihaz (ESP32) göstergeleri.
 *
 * Sahada kurulum yapan kişi ekrana bakıp "bu kutu bağlandı mı, sinyali yeterli mi,
 * doğru firmware mi" sorularını cevaplayabilmeli.
 */

/** Wi-Fi sinyal gücü (dBm) — dört çubuklu gösterge. */
export function Sinyal({ rssi }: { rssi?: number | null }) {
  if (rssi == null) {
    return <span className="text-xs text-slate-600" title="Cihazdan sinyal bilgisi gelmedi">—</span>;
  }

  // Pratik eşikler: -55 üstü mükemmel, -70 iyi, -80 zayıf, altı sorunlu.
  const seviye = rssi >= -55 ? 4 : rssi >= -67 ? 3 : rssi >= -78 ? 2 : 1;
  const renk = seviye >= 3 ? "bg-durum-yesil" : seviye === 2 ? "bg-durum-sari" : "bg-durum-kirmizi";
  const aciklama = seviye >= 3 ? "iyi" : seviye === 2 ? "zayıf" : "sorunlu";

  return (
    <span className="inline-flex items-end gap-[2px]" title={`${rssi} dBm · ${aciklama}`}
      data-test="sinyal" data-seviye={seviye}>
      {[1, 2, 3, 4].map((c) => (
        <span key={c}
          className={`w-[3px] rounded-sm ${c <= seviye ? renk : "bg-slate-700"}`}
          style={{ height: `${4 + c * 3}px` }} />
      ))}
      <span className="ml-1.5 text-[11px] tabular-nums text-slate-400">{rssi}</span>
    </span>
  );
}

/** Tek tıkla panoya kopyalama — MQTT topic'i ve MAC gibi elle yazılması zor değerler için. */
export function Kopyala({ deger, etiket }: { deger: string; etiket?: string }) {
  const [kopyalandi, setKopyalandi] = useState(false);

  async function kopyala() {
    try {
      await navigator.clipboard.writeText(deger);
      setKopyalandi(true);
      setTimeout(() => setKopyalandi(false), 1600);
    } catch {
      /* tarayıcı izin vermezse sessiz geç; değer zaten ekranda yazılı */
    }
  }

  return (
    <button onClick={kopyala} title={`Kopyala: ${deger}`}
      className="inline-flex items-center gap-1.5 rounded-md border border-zemin-cizgi bg-zemin/60
                 px-2 py-1 font-mono text-[11px] text-slate-300 transition hover:border-marka-acik
                 hover:text-marka-acik">
      <span className="truncate">{etiket ?? deger}</span>
      <span aria-hidden className={kopyalandi ? "text-durum-yesil" : "opacity-50"}>
        {kopyalandi ? "✓" : "⧉"}
      </span>
    </button>
  );
}

/** Son görülme zamanını "14 sn önce" biçiminde yazar. */
export function GecenSure({ zaman }: { zaman?: string | null }) {
  if (!zaman) return <span className="text-xs text-slate-600">hiç görülmedi</span>;

  const saniye = Math.max(0, Math.round((Date.now() - new Date(zaman).getTime()) / 1000));
  const metin =
    saniye < 60 ? `${saniye} sn önce`
    : saniye < 3600 ? `${Math.floor(saniye / 60)} dk önce`
    : saniye < 86400 ? `${Math.floor(saniye / 3600)} sa önce`
    : `${Math.floor(saniye / 86400)} gün önce`;

  return (
    <span className={`text-xs tabular-nums ${saniye > 120 ? "text-slate-500" : "text-slate-400"}`}
      title={new Date(zaman).toLocaleString("tr-TR")}>
      {metin}
    </span>
  );
}
