"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { ApiHata, api } from "@/lib/api";
import { useCanliOlaylar } from "@/hooks/useCanliOlaylar";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import { AlinanGram, baskulVar, canliKutular, useCanliAgirlik } from "@/components/Baskul";
import type { Alarm, Kutu, LedRengi } from "@/types";

/**
 * Operatör (kiosk) ekranı — NFR-08: 3 metre mesafeden okunabilir olmalı.
 * Kenar çubuğu yoktur; yalnızca ışığı yanan kutular büyük puntoyla gösterilir.
 */
const RENK: Record<LedRengi, { cerceve: string; lamba: string; metin: string; ad: string }> = {
  Yesil: { cerceve: "border-durum-yesil", lamba: "bg-led-yesil", metin: "text-durum-yesil", ad: "AL" },
  Sari: { cerceve: "border-durum-sari", lamba: "bg-led-sari nabiz-sari", metin: "text-durum-sari", ad: "AL · STOK AZALDI" },
  Kirmizi: { cerceve: "border-durum-kirmizi", lamba: "bg-led-kirmizi nabiz-kirmizi", metin: "text-durum-kirmizi", ad: "ALMA · STOK YOK" },
};

const ALARM_BASLIGI: Record<string, string> = {
  StokYok: "STOK YOK",
  CihazOffline: "KUTU ÇEVRİMDIŞI",
  AgirlikUyusmazligi: "AĞIRLIK UYUŞMAZLIĞI",
  StokAzaldi: "STOK AZALDI",
  TeyitZamanAsimi: "TEYİT ZAMAN AŞIMI",
  BeklenmeyenTeyit: "BEKLENMEYEN TEYİT",
};

export default function OperatorEkrani() {
  const oturum = useKorumaliSayfa();
  const [hamKutular, setKutular] = useState<Kutu[]>([]);
  const kutular = canliKutular(hamKutular, useCanliAgirlik());
  const [sonAlarm, setSonAlarm] = useState<Alarm | null>(null);
  const [saat, setSaat] = useState("");
  const [islemdeki, setIslemdeki] = useState<number | null>(null);
  const [hata, setHata] = useState<string | null>(null);
  const [tamamlanan, setTamamlanan] = useState<{ kod: string; urun: string; miktar: number } | null>(null);

  // Işığı yanan kutular kalem kimliğine göre hatırlanır: teyit olayı geldiğinde kart
  // çoktan kaybolmuş olabilir, bildirimde yine de kutu ve ürün adı gösterilebilsin.
  const yananKalemler = useRef(new Map<number, Kutu>());
  useEffect(() => {
    for (const k of kutular) if (k.isikYaniyor && k.aktifKalemId) yananKalemler.current.set(k.aktifKalemId, k);
  }, [kutular]);

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
    KalemTeyitEdildi: (veri) => {
      const kalemId = (veri as { kalemId: number }).kalemId;
      const k = yananKalemler.current.get(kalemId);
      if (k) {
        setTamamlanan({ kod: k.kod, urun: k.urunAdi ?? "", miktar: k.alinacakMiktar ?? 0 });
        setTimeout(() => setTamamlanan(null), 4000);
      }
      yenile();
    },
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
  const cevrimdisiler = kutular.filter((k) => k.baglantiDurumu === "Offline");

  const alarmGecerli =
    sonAlarm?.tip === "StokYok" ||
    (sonAlarm?.tip === "CihazOffline" &&
      // Alarmın kutusu hâlâ çevrimdışıysa anlamlıdır.
      cevrimdisiler.some((k) => k.id === sonAlarm.kutuId));

  const gosterilecekAlarm = alarmGecerli ? sonAlarm : null;
  const sirada = kutular.reduce((t, k) => t + (k.siradakiSayisi ?? 0), 0);

  return (
    <div className="flex min-h-screen flex-col">
      <header className="flex items-center justify-between border-b border-zemin-cizgi pb-4">
        <div className="flex items-center gap-4">
          <div className="flex gap-2">
            <span className="h-4 w-4 rounded-full bg-led-yesil" />
            <span className="h-4 w-4 rounded-full bg-led-sari" />
            <span className="h-4 w-4 rounded-full bg-led-kirmizi" />
          </div>
          <h1 className="text-2xl font-extrabold">Toplama Ekranı</h1>
        </div>
        <div className="flex items-center gap-6">
          <span className="font-mono text-2xl text-slate-300">{saat}</span>
          <Link href="/" className="dugme-ikincil text-xs">Panele dön</Link>
        </div>
      </header>

      {/* Cihaz kopması alarmı, kutu geri döndüyse gösterilmez: ekrandaki canlı
          durumla çelişen bir uyarı operatörü yanıltır. Çevrimdışı kutular zaten
          aşağıdaki sarı şeritte listeleniyor. */}
      {gosterilecekAlarm && (
        <div className="mt-4 rounded-xl border-2 border-durum-kirmizi bg-durum-kirmizi/10 px-6 py-4 nabiz-kirmizi"
          data-test="kiosk-alarm">
          <p className="text-xs font-bold uppercase tracking-widest text-durum-kirmizi">
            {ALARM_BASLIGI[gosterilecekAlarm.tip] ?? gosterilecekAlarm.tip}
          </p>
          <p className="mt-1 text-xl font-bold">{gosterilecekAlarm.mesaj}</p>
        </div>
      )}

      {tamamlanan && (
        <div className="mt-4 flex items-center gap-4 rounded-xl border-2 border-durum-yesil bg-durum-yesil/10 px-6 py-4"
          role="status" data-test="kiosk-tamam">
          <span aria-hidden className="text-4xl text-durum-yesil">✓</span>
          <p className="text-xl font-bold text-durum-yesil">
            {tamamlanan.kod} · {tamamlanan.urun} — {tamamlanan.miktar} adet teyit edildi
          </p>
        </div>
      )}

      {hata && (
        <p className="mt-4 rounded-lg border border-durum-kirmizi/40 bg-durum-kirmizi/10 px-4 py-2 text-sm text-durum-kirmizi">
          {hata}
        </p>
      )}

      {/* Çevrimdışı kutu varsa kalıcı uyarı: operatör hiç yanmayacak bir ışığı beklemesin. */}
      {cevrimdisiler.length > 0 && (
        <div className="mt-4 flex items-center gap-3 rounded-xl border border-durum-sari/40
                        bg-durum-sari/10 px-5 py-3" data-test="cihaz-uyarisi">
          <span aria-hidden className="text-xl">⚡</span>
          <p className="text-base font-semibold text-durum-sari">
            {cevrimdisiler.length} kutu çevrimdışı: {cevrimdisiler.map((k) => k.kod).join(", ")} —
            bu kutular için ışık yanmaz, sorumluya bildirin.
          </p>
        </div>
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
              {sirada > 0 && ` · ${sirada} kalem sırada`}
            </p>
          </div>
        ) : (
          <div className={`grid w-full gap-6 ${
            yananlar.length === 1 ? "grid-cols-1"
            : yananlar.length <= 4 ? "grid-cols-2"
            : "grid-cols-3"}`}>
            {yananlar.map((k) => {
              const r = RENK[k.anlikRenk ?? "Yesil"];
              return (
                <div key={k.id} data-test="kiosk-karti" data-kutu={k.kod}
                  className={`rounded-2xl border-4 ${r.cerceve} bg-zemin-kart p-8`}>
                  <div className="flex items-start justify-between">
                    <div>
                      <p className="text-6xl font-black leading-none">{k.kod}</p>
                      <p className="mt-3 text-2xl text-slate-300">{k.rafKonumu ?? "—"}</p>
                      {(k.siradakiSayisi ?? 0) > 0 && (
                        <p className="mt-2 inline-block rounded-full bg-durum-sari/20 px-3 py-1
                                      text-sm font-bold text-durum-sari">
                          +{k.siradakiSayisi} sipariş sırada
                        </p>
                      )}
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

                  {/* Baskül varsa: alınan gram/adet canlı izlenir, ağırlık oturunca teyit otomatiktir. */}
                  <AlinanGram kutu={k} />

                  <div className="mt-6 flex items-center justify-between gap-4 border-t border-zemin-cizgi pt-4">
                    <div>
                      <p className={`text-xl font-black tracking-wide ${r.metin}`}>{r.ad}</p>
                      <KioskGeriSayim sonTarih={k.teyitSonTarihi} />
                    </div>

                    {/* Buton/sensör çalışmazsa ekrandan teyit (FR-09 yedek yolu) */}
                    {k.aktifKalemId ? (
                      <button
                        className="rounded-xl bg-marka px-10 py-5 text-xl font-bold text-white shadow-lg
                                   transition hover:bg-marka-acik active:scale-95 disabled:opacity-40"
                        disabled={islemdeki === k.aktifKalemId}
                        onClick={() => teyitEt(k.aktifKalemId!)}>
                        {islemdeki === k.aktifKalemId ? "Gönderiliyor…"
                          : baskulVar(k) && k.birimAgirlikG ? "ELLE TEYİT" : "ALDIM ✓"}
                      </button>
                    ) : (
                      <p className="text-sm text-slate-500">
                        {baskulVar(k) && k.birimAgirlikG ? "ürünü al → baskül otomatik teyit eder" : "ürünü al → butona bas"}
                      </p>
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

/**
 * Kiosk için teyit geri sayımı.
 *
 * Süre dolduğunda ışık söner ve rezerve stok iade edilir; operatör bunu
 * önceden görmeli. Son dakikada sarıya döner.
 */
function KioskGeriSayim({ sonTarih }: { sonTarih?: string | null }) {
  const [kalan, setKalan] = useState<number | null>(null);

  useEffect(() => {
    if (!sonTarih) { setKalan(null); return; }

    const hesapla = () =>
      setKalan(Math.max(0, Math.round((new Date(sonTarih).getTime() - Date.now()) / 1000)));

    hesapla();
    const sayac = setInterval(hesapla, 1000);
    return () => clearInterval(sayac);
  }, [sonTarih]);

  if (kalan === null) return null;

  const dakika = Math.floor(kalan / 60);
  const saniye = kalan % 60;
  const azKaldi = kalan <= 60;

  return (
    <p className={`mt-1 font-mono text-lg tabular-nums ${azKaldi ? "text-durum-sari" : "text-slate-500"}`}
      data-test="kiosk-geri-sayim">
      {kalan === 0 ? "süre doldu" : `${dakika}:${String(saniye).padStart(2, "0")} kaldı`}
    </p>
  );
}
