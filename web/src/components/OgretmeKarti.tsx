"use client";

import { useState } from "react";
import { vision, type OgretilenProfil, type TaniYaniti } from "@/lib/vision";
import { useToast } from "@/components/Toast";
import type { Urun } from "@/types";

/** Güvenilir tanıma için önerilen çekim sayısı; bunun altı "yetersiz" sayılır. */
const HEDEF_CEKIM = 5;
const EN_AZ_CEKIM = 3;

/** Her çekimde ürünü farklı göstermek için yönerge (açı ve konum çeşitliliği profili sağlamlaştırır). */
const YONERGE = [
  "Ürünü düz zemine, kameranın tam ortasına koyun.",
  "Ürünü ters çevirin (arka yüzü görünsün).",
  "Ürünü yan yatırın.",
  "Ürünü kameranın biraz sağına/soluna kaydırın.",
  "Ürünü 90° döndürün ya da kameraya biraz yaklaştırın.",
];

type Hazirlik = { ok: boolean; metin: string };

function hazirlik(tani: TaniYaniti | null, bagli: boolean): Hazirlik {
  if (!bagli) return { ok: false, metin: "Görüntü servisine ulaşılamıyor." };
  const n = tani?.tespitler.length ?? 0;
  if (n === 0) return { ok: false, metin: "Kamerada nesne yok. Ürünü kameranın önüne koyun." };
  return { ok: true, metin: n === 1 ? "1 nesne görünüyor, öğretmeye hazır." : `${n} nesne görünüyor; hepsi aynı ürün olmalı.` };
}

function durumEtiketi(cekim: number): { metin: string; sinif: string } {
  if (cekim === 0) return { metin: "Öğretilmedi", sinif: "text-slate-500" };
  if (cekim < EN_AZ_CEKIM) return { metin: "Yetersiz", sinif: "text-durum-sari" };
  return { metin: "Hazır", sinif: "text-durum-yesil" };
}

/**
 * Ürün öğretme: ürün seç → kameraya göster → "Örnek ekle" (yönerge her çekimde değişir) →
 * ilerleme çubuğu hedefe ulaşınca ürün hazır. Yanlış çekim "Son çekimi geri al" ile silinir;
 * "Şu an kamerada" satırı öğretilenin tanınıp tanınmadığını anında gösterir.
 */
export function OgretmeKarti({ urunler, profiller, tani, bagli, yenile }: {
  urunler: Urun[];
  profiller: OgretilenProfil[];
  tani: TaniYaniti | null;
  bagli: boolean;
  yenile: () => void;
}) {
  const toast = useToast();
  const [secili, setSecili] = useState<number | "">("");
  const [islemde, setIslemde] = useState(false);

  const urun = urunler.find((u) => u.id === secili);
  const profil = profiller.find((p) => p.urunId === secili);
  const cekim = profil?.cekimSayisi ?? 0;
  const hazir = hazirlik(tani, bagli);
  const yuzde = Math.min(100, Math.round((cekim / HEDEF_CEKIM) * 100));
  const baskin = tani?.baskin ?? null;
  const tanindi = !!urun && baskin?.urunId === urun.id;

  async function ogret() {
    if (!urun) return;
    setIslemde(true);
    try {
      const sonuc = await vision.ogret(urun.id, urun.ad);
      if (!sonuc) toast("Görüntü servisine ulaşılamadı.", "hata");
      else toast(`${urun.ad}: çekim ${sonuc.cekimSayisi}/${HEDEF_CEKIM} eklendi.`, "basari");
      yenile();
    } catch (h) {
      toast(h instanceof Error ? h.message : "Öğretme başarısız.", "hata");
    } finally {
      setIslemde(false);
    }
  }

  async function geriAl() {
    if (!urun) return;
    setIslemde(true);
    try {
      const sonuc = await vision.ogretmeyiGeriAl(urun.id);
      toast(sonuc ? "Son çekim geri alındı." : "Geri alınacak çekim yok.", sonuc ? "basari" : "hata");
      yenile();
    } catch (h) {
      toast(h instanceof Error ? h.message : "Geri alınamadı.", "hata");
    } finally {
      setIslemde(false);
    }
  }

  async function sil(p: OgretilenProfil) {
    if (!confirm(`${p.ad}: öğretilen tüm örnekler (${p.cekimSayisi} çekim) silinsin mi?`)) return;
    await vision.ogretileniSil(p.urunId).catch(() => null);
    yenile();
  }

  return (
    <section className="kart space-y-4" data-test="ogretme-karti">
      <div>
        <h2 className="etiket">Ürün öğret</h2>
        <p className="mt-1 text-sm text-slate-400">
          Ürünü seçin, kameraya gösterin ve <b>Örnek ekle</b>ye basın. Her çekimde ürünü farklı gösterin;
          {" "}{HEDEF_CEKIM} çekim sonunda ürün hazır olur. Karede yalnızca öğretilen ürün bulunmalı.
        </p>
      </div>

      <div className="grid gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]">
        {/* 1) Ürün listesi: durum tek bakışta */}
        <div>
          <p className="etiket">1 · Ürün seçin</p>
          <ul className="mt-2 divide-y divide-zemin-cizgi rounded-lg border border-zemin-cizgi">
            {urunler.length === 0 && <li className="px-3 py-2 text-sm text-slate-500">Ürün bulunamadı.</li>}
            {urunler.map((u) => {
              const p = profiller.find((x) => x.urunId === u.id);
              const d = durumEtiketi(p?.cekimSayisi ?? 0);
              return (
                <li key={u.id}>
                  <button type="button" onClick={() => setSecili(u.id)} data-test="ogret-urun-secenek" data-urun={u.ad}
                    className={`flex w-full items-center justify-between gap-2 px-3 py-2 text-left text-sm ${
                      secili === u.id ? "bg-zemin-kart font-bold" : "hover:bg-zemin-kart"}`}>
                    <span>{u.ad}</span>
                    <span className={`text-xs font-semibold ${d.sinif}`}>{d.metin}</span>
                  </button>
                </li>
              );
            })}
          </ul>
        </div>

        {/* 2) Öğretme adımı */}
        <div className="space-y-3">
          <p className="etiket">2 · Kameraya gösterin ve ekleyin</p>
          {!urun ? (
            <p className="text-sm text-slate-500">Soldan öğretmek istediğiniz ürünü seçin.</p>
          ) : (
            <>
              <div>
                <div className="flex items-baseline justify-between text-sm">
                  <span className="font-bold">{urun.ad}</span>
                  <span className="ipucu" data-test="ogret-ilerleme">{cekim} / {HEDEF_CEKIM} çekim</span>
                </div>
                <div className="mt-1 h-2 overflow-hidden rounded-full bg-zemin-cizgi">
                  <div className={`h-full ${cekim >= EN_AZ_CEKIM ? "bg-durum-yesil" : "bg-durum-sari"}`}
                    style={{ width: `${yuzde}%` }} />
                </div>
              </div>

              <p className="rounded-lg bg-zemin-kart px-3 py-2 text-sm" data-test="ogret-yonerge">
                <b>Sıradaki:</b> {YONERGE[Math.min(cekim, YONERGE.length - 1)]}
              </p>

              <p className={`text-sm font-semibold ${hazir.ok ? "text-durum-yesil" : "text-durum-sari"}`}
                data-test="ogret-hazirlik">
                {hazir.ok ? "✔" : "•"} {hazir.metin}
              </p>

              <div className="flex flex-wrap gap-2">
                <button className="dugme" disabled={!hazir.ok || islemde} onClick={ogret} data-test="ogret-ekle">
                  {islemde ? "Ekleniyor…" : "Örnek ekle"}
                </button>
                <button className="dugme-ikincil" disabled={cekim === 0 || islemde} onClick={geriAl}
                  data-test="ogret-geri-al">Son çekimi geri al</button>
              </div>

              {/* 3) Anında deneme */}
              <div className="rounded-lg border border-zemin-cizgi p-3 text-sm" data-test="ogret-deneme">
                <p className="etiket">3 · Şu an kamerada</p>
                {!baskin ? (
                  <p className="mt-1 text-slate-500">Tanınan ürün yok.</p>
                ) : (
                  <p className={`mt-1 font-semibold ${tanindi ? "text-durum-yesil" : "text-durum-sari"}`}>
                    {tanindi ? "✔ " : "≠ "}{baskin.urunAdi} (%{Math.round(baskin.guven * 100)})
                    {!tanindi && <span className="ipucu ml-2">seçili ürün değil</span>}
                  </p>
                )}
              </div>
            </>
          )}
        </div>
      </div>

      <table className="tablo">
        <thead><tr><th>Öğretilmiş ürün</th><th>Çekim</th><th>Örnek</th><th /></tr></thead>
        <tbody>
          {profiller.length === 0 && (
            <tr><td colSpan={4} className="text-slate-500">Henüz ürün öğretilmedi.</td></tr>
          )}
          {profiller.map((p) => (
            <tr key={p.urunId} data-test="profil-satiri">
              <td className="font-semibold">{p.ad}</td>
              <td>{p.cekimSayisi}</td>
              <td>{p.ornekSayisi}</td>
              <td className="text-right">
                <button className="dugme-tehlike dugme-kucuk" onClick={() => sil(p)}>Sil</button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}
