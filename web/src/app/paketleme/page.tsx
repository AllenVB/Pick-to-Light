"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { ApiHata, api } from "@/lib/api";
import { VISION_URL, vision, type TaniYaniti, type VisionSaglik } from "@/lib/vision";
import { useCanliOlaylar } from "@/hooks/useCanliOlaylar";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import { useToast } from "@/components/Toast";
import { KameraGorunumu } from "@/components/KameraGorunumu";
import { TartiGirdisiKarti, useTartiGirdisi } from "@/components/TartiGirdisi";
import { canliKutular, gramMetni, useCanliAgirlik } from "@/components/Baskul";
import { OncelikRozeti } from "@/components/Rozet";
import type { DogrulamaSatiri, DogrulamaSonucu, Kutu, PaketlemeSonucu, PaketlenecekSiparis } from "@/types";

const DURUM_SINIF: Record<DogrulamaSatiri["durum"], string> = {
  Tamam: "bg-durum-yesil/15 text-durum-yesil",
  Eksik: "bg-durum-sari/20 text-durum-sari",
  Fazla: "bg-durum-sari/20 text-durum-sari",
  Yanlis: "bg-durum-kirmizi/20 text-durum-kirmizi",
  Kontrolsuz: "bg-slate-600/25 text-slate-400",
};
const DURUM_METIN: Record<DogrulamaSatiri["durum"], string> = {
  Tamam: "✓ TAMAM", Eksik: "EKSİK", Fazla: "FAZLA", Yanlis: "YANLIŞ ÜRÜN", Kontrolsuz: "KONTROLSÜZ",
};

/**
 * Paketleme & Doğrulama.
 *
 * Toplanan sipariş paketlenmeden önce içeriği doğrulanır: kamera ürün başına adet sayar, tartı toplam gramı
 * ölçer, sunucu ikisini siparişle karşılaştırır. Her şey tutarsa onay verilip sipariş PAKETLENDİ olur; tutmazsa
 * hata mesajları (eksik, fazla, yanlış ürün, tanınmayan nesne, ağırlık sapması) gösterilir ve paketlenmez.
 * Ekranın yaptığı canlı önizleme sunucudaki aynı kuralı çalıştırır (kayıt ve alarm üretmez).
 */
export default function PaketlemeSayfasi() {
  const oturum = useKorumaliSayfa();
  const yonetici = oturum?.rol === "Admin";
  const toast = useToast();

  const [hamKutular, setKutular] = useState<Kutu[]>([]);
  const kutular = canliKutular(hamKutular, useCanliAgirlik());
  const [kuyruk, setKuyruk] = useState<PaketlenecekSiparis[] | null>(null);
  const [seciliNo, setSeciliNo] = useState<string | null>(null);
  const [saglik, setSaglik] = useState<VisionSaglik | null | undefined>(undefined);
  const [tani, setTani] = useState<TaniYaniti | null>(null);

  const tarti = useTartiGirdisi(kutular);
  const [onizleme, setOnizleme] = useState<DogrulamaSonucu | null>(null);
  const [onizlemeHatasi, setOnizlemeHatasi] = useState<string | null>(null);
  const [karar, setKarar] = useState<DogrulamaSonucu | null>(null);
  const [paketlendi, setPaketlendi] = useState<PaketlemeSonucu | null>(null);
  const [islemde, setIslemde] = useState(false);

  // Otomatik doğrulama: önizleme yeşile dönünce kare yakalanır, görüntü dondurulur ve doğrulama kaydedilir.
  const [otomatik, setOtomatik] = useState(true);
  // Tartı yokken/kullanılamazken: ağırlık kontrolü yapılmaz, doğrulama yalnızca kamera görüntüsüyle kaydedilir.
  const [sadeceGoruntu, setSadeceGoruntu] = useState(false);
  const [donmusKare, setDonmusKare] = useState<string | null>(null);
  const yesilSayac = useRef(0);
  const sogumaBitis = useRef(0);
  const otomatikCalis = useRef<() => void>(() => undefined);

  // Önizleme döngüsü her tartı titreşiminde yeniden kurulmasın diye güncel girdiler ref'te tutulur.
  const girdi = useRef({ net: tarti.net, kap: tarti.kapG, sadeceGoruntu });
  girdi.current = { net: tarti.net, kap: tarti.kapG, sadeceGoruntu };

  const kuyruguYenile = useCallback(() => {
    api.paketlenecek().then(setKuyruk).catch(() => setKuyruk([]));
  }, []);

  const durumuYenile = useCallback(() => {
    vision.saglik().then(setSaglik);
    vision.tani().then(setTani).catch(() => setTani(null));
  }, []);

  useEffect(() => {
    if (!oturum) return;
    api.kutular().then(setKutular).catch(() => undefined);
    kuyruguYenile();
    durumuYenile();
    // Sipariş ekranından "Paketle" bağlantısıyla gelindiyse o sipariş seçilir.
    const istenen = new URLSearchParams(window.location.search).get("siparis");
    if (istenen) setSeciliNo(istenen);
  }, [oturum, kuyruguYenile, durumuYenile]);

  useCanliOlaylar({ SiparisDurumDegisti: kuyruguYenile, KalemTeyitEdildi: kuyruguYenile });

  useEffect(() => {
    if (!oturum) return;
    const t = setInterval(async () => {
      const s = await vision.saglik();
      setSaglik(s);
      setTani(s ? await vision.tani().catch(() => null) : null);
    }, 1500);
    return () => clearInterval(t);
  }, [oturum]);

  // Seçili sipariş yoksa (ya da paketlenip kuyruktan çıktıysa) ilk sıradaki seçilir.
  useEffect(() => {
    if (!kuyruk) return;
    if (seciliNo && kuyruk.some((k) => k.siparisNo === seciliNo)) return;
    if (paketlendi) return;
    setSeciliNo(kuyruk[0]?.siparisNo ?? null);
  }, [kuyruk, seciliNo, paketlendi]);

  // Seçim değişince önceki karar ve önizleme geçersizdir.
  useEffect(() => {
    setKarar(null);
    setOnizleme(null);
    setOnizlemeHatasi(null);
    yesilSayac.current = 0;
    donukKareyiBirak();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [seciliNo]);

  function donukKareyiBirak() {
    setDonmusKare((onceki) => {
      if (onceki) URL.revokeObjectURL(onceki);
      return null;
    });
  }

  // Canlı önizleme: sunucudaki doğrulama kuralı saniyede bir kez, kayıt/alarm üretmeden çalışır.
  useEffect(() => {
    // Görüntü donmuşken (onay bekleniyor) önizleme durur: ekrandaki sonuç sabit kalır.
    if (!oturum || !seciliNo || paketlendi || donmusKare) return;
    let iptal = false;
    const calis = async () => {
      try {
        const r = await api.dogrula(seciliNo, {
          agirlikG: girdi.current.net, kapAgirligiG: girdi.current.kap, sadeceGoruntu: girdi.current.sadeceGoruntu,
        }, false);
        if (!iptal) {
          setOnizleme(r);
          setOnizlemeHatasi(null);
          // Kamera sayımı da dahil her şey yeşilse üst üste iki yoklamada (≈3 sn) otomatik doğrula.
          yesilSayac.current = r.onaylandi && r.kameraKullanildi ? yesilSayac.current + 1 : 0;
          if (yesilSayac.current >= 2 && Date.now() > sogumaBitis.current) {
            yesilSayac.current = 0;
            otomatikCalis.current();
          }
        }
      } catch (h) {
        if (!iptal) setOnizlemeHatasi(h instanceof ApiHata ? h.message : "Önizleme alınamadı.");
      }
    };
    calis();
    const t = setInterval(calis, 1500);
    return () => { iptal = true; clearInterval(t); };
  }, [oturum, seciliNo, paketlendi, donmusKare]);

  if (!oturum) return null;

  const secili = kuyruk?.find((k) => k.siparisNo === seciliNo) ?? null;
  const satirlar: DogrulamaSatiri[] = onizleme?.satirlar
    ?? secili?.satirlar.map((s) => ({
      urunId: s.urunId, urunAdi: s.urunAdi, beklenenAdet: s.adet, kameraAdedi: null, durum: "Kontrolsuz" as const,
      beklenenAgirlikG: s.birimAgirlikG ? s.adet * s.birimAgirlikG : null,
    }))
    ?? [];

  async function dogrula(): Promise<DogrulamaSonucu | null> {
    if (!seciliNo) return null;
    setIslemde(true);
    try {
      const sonuc = await api.dogrula(seciliNo, { agirlikG: tarti.net, kapAgirligiG: tarti.kapG, sadeceGoruntu }, true);
      setKarar(sonuc);
      if (!sonuc.onaylandi) toast("Paket doğrulanamadı: hataları düzeltip tekrar deneyin.", "hata");
      return sonuc;
    } catch (h) {
      toast(h instanceof ApiHata ? h.message : "Doğrulama yapılamadı.", "hata");
      return null;
    } finally {
      setIslemde(false);
    }
  }

  /** Yeşil olunca: o anın karesini yakala, görüntüyü dondur, doğrulamayı kaydet. Tutmazsa canlıya dön. */
  async function otomatikDogrula() {
    if (!otomatik || islemde || karar?.onaylandi || donmusKare || !seciliNo || paketlendi) return;
    try {
      const yanit = await fetch(`${VISION_URL}/kare.jpg?t=${Date.now()}`, { cache: "no-store" });
      if (yanit.ok) setDonmusKare(URL.createObjectURL(await yanit.blob()));
    } catch { /* kare alınamazsa görüntü canlı kalır; doğrulama yine kaydedilir */ }
    const sonuc = await dogrula();
    if (!sonuc?.onaylandi) {
      donukKareyiBirak();
      sogumaBitis.current = Date.now() + 4000;
    }
  }
  otomatikCalis.current = () => { void otomatikDogrula(); };

  /** Donmuş görüntüyü bırakıp kamerayı canlıya döndürür; ürün hâlâ yerindeyse hemen yeniden dondurmasın. */
  function canliyaDon() {
    donukKareyiBirak();
    setKarar(null);
    sogumaBitis.current = Date.now() + 4000;
    yesilSayac.current = 0;
  }

  async function paketle(zorla = false) {
    if (!seciliNo) return;
    let neden = "";
    if (zorla) {
      neden = prompt("Doğrulamayı atlayarak paketlemek için gerekçe yazın (zorunlu, kayda geçer):") ?? "";
      if (!neden.trim()) return;
    }
    setIslemde(true);
    try {
      const sonuc = zorla ? await api.zorlaPaketle(seciliNo, neden) : await api.paketle(seciliNo, karar?.dogrulamaId);
      setPaketlendi(sonuc);
      setKarar(null);
      toast(`${sonuc.siparisNo} paketlendi${sonuc.istisna ? " (yönetici istisnası)" : ""}.`, "basari");
      kuyruguYenile();
    } catch (h) {
      toast(h instanceof ApiHata ? h.message : "Paketlenemedi.", "hata");
    } finally {
      setIslemde(false);
    }
  }

  function siradaki() {
    setPaketlendi(null);
    setSeciliNo(null);
    tarti.setElleGram("");
    kuyruguYenile();
  }

  return (
    <div className="space-y-6">
      <header>
        <p className="etiket">Paketleme</p>
        <h1 className="baslik mt-1">Paket doğrulama &amp; paketleme</h1>
        <p className="mt-1 text-sm text-slate-400">
          Toplanan siparişin paketini kameranın önüne ve tartıya koyun. Kamera ürünleri sayar, tartı gramı ölçer;
          sistem içeriği siparişle karşılaştırır. Uyuşuyorsa onaylayıp paketleyin, uyuşmuyorsa hata mesajı çıkar.
        </p>
      </header>

      {/* Paketlenecek siparişler */}
      <section className="kart" data-test="paketleme-kuyrugu">
        <h2 className="etiket mb-2">Paketlenecek siparişler {kuyruk && <span className="text-slate-500">({kuyruk.length})</span>}</h2>
        {kuyruk === null && <p className="text-sm text-slate-500">yükleniyor…</p>}
        {kuyruk?.length === 0 && !paketlendi && (
          <p className="text-sm text-slate-400">
            Paketlenmeyi bekleyen sipariş yok. Siparişler tüm kalemleri toplanınca burada görünür.{" "}
            <Link href="/siparisler" className="text-marka-metin underline">Siparişlere git</Link>
          </p>
        )}
        <div className="flex max-h-44 flex-wrap gap-2 overflow-y-auto pr-1">
          {kuyruk?.map((k) => (
            <button key={k.id} onClick={() => { setPaketlendi(null); setSeciliNo(k.siparisNo); }}
              data-test="paketleme-siparis" data-siparis={k.siparisNo}
              className={`rounded-lg border px-3 py-2 text-left text-sm transition ${
                k.siparisNo === seciliNo ? "border-marka-acik bg-marka/20" : "border-zemin-cizgi hover:border-marka-acik/60"}`}>
              <span className="flex items-center gap-2 font-semibold">{k.siparisNo} <OncelikRozeti oncelik={k.oncelik} /></span>
              <span className="ipucu">
                {k.satirlar.map((s) => `${s.adet} × ${s.urunAdi}`).join(", ")}
                {k.beklenenAgirlikG != null && ` · ${gramMetni(k.beklenenAgirlikG)}`}
              </span>
            </button>
          ))}
        </div>
      </section>

      {paketlendi && (
        <section className="kart border-durum-yesil/50 bg-durum-yesil/10 text-center" data-test="paketlendi-karti">
          <p className="text-5xl text-durum-yesil" aria-hidden>✓</p>
          <p className="mt-1 text-2xl font-extrabold text-durum-yesil">{paketlendi.siparisNo} paketlendi</p>
          <p className="mt-1 text-sm text-slate-300">
            {new Date(paketlendi.paketlenmeZamani).toLocaleTimeString("tr-TR")}
            {paketlendi.paketleyenAdi && <> · {paketlendi.paketleyenAdi}</>}
            {paketlendi.istisna && <> · yönetici istisnası: {paketlendi.istisnaNedeni}</>}
          </p>
          <button className="dugme mt-4" onClick={siradaki} data-test="siradaki-siparis">Sıradaki sipariş</button>
        </section>
      )}

      {secili && !paketlendi && (
        <div className="grid gap-4 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
          <div className="space-y-4">
            <KameraGorunumu saglik={saglik} tani={tani} yenile={durumuYenile} dondurulmus={donmusKare} />
            {donmusKare && (
              <p className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-marka-acik/50 bg-marka/15 px-3 py-2 text-sm"
                data-test="donmus-bilgi">
                <span>❄ Paket yeşil olunca görüntü yakalandı ve donduruldu.</span>
                <a className="underline" href={donmusKare} download={`${secili.siparisNo}-dogrulama.jpg`}>Görüntüyü indir</a>
              </p>
            )}
            <label className="flex items-center gap-2 text-sm text-slate-300" data-test="otomatik-dogrula">
              <input type="checkbox" checked={otomatik} onChange={(e) => setOtomatik(e.target.checked)} />
              Otomatik doğrula: paket yeşil olunca görüntüyü yakala ve dondur
            </label>
            <label className="flex items-center gap-2 text-sm text-slate-300" data-test="sadece-goruntu">
              <input type="checkbox" checked={sadeceGoruntu} onChange={(e) => setSadeceGoruntu(e.target.checked)} />
              Ağırlık olmadan, yalnızca görüntüyle doğrula (tartı kullanılmaz)
            </label>
            <div className={sadeceGoruntu ? "pointer-events-none opacity-40" : undefined}>
              <TartiGirdisiKarti tarti={tarti} kutular={kutular} kapAlani />
            </div>
          </div>

          <div className="space-y-4">
            {/* Beklenen içerik ve canlı karşılaştırma */}
            <section className="kart space-y-3" data-test="paketleme-icerik">
              <div className="flex items-center justify-between gap-2">
                <h2 className="etiket">{secili.siparisNo} · paket içeriği</h2>
                {onizleme && (
                  <span className={`rozet ${onizleme.onaylandi ? "bg-durum-yesil/15 text-durum-yesil" : "bg-durum-sari/20 text-durum-sari"}`}
                    data-test="onizleme-rozeti">
                    {onizleme.onaylandi ? "önizleme: uyuşuyor" : `önizleme: ${onizleme.hatalar.length} sorun`}
                  </span>
                )}
              </div>

              <table className="tablo">
                <thead><tr><th>Ürün</th><th>Beklenen</th><th>Kamera</th><th>Durum</th></tr></thead>
                <tbody>
                  {satirlar.map((s) => (
                    <tr key={s.urunId} data-test="paketleme-satiri" data-durum={s.durum}>
                      <td className="font-semibold">{s.urunAdi}</td>
                      <td className="tabular-nums">{s.beklenenAdet}</td>
                      <td className="tabular-nums">{s.kameraAdedi ?? "—"}</td>
                      <td><span className={`rozet ${DURUM_SINIF[s.durum]}`}>{DURUM_METIN[s.durum]}</span></td>
                    </tr>
                  ))}
                </tbody>
              </table>

              <div className="grid grid-cols-3 gap-2 border-t border-zemin-cizgi/60 pt-3 text-center">
                <div><p className="etiket">Beklenen</p>
                  <p className="mt-1 font-mono font-bold">{onizleme?.beklenenAgirlikG != null ? gramMetni(onizleme.beklenenAgirlikG)
                    : secili.beklenenAgirlikG != null ? gramMetni(secili.beklenenAgirlikG) : "—"}</p></div>
                <div><p className="etiket">Ölçülen (net)</p>
                  <p className="mt-1 font-mono font-bold">{sadeceGoruntu ? "kullanılmıyor" : tarti.net != null ? gramMetni(tarti.net - (tarti.kapG ?? 0)) : "—"}</p></div>
                <div><p className="etiket">Sapma</p>
                  <p className={`mt-1 font-mono font-bold ${onizleme?.sapmaG != null && onizleme.toleransG != null && Math.abs(onizleme.sapmaG) > onizleme.toleransG ? "text-durum-kirmizi" : ""}`}>
                    {onizleme?.sapmaG != null ? `${onizleme.sapmaG > 0 ? "+" : ""}${gramMetni(onizleme.sapmaG)}` : "—"}
                  </p>
                  {onizleme?.toleransG != null && <p className="ipucu">tolerans ±{gramMetni(onizleme.toleransG)}</p>}
                </div>
              </div>

              {onizlemeHatasi && <p className="text-sm text-durum-kirmizi">{onizlemeHatasi}</p>}
              {onizleme?.uyarilar.map((u) => <p key={u} className="text-sm text-durum-sari">⚠ {u}</p>)}
            </section>

            {/* Karar */}
            <section className="kart space-y-3" data-test="paketleme-karar">
              {!karar && (
                <>
                  <p className="text-sm text-slate-300">
                    Paketi yerleştirin; önizleme tutunca <b>Doğrula</b>ya basın. Sonuç kaydedilir.
                  </p>
                  <button className="dugme" disabled={islemde} onClick={() => void dogrula()} data-test="dogrula">
                    {islemde ? "Doğrulanıyor…" : "Doğrula"}
                  </button>
                </>
              )}

              {karar?.onaylandi && (
                <div className="space-y-3" data-test="karar-onay">
                  <p className="text-xl font-extrabold text-durum-yesil">✓ ONAY — paket siparişle uyuşuyor</p>
                  <p className="text-sm text-slate-300">
                    {karar.kameraKullanildi
                      ? karar.netAgirlikG == null
                        ? "Kamera sayımı siparişle aynı. Ağırlık kullanılmadı; yalnızca görüntüyle doğrulandı."
                        : "Kamera sayımı ve ağırlık siparişle aynı."
                      : "Yalnızca ağırlıkla doğrulandı (kamera kullanılmadı)."}
                  </p>
                  <div className="flex flex-wrap gap-2">
                    <button className="dugme" disabled={islemde} onClick={() => paketle(false)} data-test="paketle">
                      Onayla ve paketle
                    </button>
                    <button className="dugme-ikincil" disabled={islemde} onClick={canliyaDon} data-test="canliya-don">Yeniden doğrula (kamerayı aç)</button>
                  </div>
                </div>
              )}

              {karar && !karar.onaylandi && (
                <div className="space-y-3" data-test="karar-hata">
                  <p className="text-xl font-extrabold text-durum-kirmizi">✕ HATA — paket paketlenemez</p>
                  <ul className="space-y-2">
                    {karar.hatalar.map((h, i) => (
                      <li key={i} className="rounded-lg border border-durum-kirmizi/40 bg-durum-kirmizi/10 px-3 py-2 text-sm"
                        data-test="dogrulama-hatasi" data-kod={h.kod}>
                        <span className="rozet mr-2 bg-durum-kirmizi/25 text-durum-kirmizi">{h.kod.replaceAll("_", " ")}</span>
                        {h.mesaj}
                      </li>
                    ))}
                  </ul>
                  <div className="flex flex-wrap gap-2">
                    <button className="dugme" disabled={islemde} onClick={() => void dogrula()} data-test="yeniden-dogrula">
                      Düzelttim, yeniden doğrula
                    </button>
                    {yonetici && (
                      <button className="dugme-tehlike" disabled={islemde} onClick={() => paketle(true)} data-test="zorla-paketle"
                        title="Doğrulamayı atlar; gerekçe kayda geçer ve alarm üretilir">
                        Yine de paketle (yönetici)
                      </button>
                    )}
                  </div>
                </div>
              )}
            </section>
          </div>
        </div>
      )}
    </div>
  );
}
