"use client";

import { useCallback, useEffect, useState } from "react";
import { api } from "@/lib/api";
import { vision, type OgretilenProfil, type SahneNesnesi, type TaniYaniti, type VisionSaglik } from "@/lib/vision";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import { useToast } from "@/components/Toast";
import type { Urun } from "@/types";

const SAHNELER: { ad: string; nesneler: SahneNesnesi[] }[] = [
  { ad: "3 civata", nesneler: [{ tip: "civata", adet: 3 }] },
  { ad: "4 somun", nesneler: [{ tip: "somun", adet: 4 }] },
  { ad: "2 rondela", nesneler: [{ tip: "rondela", adet: 2 }] },
  { ad: "Karışık", nesneler: [{ tip: "civata", adet: 2 }, { tip: "somun", adet: 3 }] },
  { ad: "Boş masa", nesneler: [] },
];

/**
 * Kamera & Tanıma (yönetici).
 *
 * Akış: ürün kameraya gösterilir ve öğretilir → kamera ne gördüğünü tanır → baskül gram düşüşü,
 * tanınan ürünün birim ağırlığına bölünerek adede çevrilir (civata 10 g, somun 20 g:
 * 100 g düşüş = 10 civata ya da 5 somun). Aşağıdaki hesap kartı bunu canlı gösterir.
 */
export default function KameraSayfasi() {
  const oturum = useKorumaliSayfa("Admin");
  const toast = useToast();

  const [saglik, setSaglik] = useState<VisionSaglik | null | undefined>(undefined);
  const [tani, setTani] = useState<TaniYaniti | null>(null);
  const [profiller, setProfiller] = useState<OgretilenProfil[]>([]);
  const [urunler, setUrunler] = useState<Urun[]>([]);
  const [secili, setSecili] = useState<number | "">("");
  const [dusus, setDusus] = useState("100");
  const [islemde, setIslemde] = useState(false);

  const profilleriYenile = useCallback(() => {
    vision.profiller().then((p) => setProfiller(p ?? []));
  }, []);

  useEffect(() => {
    if (!oturum) return;
    api.urunler().then(setUrunler).catch(() => undefined);
    vision.saglik().then(setSaglik);
    profilleriYenile();
  }, [oturum, profilleriYenile]);

  // Tanıma sonucu 1,5 sn'de bir tazelenir; servis kapanırsa durum da güncellenir.
  useEffect(() => {
    if (!oturum) return;
    const t = setInterval(async () => {
      const s = await vision.saglik();
      setSaglik(s);
      setTani(s ? await vision.tani().catch(() => null) : null);
    }, 1500);
    return () => clearInterval(t);
  }, [oturum]);

  async function ogret() {
    const urun = urunler.find((u) => u.id === secili);
    if (!urun) return;
    setIslemde(true);
    try {
      const sonuc = await vision.ogret(urun.id, urun.ad);
      if (sonuc) toast(`${urun.ad}: ${sonuc.eklenenOrnek} örnek eklendi (toplam ${sonuc.toplamOrnek}).`, "basari");
      else toast("Görüntü servisine ulaşılamadı.", "hata");
      profilleriYenile();
    } catch (h) {
      toast(h instanceof Error ? h.message : "Öğretme başarısız.", "hata");
    } finally {
      setIslemde(false);
    }
  }

  async function sil(profil: OgretilenProfil) {
    if (!confirm(`${profil.ad}: öğretilen ${profil.ornekSayisi} örnek silinsin mi?`)) return;
    await vision.ogretileniSil(profil.urunId).catch(() => null);
    profilleriYenile();
  }

  async function sahne(nesneler: SahneNesnesi[]) {
    try {
      await vision.simSahne(nesneler);
    } catch (h) {
      toast(h instanceof Error ? h.message : "Sahne ayarlanamadı.", "hata");
    }
  }

  if (!oturum) return null;

  const bagli = !!saglik;
  const baskin = tani?.baskin ?? null;
  const baskinUrun = baskin ? urunler.find((u) => u.id === baskin.urunId) : undefined;
  const birim = baskinUrun?.birimAgirlikG ?? null;
  const dususG = Number(dusus.replace(",", "."));
  const adet = birim && dususG > 0 ? Math.round(dususG / birim) : null;
  const secilenProfil = profiller.find((p) => p.urunId === secili);

  return (
    <div className="space-y-6">
      <header>
        <p className="etiket">Görüntü İşleme</p>
        <h1 className="baslik mt-1">Kamera &amp; Tanıma</h1>
        <p className="mt-1 text-sm text-slate-400">
          Kamera hangi ürünün alındığını tanır; baskül gram düşüşü o ürünün birim ağırlığına bölünür.
          Aynı kutuda farklı ağırlıkta ürünler olsa bile adet doğru hesaplanır.
        </p>
      </header>

      {saglik === null && (
        <p className="rounded-lg border border-durum-sari/40 bg-durum-sari/10 px-4 py-3 text-sm text-durum-sari"
          data-test="vision-kapali">
          Görüntü işleme servisine ulaşılamıyor. Servis kapalıyken sistem yalnızca baskülle çalışmaya devam eder.
          Başlatmak için: <code>vision\.venv\Scripts\python -m uvicorn app:app --port 5100</code>
        </p>
      )}

      <div className="grid gap-4 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        {/* Canlı görüntü */}
        <section className="kart space-y-3">
          <div className="flex items-center justify-between">
            <h2 className="etiket">Canlı görüntü</h2>
            <span className={`rozet ${bagli ? "bg-durum-yesil/15 text-durum-yesil" : "bg-slate-600/20 text-slate-400"}`}>
              ● {bagli ? (saglik.sanal ? "SANAL SAHNE" : "KAMERA") : "BAĞLANTI YOK"}
            </span>
          </div>

          <div className="relative aspect-[4/3] w-full overflow-hidden rounded-lg border border-zemin-cizgi bg-black/40">
            {bagli ? (
              // eslint-disable-next-line @next/next/no-img-element
              <img src={vision.onizlemeUrl} alt="Kamera önizlemesi" className="h-full w-full object-contain"
                data-test="kamera-onizleme" />
            ) : (
              <div className="flex h-full items-center justify-center text-sm text-slate-500">görüntü yok</div>
            )}
          </div>

          {bagli && !saglik.sanal && (
            <p className="ipucu" data-test="kamera-ipucu">
              Görüntüde &quot;kamera yok&quot; simgesi çıkıyorsa Windows Ayarlar → Gizlilik → Kamera iznini ve kameranın
              başka bir uygulamada açık olmadığını kontrol edin. Kamerasız denemek için servisi{" "}
              <code>VISION_KAYNAK=sim</code> ile başlatın.
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

        {/* Tespit */}
        <section className="space-y-4">
          <div className="kart" data-test="tespit-karti">
            <h2 className="etiket">Tespit edilen ürün</h2>
            {baskin ? (
              <div className="mt-2">
                <p className="text-3xl font-extrabold leading-tight">{baskin.urunAdi}</p>
                <p className="mt-1 text-sm text-slate-400">
                  güven <b className="text-slate-200">%{Math.round(baskin.guven * 100)}</b> · kamerada{" "}
                  <b className="text-slate-200">{baskin.adet}</b> adet görünüyor
                </p>
              </div>
            ) : (
              <p className="mt-2 text-sm text-slate-500">
                {bagli ? "Tanınan ürün yok. Ürünü kameranın önüne koyun." : "—"}
              </p>
            )}
            {tani && tani.tespitler.length > baskin?.adet! && (
              <p className="ipucu mt-2">
                {tani.tespitler.filter((t) => t.urunId === null).length} nesne tanınamadı (bilinmiyor)
              </p>
            )}
          </div>

          {/* Hesap: tanınan ürün + gram düşüşü → adet */}
          <div className="kart space-y-3" data-test="adet-hesabi">
            <h2 className="etiket">Baskül → adet hesabı</h2>
            <div className="flex items-end gap-3">
              <div className="flex-1">
                <label className="etiket">Gram düşüşü</label>
                <input className="girdi mt-1" inputMode="decimal" value={dusus}
                  onChange={(e) => setDusus(e.target.value)} />
              </div>
              <p className="pb-2 text-slate-500">÷</p>
              <div className="flex-1">
                <p className="etiket">Birim ağırlık</p>
                <p className="mt-2 text-lg font-bold">{birim ? `${birim} g` : "—"}</p>
              </div>
              <p className="pb-2 text-slate-500">=</p>
              <div className="flex-1">
                <p className="etiket">Adet</p>
                <p className="mt-1 text-3xl font-extrabold text-marka-metin" data-test="hesaplanan-adet">
                  {adet ?? "—"}
                </p>
              </div>
            </div>
            <p className="ipucu">
              {baskin
                ? birim
                  ? `${baskin.urunAdi} birim ağırlığı ${birim} g. Başka bir ürün tanınırsa adet ona göre değişir.`
                  : `${baskin.urunAdi} için birim ağırlık tanımlı değil (Ürünler sayfası).`
                : "Önce bir ürün tanınmalı."}
            </p>
          </div>
        </section>
      </div>

      {/* Öğretme */}
      <section className="kart space-y-4">
        <div>
          <h2 className="etiket">Ürün öğret</h2>
          <p className="mt-1 text-sm text-slate-400">
            Ürünü düz zemine, kameranın önüne koyun ve ürünü seçip <b>Örnek ekle</b>ye basın. Farklı açılardan
            3–5 örnek yeterlidir. Karede yalnızca öğretilen ürün bulunmalı.
          </p>
        </div>

        <div className="flex flex-wrap items-end gap-3">
          <div className="min-w-56">
            <label className="etiket">Ürün</label>
            <select className="girdi mt-1" value={secili}
              onChange={(e) => setSecili(e.target.value ? Number(e.target.value) : "")} data-test="ogret-urun">
              <option value="">Ürün seçin…</option>
              {urunler.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.ad}{u.birimAgirlikG ? ` (${u.birimAgirlikG} g)` : " (birim ağırlık yok)"}
                </option>
              ))}
            </select>
          </div>
          <button className="dugme" disabled={!secili || !bagli || islemde} onClick={ogret} data-test="ogret-ekle">
            {islemde ? "Ekleniyor…" : "Örnek ekle"}
          </button>
          {secilenProfil && (
            <p className="ipucu pb-2">{secilenProfil.ornekSayisi} örnek öğretilmiş</p>
          )}
        </div>

        <table className="tablo">
          <thead><tr><th>Ürün</th><th>Öğretilen örnek</th><th /></tr></thead>
          <tbody>
            {profiller.length === 0 && (
              <tr><td colSpan={3} className="text-slate-500">Henüz ürün öğretilmedi.</td></tr>
            )}
            {profiller.map((p) => (
              <tr key={p.urunId} data-test="profil-satiri">
                <td className="font-semibold">{p.ad}</td>
                <td>{p.ornekSayisi}</td>
                <td className="text-right">
                  <button className="dugme-tehlike dugme-kucuk" onClick={() => sil(p)}>Sil</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </div>
  );
}
