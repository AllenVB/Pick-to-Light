"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { ApiHata, api } from "@/lib/api";
import { useCanliOlaylar } from "@/hooks/useCanliOlaylar";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import type { Alarm, Kutu, LedRengi } from "@/types";

/**
 * Operatör (kiosk) ekranı — NFR-08: 3 metre mesafeden okunabilir olmalı.
 * Kenar çubuğu yoktur; yalnızca ışığı yanan kutular büyük puntoyla gösterilir.
 */
const RENK: Record<LedRengi, { cerceve: string; lamba: string; metin: string; ad: string }> = {
  Yesil: { cerceve: "border-durum-yesil", lamba: "bg-durum-yesil", metin: "text-durum-yesil", ad: "AL" },
  Sari: { cerceve: "border-durum-sari", lamba: "bg-durum-sari nabiz-sari", metin: "text-durum-sari", ad: "AL · STOK AZALDI" },
  Kirmizi: { cerceve: "border-durum-kirmizi", lamba: "bg-durum-kirmizi nabiz-kirmizi", metin: "text-durum-kirmizi", ad: "ALMA · STOK YOK" },
};

export default function OperatorEkrani() {
  const oturum = useKorumaliSayfa();
  const [kutular, setKutular] = useState<Kutu[]>([]);
  const [sonAlarm, setSonAlarm] = useState<Alarm | null>(null);
  const [saat, setSaat] = useState("");
  const [islemdeki, setIslemdeki] = useState<number | null>(null);
  const [hata, setHata] = useState<string | null>(null);

  const yenile = useCallback(() => {
    api.kutular().then(setKutular).catch(() => undefined);
    // Yalnızca son 2 dakikanın alarmı gösterilir; eski uyarılar ekranı kirletmesin.
    api.alarmlar(false)
      .then((a) => {
        const taze = a.find((x) => Date.now() - new Date(x.zaman).getTime() < 120_000);
        setSonAlarm(taze ?? null);
      })
      .catch(() => undefined);
  }, []);

  useEffect(() => { if (oturum) yenile(); }, [oturum, yenile]);

  useEffect(() => {
    const t = setInterval(() => setSaat(new Date().toLocaleTimeString("tr-TR")), 1000);
    return () => clearInterval(t);
  }, []);

  useCanliOlaylar({
    KutuIsikDegisti: yenile,
    StokGuncellendi: yenile,
    KalemTeyitEdildi: yenile,
    AlarmOlustu: yenile,
    CihazDurumDegisti: yenile,
  });

  async function teyitEt(kalemId: number) {
    setIslemdeki(kalemId);
    setHata(null);
    try {
      await api.kalemTeyit(kalemId, "Manuel");
      yenile();
    } catch (h) {
      setHata(h instanceof ApiHata ? h.message : "Teyit gönderilemedi.");
    } finally {
      setIslemdeki(null);
    }
  }

  if (!oturum) return null;

  const yananlar = kutular.filter((k) => k.isikYaniyor);

  return (
    <div className="flex min-h-screen flex-col">
      <header className="flex items-center justify-between border-b border-zemin-cizgi pb-4">
        <div className="flex items-center gap-4">
          <div className="flex gap-2">
            <span className="h-4 w-4 rounded-full bg-durum-yesil" />
            <span className="h-4 w-4 rounded-full bg-durum-sari" />
            <span className="h-4 w-4 rounded-full bg-durum-kirmizi" />
          </div>
          <h1 className="text-2xl font-extrabold">Toplama Ekranı</h1>
        </div>
        <div className="flex items-center gap-6">
          <span className="font-mono text-2xl text-slate-300">{saat}</span>
          <Link href="/" className="dugme-ikincil text-xs">Panele dön</Link>
        </div>
      </header>

      {sonAlarm && (sonAlarm.tip === "StokYok" || sonAlarm.tip === "CihazOffline") && (
        <div className="mt-4 rounded-xl border-2 border-durum-kirmizi bg-durum-kirmizi/10 px-6 py-4 nabiz-kirmizi">
          <p className="text-xs font-bold uppercase tracking-widest text-durum-kirmizi">{sonAlarm.tip}</p>
          <p className="mt-1 text-xl font-bold">{sonAlarm.mesaj}</p>
        </div>
      )}

      {hata && (
        <p className="mt-4 rounded-lg border border-durum-kirmizi/40 bg-durum-kirmizi/10 px-4 py-2 text-sm text-durum-kirmizi">
          {hata}
        </p>
      )}

      <main className="flex flex-1 items-center justify-center py-8">
        {yananlar.length === 0 ? (
          <div className="text-center">
            <p className="text-5xl font-extrabold text-slate-600">Bekleniyor</p>
            <p className="mt-3 text-lg text-slate-500">
              Sipariş başlatıldığında alınacak kutular burada büyük puntoyla görünür.
            </p>
            <p className="mt-6 text-sm text-slate-600">
              {kutular.filter((k) => k.baglantiDurumu === "Online").length}/{kutular.length} kutu çevrimiçi
            </p>
          </div>
        ) : (
          <div className={`grid w-full gap-6 ${yananlar.length === 1 ? "grid-cols-1" : "grid-cols-2"}`}>
            {yananlar.map((k) => {
              const r = RENK[k.anlikRenk ?? "Yesil"];
              return (
                <div key={k.id} data-test="kiosk-karti" data-kutu={k.kod}
                  className={`rounded-2xl border-4 ${r.cerceve} bg-zemin-kart p-8`}>
                  <div className="flex items-start justify-between">
                    <div>
                      <p className="text-6xl font-black leading-none">{k.kod}</p>
                      <p className="mt-3 text-2xl text-slate-300">{k.rafKonumu ?? "—"}</p>
                    </div>
                    <span className={`h-20 w-20 shrink-0 rounded-full ${r.lamba}`} />
                  </div>

                  <div className="mt-6 flex items-end gap-6">
                    <div>
                      <p className="text-xs uppercase tracking-widest text-slate-500">alınacak</p>
                      <p className={`text-7xl font-black leading-none ${r.metin}`}>
                        {k.alinacakMiktar ?? "—"}
                      </p>
                      <p className="mt-1 text-sm text-slate-500">adet</p>
                    </div>
                    <div className="flex-1">
                      <p className="text-3xl font-bold">{k.urunAdi ?? "—"}</p>
                      <p className="mt-2 text-sm text-slate-400">
                        {k.siparisNo ? `sipariş ${k.siparisNo} · ` : ""}kutuda {k.adet} adet kaldı
                      </p>
                    </div>
                  </div>

                  <div className="mt-6 flex items-center justify-between border-t border-zemin-cizgi pt-4">
                    <p className={`text-xl font-black tracking-wide ${r.metin}`}>{r.ad}</p>

                    {/* Buton/sensör çalışmazsa ekrandan teyit (FR-09 yedek yolu) */}
                    {k.aktifKalemId ? (
                      <button
                        className="rounded-xl bg-marka px-8 py-4 text-lg font-bold text-white transition
                                   hover:bg-marka-acik disabled:opacity-40"
                        disabled={islemdeki === k.aktifKalemId}
                        onClick={() => teyitEt(k.aktifKalemId!)}>
                        {islemdeki === k.aktifKalemId ? "Gönderiliyor…" : "ALDIM ✓"}
                      </button>
                    ) : (
                      <p className="text-sm text-slate-500">ürünü al → butona bas</p>
                    )}
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </main>
    </div>
  );
}
