"use client";

import { useEffect, useState } from "react";
import { TespitKatmani, useTarayiciKamera } from "@/components/TarayiciKamera";
import { vision, type SahneNesnesi, type TaniYaniti, type VisionSaglik } from "@/lib/vision";
import { useToast } from "@/components/Toast";

const SAHNELER: { ad: string; nesneler: SahneNesnesi[] }[] = [
  { ad: "10 vida", nesneler: [{ tip: "vida", adet: 10 }] },
  { ad: "3 civata", nesneler: [{ tip: "civata", adet: 3 }] },
  { ad: "4 somun", nesneler: [{ tip: "somun", adet: 4 }] },
  { ad: "Karışık", nesneler: [{ tip: "vida", adet: 4 }, { tip: "civata", adet: 2 }, { tip: "somun", adet: 3 }] },
  { ad: "Boş masa", nesneler: [] },
];

const KAYNAKLAR = [
  { tip: "tarayici", ad: "Tarayıcı kamerası", ipucu: "İzin ister; Windows engeline takılmaz" },
  { tip: "sim", ad: "Sanal sahne", ipucu: "Kamera olmadan deneme" },
  { tip: "kamera:0", ad: "Sunucu kamerası", ipucu: "Python'un doğrudan açtığı kamera" },
] as const;

/**
 * Canlı görüntü kartı: kaynak seçimi (tarayıcı kamerası / sanal sahne / sunucu kamerası), önizleme ve
 * tanınan nesnelerin kutuları. Kamera & Tanıma ve Tartı & Sayım sayfaları ortak kullanır.
 */
export function KameraGorunumu({ saglik, tani, yenile }: {
  saglik: VisionSaglik | null | undefined;
  tani: TaniYaniti | null;
  /** Kaynak değiştikten sonra üst sayfanın sağlık/tanı bilgisini hemen tazelemesi için. */
  yenile: () => void;
}) {
  const toast = useToast();
  const kamera = useTarayiciKamera();
  const [degistiriliyor, setDegistiriliyor] = useState(false);

  const bagli = !!saglik;
  const kaynak = saglik?.kaynak ?? null;
  const tarayici = kaynak === "tarayici";

  // Kaynak başka bir yerden (ya da önceki oturumdan) tarayıcıya ayarlıysa ve izin daha önce verilmişse
  // kamera sessizce açılır; izin yoksa kullanıcı "Kamerayı aç" der.
  useEffect(() => {
    if (!tarayici || kamera.durum !== "kapali") return;
    navigator.permissions?.query({ name: "camera" as PermissionName })
      .then((p) => { if (p.state === "granted") kamera.baslat(); })
      .catch(() => undefined);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tarayici]);

  async function kaynakSec(tip: string) {
    setDegistiriliyor(true);
    try {
      const sonuc = await vision.kaynakSec(tip);
      if (!sonuc) { toast("Görüntü servisine ulaşılamadı.", "hata"); return; }
      if (tip === "tarayici") await kamera.baslat();   // izin penceresi burada açılır
      else kamera.durdur();
      yenile();
    } catch (h) {
      toast(h instanceof Error ? h.message : "Kaynak değiştirilemedi.", "hata");
    } finally {
      setDegistiriliyor(false);
    }
  }

  async function sahne(nesneler: SahneNesnesi[]) {
    try {
      await vision.simSahne(nesneler);
    } catch (h) {
      toast(h instanceof Error ? h.message : "Sahne ayarlanamadı.", "hata");
    }
  }

  const kaynakAdi = kaynak === "tarayici" ? "TARAYICI KAMERASI"
    : kaynak === "sim" ? "SANAL SAHNE"
    : kaynak?.startsWith("kamera") ? "SUNUCU KAMERASI" : "—";

  return (
    <section className="kart space-y-3" data-test="kamera-gorunumu">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="etiket">Canlı görüntü</h2>
        <span className={`rozet ${bagli ? "bg-durum-yesil/15 text-durum-yesil" : "bg-slate-600/20 text-slate-400"}`}
          data-test="kamera-kaynak-rozeti">
          ● {bagli ? kaynakAdi : "BAĞLANTI YOK"}
        </span>
      </div>

      {bagli && (
        <div className="flex flex-wrap gap-2" data-test="kaynak-secici">
          {KAYNAKLAR.map((k) => {
            const aktif = kaynak === k.tip || (k.tip === "kamera:0" && kaynak?.startsWith("kamera") === true);
            return (
              <button key={k.tip} title={k.ipucu} disabled={degistiriliyor}
                data-test={`kaynak-${k.tip.replace(":", "-")}`}
                onClick={() => kaynakSec(k.tip)}
                className={`rounded-lg border px-3 py-1.5 text-xs font-semibold transition disabled:opacity-50 ${
                  aktif ? "border-marka-acik bg-marka/25 text-marka-metin" : "border-zemin-cizgi text-slate-300 hover:border-marka-acik/60"}`}>
                {k.ad}
              </button>
            );
          })}
        </div>
      )}

      <div className="relative aspect-[4/3] w-full overflow-hidden rounded-lg border border-zemin-cizgi bg-black/40">
        {!bagli && <div className="flex h-full items-center justify-center text-sm text-slate-500">görüntü yok</div>}

        {/* Tarayıcı kamerası: yerel video + sunucunun bulduğu kutular */}
        {bagli && tarayici && (
          <>
            <video ref={kamera.videoRef} playsInline muted className="h-full w-full object-cover"
              data-test="tarayici-video" />
            {kamera.durum === "acik" && tani && <TespitKatmani tespitler={tani.tespitler} />}
            {kamera.durum !== "acik" && (
              <div className="absolute inset-0 flex flex-col items-center justify-center gap-3 bg-black/50 p-4 text-center">
                <p className="text-sm text-slate-200">
                  {kamera.durum === "izin-bekliyor"
                    ? "Tarayıcının kamera izin penceresinde “İzin ver”e basın…"
                    : "Tarayıcı kamerası kapalı."}
                </p>
                {kamera.durum !== "izin-bekliyor" && (
                  <button className="dugme" onClick={() => kamera.baslat()} data-test="kamerayi-ac">Kamerayı aç</button>
                )}
              </div>
            )}
          </>
        )}

        {/* Sanal sahne ve sunucu kamerası: servisin işaretlediği MJPEG akışı */}
        {bagli && !tarayici && (
          // eslint-disable-next-line @next/next/no-img-element
          <img src={vision.onizlemeUrl} alt="Kamera önizlemesi" className="h-full w-full object-contain"
            data-test="kamera-onizleme" />
        )}
      </div>

      {kamera.hata && (
        <p className="rounded-lg border border-durum-kirmizi/40 bg-durum-kirmizi/10 px-3 py-2 text-sm text-durum-kirmizi"
          data-test="kamera-hata">{kamera.hata}</p>
      )}

      {bagli && tarayici && kamera.durum === "acik" && (
        <p className="ipucu" data-test="kamera-ipucu">
          Tarayıcı kamerası açık; kareler tanıma servisine gönderiliyor. Ürünleri düz, tek renkli bir zemine koyun,
          ışığı sabit tutun. Sayfadan çıkınca kamera kapanır.
        </p>
      )}

      {bagli && kaynak?.startsWith("kamera") && saglik?.kameraSorunu === "donuk" && (
        <p className="rounded-lg border border-durum-sari/40 bg-durum-sari/10 px-3 py-2 text-sm text-durum-sari"
          data-test="kamera-donuk">
          Sunucu kamerasından canlı görüntü gelmiyor (Windows, masaüstü uygulamalarının kamerasını kapatmış olabilir).
          <b> Tarayıcı kamerası</b> seçeneğiyle izin verip devam edebilirsiniz.
        </p>
      )}

      {saglik?.sanal && (
        <div className="space-y-2" data-test="sim-sahne">
          <p className="ipucu">Kamera takılı değil: masaya parça koyma simülasyonu</p>
          <div className="flex flex-wrap gap-2">
            {SAHNELER.map((s) => (
              <button key={s.ad} className="dugme-ikincil dugme-kucuk" onClick={() => sahne(s.nesneler)}>
                {s.ad}
              </button>
            ))}
          </div>
        </div>
      )}
    </section>
  );
}
