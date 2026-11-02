"use client";

import { useCallback, useEffect, useState } from "react";
import { ApiHata, api } from "@/lib/api";
import { vision, type TaniYaniti, type VisionSaglik } from "@/lib/vision";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import { useToast } from "@/components/Toast";
import { KameraGorunumu } from "@/components/KameraGorunumu";
import { canliKutular, gramMetni, useCanliAgirlik } from "@/components/Baskul";
import type { Kutu, TartiSonucu, Urun } from "@/types";

interface Islem extends TartiSonucu {
  tur: "azalt" | "ekle";
  zaman: string;
  agirlikG: number;
}

/**
 * Tartı & Sayım (yönetici).
 *
 * Paketi tartıya koy → kamera ürünü tanır → tartıdaki gram ürünün birim ağırlığına bölünür
 * (20 g ÷ 2 g vida = 10 adet) → "Stoktan azalt" deyince o adet stoktan düşer (mal kabulde "Stoğa ekle").
 * Tartı değeri, seçilen kutunun baskülünden canlı gelir; tartı yoksa gram elle girilir.
 */
export default function TartiSayimSayfasi() {
  const oturum = useKorumaliSayfa("Admin");
  const toast = useToast();

  const [hamKutular, setKutular] = useState<Kutu[]>([]);
  const kutular = canliKutular(hamKutular, useCanliAgirlik());
  const [urunler, setUrunler] = useState<Urun[]>([]);
  const [saglik, setSaglik] = useState<VisionSaglik | null | undefined>(undefined);
  const [tani, setTani] = useState<TaniYaniti | null>(null);

  // Varsayılan: gram elle girilir. Bir kutunun baskülü seçilirse o kutunun TOPLAM içeriği ölçülür; paketi
  // tartmadan önce "Tartıyı sıfırla" ile kutunun kendi ağırlığı düşülmelidir.
  const [tartiKutuId, setTartiKutuId] = useState<number | "elle">("elle");
  const [elleGram, setElleGram] = useState("");
  const [tare, setTare] = useState(0);
  const [urunSecimi, setUrunSecimi] = useState<number | "">("");   // "" = kameranın tanıdığı ürün
  const [hedefKutuId, setHedefKutuId] = useState<number | "">("");
  const [islemde, setIslemde] = useState(false);
  const [islemler, setIslemler] = useState<Islem[]>([]);

  const kutulariYenile = useCallback(() => {
    api.kutular().then(setKutular).catch(() => undefined);
  }, []);

  const durumuYenile = useCallback(() => {
    vision.saglik().then(setSaglik);
    vision.tani().then(setTani).catch(() => setTani(null));
  }, []);

  useEffect(() => {
    if (!oturum) return;
    api.urunler().then(setUrunler).catch(() => undefined);
    kutulariYenile();
    durumuYenile();
  }, [oturum, kutulariYenile, durumuYenile]);

  useEffect(() => {
    if (!oturum) return;
    const t = setInterval(async () => {
      const s = await vision.saglik();
      setSaglik(s);
      setTani(s ? await vision.tani().catch(() => null) : null);
    }, 1500);
    return () => clearInterval(t);
  }, [oturum]);

  // Baskülü olan kutular tartı kaynağı olabilir.
  const tartiliKutular = kutular.filter((k) => k.agirlikG != null);

  if (!oturum) return null;

  // ── Hesap ──
  const elle = tartiKutuId === "elle";
  const tartiKutu = elle ? undefined : kutular.find((k) => k.id === tartiKutuId);
  const brut = elle
    ? (elleGram.trim() === "" ? null : Number(elleGram.replace(",", ".")))
    : tartiKutu?.agirlikG ?? null;
  const net = brut != null && Number.isFinite(brut) ? brut - (elle ? 0 : tare) : null;

  const baskin = tani?.baskin ?? null;
  const urunId = urunSecimi !== "" ? urunSecimi : baskin?.urunId ?? null;
  const urun = urunler.find((u) => u.id === urunId);
  const birim = urun?.birimAgirlikG && urun.birimAgirlikG > 0 ? urun.birimAgirlikG : null;

  const adetHam = birim && net != null && net > 0 ? net / birim : null;
  const adet = adetHam != null ? Math.round(adetHam) : null;
  // Gram tam sayıda parçaya denk gelmiyorsa (ör. 9,6 parça) ölçüm ya da ürün seçimi şüphelidir.
  const tamDegil = adetHam != null && Math.abs(adetHam - Math.round(adetHam)) > 0.2;

  // Kamera karedeki parçaları da sayar: tartıdan çıkan adetle karşılaştırılır (çapraz doğrulama).
  const kameraAdedi = baskin && urunId === baskin.urunId ? tani?.toplamAdet ?? null : null;
  const adetFarkli = adet != null && kameraAdedi != null && kameraAdedi !== adet;

  // Ürünü tutan kutular/bölmeler: stok oradan düşer ya da oraya eklenir.
  const hedefler: { id: number; kod: string; adet: number }[] = urunId == null ? [] : kutular.flatMap((k) =>
    k.bolmeler
      ? k.bolmeler.filter((b) => b.urunId === urunId).map((b) => ({ id: b.id, kod: b.kod, adet: b.adet }))
      : k.urunId === urunId ? [{ id: k.id, kod: k.kod, adet: k.adet }] : []);

  const hazir = !!urun && adet != null && adet > 0;

  async function uygula(tur: "azalt" | "ekle") {
    if (!urun || adet == null || adet <= 0 || net == null) return;
    setIslemde(true);
    try {
      const istek = {
        urunId: urun.id, adet, agirlikG: Math.round(net * 10) / 10,
        kutuId: hedefKutuId === "" ? undefined : hedefKutuId,
      };
      const sonuc = tur === "azalt" ? await api.tartiAzalt(istek) : await api.tartiEkle(istek);
      setIslemler((onceki) => [{ ...sonuc, tur, zaman: new Date().toLocaleTimeString("tr-TR"), agirlikG: istek.agirlikG }, ...onceki].slice(0, 8));
      toast(`${sonuc.kutuKod} · ${sonuc.urunAdi}: ${tur === "azalt" ? "−" : "+"}${sonuc.adet} adet → stok ${sonuc.yeniStok}`, "basari");
      kutulariYenile();
    } catch (h) {
      toast(h instanceof ApiHata ? h.message : "İşlem başarısız.", "hata");
    } finally {
      setIslemde(false);
    }
  }

  return (
    <div className="space-y-6">
      <header>
        <p className="etiket">Görüntü İşleme + Baskül</p>
        <h1 className="baslik mt-1">Tartı &amp; Sayım</h1>
        <p className="mt-1 text-sm text-slate-400">
          Paketi tartıya koyun: kamera ürünü tanır, tartıdaki gram ürünün birim ağırlığına bölünür ve adet bulunur.
          Örnek: 20 g ÷ 2 g (vida) = 10 adet. Onaylayınca stoktan düşer.
        </p>
      </header>

      <div className="grid gap-4 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <KameraGorunumu saglik={saglik} tani={tani} yenile={durumuYenile} />

        <div className="space-y-4">
          {/* Tartı */}
          <section className="kart space-y-3" data-test="tarti-karti">
            <h2 className="etiket">Tartı</h2>

            <div>
              <label className="etiket">Tartı kaynağı</label>
              <select className="girdi mt-1" value={tartiKutuId} data-test="tarti-kaynak"
                onChange={(e) => { setTartiKutuId(e.target.value === "elle" ? "elle" : Number(e.target.value)); setTare(0); }}>
                <option value="elle">Gramı elle gireceğim</option>
                {tartiliKutular.map((k) => (
                  <option key={k.id} value={k.id}>{k.kod} baskülü (şu an {gramMetni(k.agirlikG ?? 0)})</option>
                ))}
              </select>
            </div>

            {elle ? (
              <div>
                <label className="etiket">Tartıdaki ağırlık (g)</label>
                <input className="girdi mt-1" inputMode="decimal" placeholder="örn. 20" value={elleGram}
                  onChange={(e) => setElleGram(e.target.value)} data-test="tarti-elle" />
              </div>
            ) : (
              <p className="ipucu">
                Kutu baskülü içindeki <b>tüm</b> parçaları tartar. Paketi koymadan önce <b>Tartıyı sıfırla</b>ya basın,
                sonra paketi koyun; net ağırlık paketin ağırlığı olur.
              </p>
            )}

            <div className="flex items-end justify-between gap-3 border-t border-zemin-cizgi/60 pt-3">
              <div>
                <p className="etiket">Net ağırlık</p>
                <p className="mt-1 font-mono text-3xl font-extrabold tabular-nums" data-test="tarti-net">
                  {net != null ? gramMetni(net) : "—"}
                </p>
              </div>
              <button className="dugme-ikincil dugme-kucuk" disabled={elle || brut == null}
                hidden={elle}
                title="Tartı boşken basın: şu anki ağırlık sıfır kabul edilir (kap/kutu ağırlığı düşülür)"
                onClick={() => setTare(tartiKutu?.agirlikG ?? 0)} data-test="tarti-sifirla">
                Tartıyı sıfırla
              </button>
            </div>
            {!elle && tare > 0 && <p className="ipucu">Dara: {gramMetni(tare)} düşüldü.</p>}
          </section>

          {/* Sonuç */}
          <section className="kart space-y-3" data-test="tarti-sonuc">
            <h2 className="etiket">Sayım sonucu</h2>

            <div>
              <label className="etiket">Ürün</label>
              <select className="girdi mt-1" value={urunSecimi} data-test="tarti-urun-sec"
                onChange={(e) => setUrunSecimi(e.target.value ? Number(e.target.value) : "")}>
                <option value="">
                  {baskin ? `Kamera: ${baskin.urunAdi} (%${Math.round(baskin.guven * 100)})` : "Kamera ürün tanımadı — elle seçin"}
                </option>
                {urunler.map((u) => (
                  <option key={u.id} value={u.id}>{u.ad}{u.birimAgirlikG ? ` (${u.birimAgirlikG} g)` : ""}</option>
                ))}
              </select>
            </div>

            <div className="flex items-end gap-3">
              <div className="flex-1">
                <p className="etiket">Net gram</p>
                <p className="mt-1 text-lg font-bold">{net != null ? gramMetni(net) : "—"}</p>
              </div>
              <p className="pb-1 text-slate-500">÷</p>
              <div className="flex-1">
                <p className="etiket">Birim ağırlık</p>
                <p className="mt-1 text-lg font-bold">{birim ? `${birim} g` : "—"}</p>
              </div>
              <p className="pb-1 text-slate-500">=</p>
              <div className="flex-1">
                <p className="etiket">Adet</p>
                <p className="mt-1 text-4xl font-extrabold text-marka-metin" data-test="tarti-adet">{adet ?? "—"}</p>
              </div>
            </div>

            {urun && !birim && (
              <p className="text-sm text-durum-sari">{urun.ad} için birim ağırlık tanımlı değil (Ürünler sayfası).</p>
            )}
            {tamDegil && adetHam != null && (
              <p className="text-sm text-durum-sari" data-test="tarti-uyari-tam">
                Ağırlık tam sayıda parçaya denk gelmiyor ({adetHam.toLocaleString("tr-TR", { maximumFractionDigits: 1 })} adet):
                ürün seçimini ve tartıyı kontrol edin.
              </p>
            )}
            {adetFarkli && (
              <p className="text-sm text-durum-sari" data-test="tarti-uyari-kamera">
                Kamera karede {kameraAdedi} parça görüyor, tartı {adet} adet diyor. Parçaların üst üste durmadığından emin olun.
              </p>
            )}
            {adet != null && kameraAdedi != null && !adetFarkli && !tamDegil && (
              <p className="text-sm text-durum-yesil">Kamera sayımı da tartıyla aynı ({kameraAdedi} parça).</p>
            )}

            {urun && hedefler.length > 0 && (
              <div>
                <label className="etiket">Kutu / bölme</label>
                <select className="girdi mt-1" value={hedefKutuId} data-test="tarti-hedef"
                  onChange={(e) => setHedefKutuId(e.target.value ? Number(e.target.value) : "")}>
                  <option value="">Otomatik (en dolu kutu)</option>
                  {hedefler.map((h) => <option key={h.id} value={h.id}>{h.kod} (stok {h.adet})</option>)}
                </select>
              </div>
            )}

            <div className="flex flex-wrap gap-2 pt-1">
              <button className="dugme" disabled={!hazir || islemde} onClick={() => uygula("azalt")} data-test="tarti-azalt">
                {adet ? `Stoktan azalt (−${adet})` : "Stoktan azalt"}
              </button>
              <button className="dugme-ikincil" disabled={!hazir || islemde} onClick={() => uygula("ekle")} data-test="tarti-ekle">
                {adet ? `Stoğa ekle (+${adet})` : "Stoğa ekle"}
              </button>
            </div>
          </section>
        </div>
      </div>

      {islemler.length > 0 && (
        <section className="kart" data-test="tarti-islemler">
          <h2 className="etiket mb-2">Bu oturumdaki işlemler</h2>
          <table className="tablo">
            <thead><tr><th>Saat</th><th>İşlem</th><th>Kutu</th><th>Ürün</th><th>Gram</th><th>Adet</th><th>Stok</th></tr></thead>
            <tbody>
              {islemler.map((i, n) => (
                <tr key={n}>
                  <td className="text-xs text-slate-400">{i.zaman}</td>
                  <td className={i.tur === "azalt" ? "text-durum-kirmizi" : "text-durum-yesil"}>
                    {i.tur === "azalt" ? "Stoktan azaltıldı" : "Stoğa eklendi"}
                  </td>
                  <td className="font-mono text-xs">{i.kutuKod}</td>
                  <td>{i.urunAdi}</td>
                  <td className="tabular-nums">{gramMetni(i.agirlikG)}</td>
                  <td className="font-bold tabular-nums">{i.tur === "azalt" ? "−" : "+"}{i.adet}</td>
                  <td className="tabular-nums">{i.oncekiStok} → <b>{i.yeniStok}</b></td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}
    </div>
  );
}
