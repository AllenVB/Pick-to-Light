"use client";

import { useCallback, useEffect, useState } from "react";
import { api } from "@/lib/api";
import { vision, type OgretilenProfil, type TaniYaniti, type VisionSaglik } from "@/lib/vision";
import { KameraGorunumu } from "@/components/KameraGorunumu";
import { OgretmeKarti } from "@/components/OgretmeKarti";
import { UrunDogrulama, type OnaylananUrun } from "@/components/UrunDogrulama";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import type { Urun } from "@/types";

/**
 * Kamera & Tanıma (yönetici).
 *
 * Akış: ürün kameraya gösterilir ve öğretilir → kamera ne gördüğünü tanır → baskül gram düşüşü,
 * tanınan ürünün birim ağırlığına bölünerek adede çevrilir (civata 10 g, somun 20 g:
 * 100 g düşüş = 10 civata ya da 5 somun). Aşağıdaki hesap kartı bunu canlı gösterir.
 */
export default function KameraSayfasi() {
  const oturum = useKorumaliSayfa("Admin");

  const [saglik, setSaglik] = useState<VisionSaglik | null | undefined>(undefined);
  const [tani, setTani] = useState<TaniYaniti | null>(null);
  const [profiller, setProfiller] = useState<OgretilenProfil[]>([]);
  const [urunler, setUrunler] = useState<Urun[]>([]);
  const [dusus, setDusus] = useState("100");
  const [donmusKare, setDonmusKare] = useState<string | null>(null);
  const [onaylanan, setOnaylanan] = useState<OnaylananUrun | null>(null);

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

  // Kaynak değişince sağlık ve tanı bilgisi 1,5 sn beklemeden tazelenir.
  const durumuYenile = useCallback(() => {
    vision.saglik().then(setSaglik);
    vision.tani().then(setTani).catch(() => setTani(null));
  }, []);

  if (!oturum) return null;

  const bagli = !!saglik;
  const baskin = tani?.baskin ?? null;
  // Ürün onaylandıysa adet hesabı onaylanan ürünle yapılır (kamera donmuş; canlı tanıma değişse de etkilenmez).
  const hesapUrunId = onaylanan?.urunId ?? baskin?.urunId;
  const baskinUrun = hesapUrunId !== undefined ? urunler.find((u) => u.id === hesapUrunId) : undefined;
  const hesapUrunAdi = onaylanan?.urunAdi ?? baskin?.urunAdi ?? null;
  const birim = baskinUrun?.birimAgirlikG ?? null;
  const dususG = Number(dusus.replace(",", "."));
  const adet = birim && dususG > 0 ? Math.round(dususG / birim) : null;

  return (
    <div className="space-y-6">
      <header>
        <p className="etiket">Görüntü İşleme</p>
        <h1 className="baslik mt-1">Kamera &amp; Tanıma</h1>
        <p className="mt-1 text-sm text-slate-400">
          Kamera hangi ürünün alındığını tanır; baskül gram düşüşü o ürünün birim ağırlığına bölünür.
          Aynı kutuda farklı ağırlıkta ürünler olsa bile adet doğru hesaplanır.
          Kamerayı bilgisayarın izniyle <b>tarayıcıdan</b> açabilirsiniz.
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
        <KameraGorunumu saglik={saglik} tani={tani} yenile={durumuYenile} dondurulmus={donmusKare} />

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

          <UrunDogrulama profiller={profiller} bagli={bagli} onDondur={setDonmusKare} onOnay={setOnaylanan} />

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
              {hesapUrunAdi
                ? birim
                  ? onaylanan
                    ? `${hesapUrunAdi} onaylandı; birim ağırlığı ${birim} g. Adet bu ürüne göre hesaplanır.`
                    : `${hesapUrunAdi} birim ağırlığı ${birim} g. Başka bir ürün tanınırsa adet ona göre değişir.`
                  : `${hesapUrunAdi} için birim ağırlık tanımlı değil (Ürünler sayfası).`
                : "Önce bir ürün tanınmalı."}
            </p>
          </div>
        </section>
      </div>

      <OgretmeKarti urunler={urunler} profiller={profiller} tani={tani} bagli={bagli} yenile={profilleriYenile} />
    </div>
  );
}
