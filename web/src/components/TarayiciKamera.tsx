"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { vision, type Tespit } from "@/lib/vision";

/**
 * Tarayıcı kamerası.
 *
 * Neden tarayıcıdan? Windows, masaüstü uygulamalarının (Python/OpenCV) kameraya erişimini ayrı bir
 * gizlilik anahtarıyla kapatabilir. Tarayıcı ise kendi izin penceresini açar: kullanıcı "İzin ver" der ve
 * kamera çalışır. Tarayıcı kareleri görüntü servisine gönderir (POST /kare); servis o kareleri tanır ve
 * backend teyit anında aynı kareleri kullanır.
 */

export type KameraDurumu = "kapali" | "izin-bekliyor" | "acik" | "hata";

const KARE_GEN = 640;
const KARE_YUK = 480;
const GONDERME_ARALIGI_MS = 250;   // ~4 kare/sn: tanıma için yeterli, ağı yormaz

function hataMetni(hata: unknown): string {
  if (typeof window !== "undefined" && !window.isSecureContext) {
    return "Tarayıcı kamerası yalnızca güvenli adreste (localhost ya da https) açılır.";
  }
  const ad = hata instanceof DOMException ? hata.name : "";
  switch (ad) {
    case "NotAllowedError":
      return "Kamera izni verilmedi. Adres çubuğundaki kamera/kilit simgesinden izin verip tekrar deneyin.";
    case "NotFoundError":
      return "Bu bilgisayarda kamera bulunamadı.";
    case "NotReadableError":
      return "Kamera başka bir uygulamada açık olabilir. Onu kapatıp tekrar deneyin.";
    case "OverconstrainedError":
      return "Kamera istenen çözünürlüğü desteklemiyor.";
    default:
      return "Kamera açılamadı.";
  }
}

export function useTarayiciKamera() {
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const akisRef = useRef<MediaStream | null>(null);
  const [durum, setDurum] = useState<KameraDurumu>("kapali");
  const [hata, setHata] = useState<string | null>(null);

  const durdur = useCallback(() => {
    akisRef.current?.getTracks().forEach((t) => t.stop());
    akisRef.current = null;
    if (videoRef.current) videoRef.current.srcObject = null;
    setDurum("kapali");
  }, []);

  const baslat = useCallback(async () => {
    if (akisRef.current) return;
    setHata(null);
    setDurum("izin-bekliyor");
    try {
      // 4:3 istenir ki sunucudaki 640×480 çerçeveyle ve ekrandaki kutularla hizalı kalsın.
      const akis = await navigator.mediaDevices.getUserMedia({
        video: { width: { ideal: KARE_GEN }, height: { ideal: KARE_YUK }, aspectRatio: { ideal: 4 / 3 } },
        audio: false,
      });
      akisRef.current = akis;
      if (videoRef.current) {
        videoRef.current.srcObject = akis;
        await videoRef.current.play().catch(() => undefined);
      }
      setDurum("acik");
    } catch (h) {
      setHata(hataMetni(h));
      setDurum("hata");
    }
  }, []);

  // Kamera açıkken kareler servise gönderilir. Önceki gönderim bitmeden yenisi başlamaz.
  useEffect(() => {
    if (durum !== "acik") return;

    const tuval = document.createElement("canvas");
    tuval.width = KARE_GEN;
    tuval.height = KARE_YUK;
    const ctx = tuval.getContext("2d");
    let yolda = false;

    const t = setInterval(() => {
      const video = videoRef.current;
      if (!ctx || !video || yolda || video.readyState < 2 || video.videoWidth === 0) return;

      // Merkezden 4:3 kırpma: ekrandaki object-cover görünümüyle birebir aynı alan.
      const oran = KARE_GEN / KARE_YUK;
      let kw = video.videoWidth, kh = video.videoHeight, kx = 0, ky = 0;
      if (kw / kh > oran) { const yeni = kh * oran; kx = (kw - yeni) / 2; kw = yeni; }
      else { const yeni = kw / oran; ky = (kh - yeni) / 2; kh = yeni; }
      ctx.drawImage(video, kx, ky, kw, kh, 0, 0, KARE_GEN, KARE_YUK);

      yolda = true;
      tuval.toBlob(
        (blob) => {
          if (!blob) { yolda = false; return; }
          vision.kareGonder(blob).finally(() => { yolda = false; });
        },
        "image/jpeg",
        0.7,
      );
    }, GONDERME_ARALIGI_MS);

    return () => clearInterval(t);
  }, [durum]);

  // Sayfadan çıkınca kamera kapanır (ışık yanık kalmasın).
  useEffect(() => durdur, [durdur]);

  return { videoRef, durum, hata, baslat, durdur };
}

/** Tespit edilen nesnelerin çevresine kutu ve ürün adı çizer (kutular 640×480 çerçevenin pikselleridir). */
export function TespitKatmani({ tespitler }: { tespitler: Tespit[] }) {
  return (
    <div className="pointer-events-none absolute inset-0" aria-hidden>
      {tespitler.map((t, i) => {
        const [x, y, w, h] = t.kutu;
        const taninan = t.urunId !== null;
        return (
          <div key={i} className={`absolute rounded-sm border-2 ${taninan ? "border-durum-yesil" : "border-durum-kirmizi"}`}
            style={{
              left: `${(x / KARE_GEN) * 100}%`, top: `${(y / KARE_YUK) * 100}%`,
              width: `${(w / KARE_GEN) * 100}%`, height: `${(h / KARE_YUK) * 100}%`,
            }}>
            <span className={`absolute -top-5 left-0 whitespace-nowrap rounded px-1 text-[10px] font-bold text-white ${
              taninan ? "bg-durum-yesil/90" : "bg-durum-kirmizi/90"}`}>
              {taninan ? `${t.urunAdi} %${Math.round(t.guven * 100)}` : "bilinmiyor"}
            </span>
          </div>
        );
      })}
    </div>
  );
}
