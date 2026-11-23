"use client";

import { useEffect, useRef, useState } from "react";
import { VISION_URL, vision, type DogrulamaYaniti, type DogrulamaDurumu, type OgretilenProfil } from "@/lib/vision";

const GORUNUM: Record<DogrulamaDurumu, { baslik: string; sinif: string; ipucu: string }> = {
  bekliyor: {
    baslik: "Ürünü kameraya gösterin",
    sinif: "border-zemin-cizgi bg-zemin text-slate-300",
    ipucu: "Ürünü düz, sade bir zemine koyun ya da kameraya sabit tutun.",
  },
  dogru: {
    baslik: "DOĞRU ÜRÜN",
    sinif: "border-durum-yesil/60 bg-durum-yesil/15 text-durum-yesil",
    ipucu: "Gösterilen ürün beklenen ürünle aynı.",
  },
  yanlis: {
    baslik: "YANLIŞ ÜRÜN",
    sinif: "border-durum-kirmizi/60 bg-durum-kirmizi/15 text-durum-kirmizi",
    ipucu: "Gösterilen ürün beklenen ürün değil.",
  },
  taninmadi: {
    baslik: "Ürün tanınamadı",
    sinif: "border-durum-sari/50 bg-durum-sari/10 text-durum-sari",
    ipucu: "Hiçbir öğretilmiş ürüne benzemiyor: ürün öğretilmemiş olabilir, ışığı ve zemini kontrol edin.",
  },
  belirsiz: {
    baslik: "Emin olunamadı",
    sinif: "border-durum-sari/50 bg-durum-sari/10 text-durum-sari",
    ipucu: "Görüntü değişkenlik gösteriyor: elinizi çekin ve ürünü sabit tutun.",
  },
  ogretilmemis: {
    baslik: "Bu ürün henüz öğretilmedi",
    sinif: "border-durum-sari/50 bg-durum-sari/10 text-durum-sari",
    ipucu: "Aşağıdaki \"Ürün öğret\" bölümünden bu ürünü kameraya göstererek öğretin.",
  },
};

export interface OnaylananUrun { urunId: number; urunAdi: string }

/**
 * "Ürünü kameraya göster": beklenen ürünü seçin, kameraya ürün gösterin.
 * Ürün tanınınca görüntü DONDURULUR ve "şu ürün tanımlandı, onaylıyor musunuz?" diye sorulur:
 *   onay  → bir sonraki adıma geçilir (onaylanan ürün üst sayfaya bildirilir)
 *   red   → kamera yeniden canlıya döner ve tanıma devam eder.
 */
export function UrunDogrulama({ profiller, bagli, onDondur, onOnay }: {
  profiller: OgretilenProfil[];
  bagli: boolean;
  /** Donmuş kare (nesne URL'i) ya da null: kameranın canlıya dönmesi. */
  onDondur?: (kareUrl: string | null) => void;
  onOnay?: (urun: OnaylananUrun | null) => void;
}) {
  const [beklenen, setBeklenen] = useState<number | "">("");
  const [karar, setKarar] = useState<DogrulamaYaniti | null>(null);
  const [asama, setAsama] = useState<"bekle" | "onay" | "onaylandi">("bekle");
  const [bulunan, setBulunan] = useState<OnaylananUrun | null>(null);
  const [sogumaBitis, setSogumaBitis] = useState(0);
  const kareUrl = useRef<string | null>(null);

  function donukKareyiBirak() {
    if (kareUrl.current) URL.revokeObjectURL(kareUrl.current);
    kareUrl.current = null;
    onDondur?.(null);
  }

  async function dondur(urun: OnaylananUrun) {
    try {
      const yanit = await fetch(`${VISION_URL}/kare.jpg?t=${Date.now()}`, { cache: "no-store" });
      if (yanit.ok) {
        kareUrl.current = URL.createObjectURL(await yanit.blob());
        onDondur?.(kareUrl.current);
      }
    } catch { /* kare alınamazsa görüntü canlı kalır; onay yine istenir */ }
    setBulunan(urun);
    setAsama("onay");
  }

  // Yalnızca "bekle" aşamasında tanıma yoklanır: dondurulunca karar sabit kalır.
  useEffect(() => {
    if (asama !== "bekle") return;
    if (!bagli || beklenen === "") { setKarar(null); return; }
    let iptal = false;
    const yokla = async () => {
      const k = await vision.dogrula(beklenen).catch(() => null);
      if (iptal) return;
      setKarar(k);
      if (k?.durum === "dogru" && k.algilanan && Date.now() > sogumaBitis) {
        iptal = true;
        void dondur({ urunId: k.algilanan.urunId, urunAdi: k.algilanan.urunAdi ?? "" });
      }
    };
    yokla();
    const t = setInterval(yokla, 700);
    return () => { iptal = true; clearInterval(t); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [beklenen, bagli, asama, sogumaBitis]);

  useEffect(() => () => donukKareyiBirak(), []);   // eslint-disable-line react-hooks/exhaustive-deps

  function onayla() {
    setAsama("onaylandi");
    if (bulunan) onOnay?.(bulunan);
  }

  function reddet() {
    donukKareyiBirak();
    setBulunan(null);
    setKarar(null);
    setSogumaBitis(Date.now() + 3000);   // ürün hâlâ kamerada: hemen yeniden sormasın
    setAsama("bekle");
  }

  function yeniden() {
    donukKareyiBirak();
    onOnay?.(null);
    setBulunan(null);
    setKarar(null);
    setSogumaBitis(Date.now() + 3000);
    setAsama("bekle");
  }

  const g = karar ? GORUNUM[karar.durum] : null;
  const beklenenAd = profiller.find((p) => p.urunId === beklenen)?.ad;

  return (
    <section className="kart space-y-3" data-test="urun-dogrulama">
      <div>
        <h2 className="etiket">Ürün doğrulama</h2>
        <p className="mt-1 text-sm text-slate-400">
          Beklenen ürünü seçin, ardından ürünü kameraya gösterin: sistem doğru mu yanlış mı olduğunu söyler.
        </p>
      </div>

      <select className="girdi" value={beklenen} data-test="dogrula-urun"
        onChange={(e) => setBeklenen(e.target.value ? Number(e.target.value) : "")}>
        <option value="">Beklenen ürünü seçin…</option>
        {profiller.map((p) => <option key={p.urunId} value={p.urunId}>{p.ad}</option>)}
      </select>

      {asama === "onay" && bulunan && (
        <div className="space-y-3 rounded-xl border-2 border-marka-acik bg-marka/15 px-4 py-5 text-center"
          data-test="onay-istegi">
          <p className="text-xs font-semibold tracking-widest text-marka-metin">❄ GÖRÜNTÜ DONDURULDU</p>
          <p className="text-2xl font-extrabold">Ürün tanımlandı: {bulunan.urunAdi}</p>
          <p className="text-sm text-slate-300">Bu ürün doğru mu? Onaylarsanız bir sonraki adıma geçilir.</p>
          <div className="flex justify-center gap-3">
            <button className="dugme" onClick={onayla} data-test="onayla">✔ Onayla</button>
            <button className="dugme-tehlike" onClick={reddet} data-test="reddet">✘ Onaylama</button>
          </div>
        </div>
      )}

      {asama === "onaylandi" && bulunan && (
        <div className="space-y-3 rounded-xl border-2 border-durum-yesil/60 bg-durum-yesil/15 px-4 py-5 text-center"
          data-test="onaylandi">
          <p className="text-3xl font-extrabold text-durum-yesil">✔ Onaylandı</p>
          <p className="text-lg font-bold">{bulunan.urunAdi}</p>
          <p className="text-sm text-slate-300">Sıradaki adım: baskül → adet hesabı.</p>
          <button className="dugme" onClick={yeniden} data-test="yeni-urun">Yeni ürün doğrula</button>
        </div>
      )}

      {asama === "bekle" && Date.now() < sogumaBitis && (
        <p className="ipucu" data-test="soguma">Ürünü kameradan çekin; tanıma birkaç saniye sonra yeniden başlar.</p>
      )}

      {asama === "bekle" && g && karar && (
        <div className={`rounded-xl border-2 px-4 py-5 text-center ${g.sinif}`} data-test="dogrulama-sonucu"
          data-durum={karar.durum}>
          <p className="text-3xl font-extrabold">
            {karar.durum === "dogru" ? "✔ " : karar.durum === "yanlis" ? "✘ " : ""}{g.baslik}
          </p>
          {karar.durum === "yanlis" && karar.algilanan && (
            <p className="mt-2 text-lg font-bold">
              Algılanan: {karar.algilanan.urunAdi} <span className="text-sm font-normal">
                (beklenen: {beklenenAd})</span>
            </p>
          )}
          {karar.durum === "dogru" && karar.algilanan && (
            <p className="mt-2 text-lg font-bold">{karar.algilanan.urunAdi}
              <span className="text-sm font-normal"> · güven %{Math.round(karar.algilanan.guven * 100)}</span>
            </p>
          )}
          <p className="mt-2 text-sm opacity-90">{g.ipucu}</p>
        </div>
      )}
      {beklenen === "" && profiller.length === 0 && (
        <p className="ipucu">Önce aşağıdan bir ürün öğretin.</p>
      )}
    </section>
  );
}
