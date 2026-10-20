"use client";

import { useCallback, useEffect, useState } from "react";
import { ApiHata, api } from "@/lib/api";
import { useCanliOlaylar } from "@/hooks/useCanliOlaylar";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import { useToast } from "@/components/Toast";
import { BosDurum, IskeletKart } from "@/components/Durumlar";
import { GecenSure, Kopyala, Sinyal } from "@/components/Cihaz";
import type { Kutu, LedRengi, Urun } from "@/types";

export default function KutularSayfasi() {
  const oturum = useKorumaliSayfa();
  const yonetici = oturum?.rol === "Admin";

  const [kutular, setKutular] = useState<Kutu[]>([]);
  const [urunler, setUrunler] = useState<Urun[]>([]);
  const [hata, setHata] = useState<string | null>(null);
  const [formAcik, setFormAcik] = useState(false);
  const [duzenlenen, setDuzenlenen] = useState<Kutu | null>(null);
  const [yukleniyor, setYukleniyor] = useState(true);
  const toast = useToast();

  const yenile = useCallback(() => {
    api.kutular()
      .then(setKutular)
      .catch(() => setHata("Kutu listesi alınamadı."))
      .finally(() => setYukleniyor(false));
    api.urunler().then(setUrunler).catch(() => undefined);
  }, []);

  useEffect(() => { if (oturum) yenile(); }, [oturum, yenile]);
  useCanliOlaylar({ CihazDurumDegisti: yenile, StokGuncellendi: yenile, KutuIsikDegisti: yenile });

  async function calistir(f: () => Promise<unknown>, basarim: string) {
    setHata(null);
    try {
      await f();
      toast(basarim, "basari");
      yenile();
    } catch (h) {
      toast(h instanceof ApiHata ? h.message : "İşlem başarısız.", "hata");
    }
  }

  async function isikTesti(kutu: Kutu, renk: LedRengi) {
    try {
      const sonuc = await api.ledTest(kutu.id, renk, 8);
      toast(
        sonuc.cihazCevrimici
          ? `${kutu.kod}: ${sonuc.saniye} sn test ışığı gönderildi (${sonuc.topic}).`
          : `${kutu.kod} çevrimdışı — komut yayınlandı ama cihaz dinlemiyor olabilir.`,
        sonuc.cihazCevrimici ? "basari" : "bilgi"
      );
    } catch (h) {
      toast(h instanceof ApiHata ? h.message : "Test komutu gönderilemedi.", "hata");
    }
  }

  if (!oturum) return null;

  const cevrimici = kutular.filter((k) => k.baglantiDurumu === "Online").length;

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <p className="etiket">Kutu &amp; Cihaz Yönetimi</p>
          <h1 className="baslik mt-1">Fiziksel kutular</h1>
          <p className="mt-1 text-sm text-slate-400">
            {kutular.length} kutu · {cevrimici} çevrimiçi. Kutu eklemek yalnızca veritabanı kaydı
            gerektirir; MQTT topic'i kimlikten türetilir, kod değişmez.
          </p>
        </div>
        {yonetici && (
          <button className="dugme" onClick={() => { setDuzenlenen(null); setFormAcik((a) => !a); }}>
            {formAcik ? "Formu kapat" : "+ Yeni kutu"}
          </button>
        )}
      </header>

      {hata && <p className="rounded-lg border border-durum-kirmizi/40 bg-durum-kirmizi/10 px-4 py-2 text-sm text-durum-kirmizi">{hata}</p>}

      {(formAcik || duzenlenen) && yonetici && (
        <KutuFormu
          urunler={urunler}
          mevcut={duzenlenen}
          onKaydet={(istek) =>
            calistir(
              () => duzenlenen ? api.kutuGuncelle(duzenlenen.id, istek) : api.kutuEkle(istek),
              duzenlenen ? `${istek.kod} güncellendi.` : `${istek.kod} eklendi.`
            ).then(() => { setFormAcik(false); setDuzenlenen(null); })
          }
          onVazgec={() => { setFormAcik(false); setDuzenlenen(null); }}
        />
      )}

      {yukleniyor ? (
        <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-3"><IskeletKart adet={3} /></section>
      ) : kutular.length === 0 ? (
        <section className="kart">
          <BosDurum simge="▣" baslik="Henüz kutu tanımlı değil"
            aciklama="Her kutu bir ESP32 kontrolcüsüne karşılık gelir. Kutuyu ekledikten sonra
                      MQTT topic'i kimliğinden türetilir ve aynı kimliği firmware'e girersiniz."
            eylem={yonetici && <button className="dugme" onClick={() => setFormAcik(true)}>+ İlk kutuyu ekle</button>} />
        </section>
      ) : (
        <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-3" data-test="cihaz-kartlari">
          {kutular.map((k) => (
            <CihazKarti key={k.id} kutu={k} yonetici={yonetici}
              onDuzenle={() => { setFormAcik(false); setDuzenlenen(k); }}
              onTest={(renk) => isikTesti(k, renk)}
              onArsiv={() => calistir(
                () => api.kutuAktiflik(k.id, k.aktif === false),
                k.aktif === false ? `${k.kod} yeniden devreye alındı.` : `${k.kod} arşivlendi.`)} />
          ))}
        </section>
      )}

      {!yonetici && (
        <p className="text-xs text-slate-500">
          Kutu ekleme ve LED testi yönetici yetkisi gerektirir.
        </p>
      )}
    </div>
  );
}

function KutuFormu({ urunler, mevcut, onKaydet, onVazgec }: {
  urunler: Urun[];
  mevcut: Kutu | null;
  onKaydet: (istek: { kod: string; urunId?: number; cihazMac: string; rafKonumu?: string; baslangicStogu: number }) => void;
  onVazgec: () => void;
}) {
  const [kod, setKod] = useState(mevcut?.kod ?? "");
  const [urunId, setUrunId] = useState<number | "">(mevcut?.urunId ?? "");
  const [cihazMac, setCihazMac] = useState(mevcut?.cihazMac ?? "");
  const [rafKonumu, setRafKonumu] = useState(mevcut?.rafKonumu ?? "");
  const [baslangicStogu, setBaslangicStogu] = useState(mevcut?.adet ?? 0);

  const macGecerli = /^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$/.test(cihazMac);
  const gecerli = kod.trim().length > 0 && macGecerli;

  return (
    <section className="kart space-y-4">
      <h2 className="etiket">{mevcut ? `${mevcut.kod} — kutuyu düzenle` : "Yeni kutu"}</h2>
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
        <div>
          <label className="etiket">Kod</label>
          <input className="girdi mt-1" placeholder="K-06" value={kod} onChange={(e) => setKod(e.target.value)} />
        </div>
        <div>
          <label className="etiket">Ürün</label>
          <select className="girdi mt-1" value={urunId}
            onChange={(e) => setUrunId(e.target.value ? Number(e.target.value) : "")}>
            <option value="">— atanmamış —</option>
            {urunler.map((u) => <option key={u.id} value={u.id}>{u.ad}</option>)}
          </select>
        </div>
        <div>
          <label className="etiket">Cihaz MAC</label>
          <input className="girdi mt-1" placeholder="A0:B1:C2:D3:E4:F5" value={cihazMac}
            onChange={(e) => setCihazMac(e.target.value)} />
          {cihazMac && !macGecerli && (
            <p className="mt-1 text-[11px] text-durum-kirmizi">Biçim: AA:BB:CC:DD:EE:FF</p>
          )}
        </div>
        <div>
          <label className="etiket">Raf konumu</label>
          <input className="girdi mt-1" placeholder="B2-R1" value={rafKonumu}
            onChange={(e) => setRafKonumu(e.target.value)} />
        </div>
        <div>
          <label className="etiket">{mevcut ? "Mevcut stok" : "Başlangıç stoğu"}</label>
          <input className="girdi mt-1" type="number" min={0} value={baslangicStogu} disabled={!!mevcut}
            onChange={(e) => setBaslangicStogu(Math.max(0, Number(e.target.value)))} />
          {mevcut && <p className="mt-1 text-[11px] text-slate-500">stok yalnızca sayım/giriş ile değişir</p>}
        </div>
      </div>

      <div className="flex gap-2">
      <button className="dugme" disabled={!gecerli}
        onClick={() => onKaydet({
          kod: kod.trim(),
          urunId: urunId === "" ? undefined : urunId,
          cihazMac: cihazMac.trim().toUpperCase(),
          rafKonumu: rafKonumu.trim() || undefined,
          baslangicStogu,
        })}>
        {mevcut ? "Değişiklikleri kaydet" : "Kutuyu ekle"}
      </button>
      <button className="dugme-ikincil" onClick={onVazgec}>Vazgeç</button>
      </div>
      <p className="text-[11px] text-slate-500">
        Kayıttan sonra kutunun MQTT topic'i <span className="font-mono">depo/kutu/&#123;id&#125;</span> olarak atanır;
        aynı değeri ESP32 firmware'ine kutu kimliği olarak girin.
      </p>
    </section>
  );
}

/**
 * Tek bir kutunun donanım kartı.
 *
 * Sahada ESP32 kurarken sırasıyla şunlara bakılır: cihaz bağlandı mı, sinyal
 * yeterli mi, doğru firmware mi, hangi topic'i dinliyor. Hepsi tek kartta
 * toplanır; topic ve MAC tıklanarak kopyalanır.
 */
function CihazKarti({ kutu, yonetici, onDuzenle, onTest, onArsiv }: {
  kutu: Kutu;
  yonetici: boolean;
  onDuzenle: () => void;
  onTest: (renk: LedRengi) => void;
  onArsiv: () => void;
}) {
  const cevrimici = kutu.baglantiDurumu === "Online";
  const arsivli = kutu.aktif === false;

  return (
    <article
      className={`kart space-y-3 ${kutu.isikYaniyor ? "border-marka-acik/70 nabiz-marka" : ""}
                  ${cevrimici && !arsivli ? "" : "opacity-70"} ${arsivli ? "border-dashed" : ""}`}
      data-test="cihaz-karti" data-kutu={kutu.kod}>

      {/* Başlık: kod, raf, bağlantı */}
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <h3 className="font-mono text-lg font-bold tracking-tight">{kutu.kod}</h3>
            {kutu.isikYaniyor && (
              <span className="rozet bg-marka/25 text-marka-acik">IŞIK YANIYOR</span>
            )}
            {arsivli && <span className="rozet bg-slate-600/30 text-slate-400">ARŞİVDE</span>}
          </div>
          <p className="truncate text-sm text-slate-300">
            {kutu.urunAdi ?? <span className="text-slate-500">ürün atanmamış</span>}
          </p>
          <p className="ipucu">{kutu.rafKonumu ?? "raf tanımsız"}</p>
        </div>

        <div className="shrink-0 text-right">
          <span className={`rozet ${cevrimici
            ? "bg-durum-yesil/15 text-durum-yesil"
            : "bg-slate-600/20 text-slate-400"}`}>
            ● {cevrimici ? "ÇEVRİMİÇİ" : "ÇEVRİMDIŞI"}
          </span>
          <div className="mt-1.5"><GecenSure zaman={kutu.sonGorulme} /></div>
        </div>
      </div>

      {/* Cihaz telemetrisi */}
      <div className="grid grid-cols-3 gap-2 border-y border-zemin-cizgi/60 py-2.5">
        <div>
          <p className="etiket">Sinyal</p>
          <div className="mt-1"><Sinyal rssi={kutu.rssi} /></div>
        </div>
        <div>
          <p className="etiket">Firmware</p>
          <p className="mt-1 font-mono text-[11px] text-slate-300">{kutu.firmwareSurumu ?? "—"}</p>
        </div>
        <div>
          <p className="etiket">Stok</p>
          <p className="mt-1 text-sm font-bold tabular-nums">{kutu.adet}</p>
        </div>
      </div>

      {/* Ağ kimliği — kurulumda kopyalanır */}
      <div className="space-y-1.5">
        <div className="flex items-center gap-2">
          <span className="etiket w-14 shrink-0">Topic</span>
          <Kopyala deger={`${kutu.mqttTopic}/komut`} />
        </div>
        <div className="flex items-center gap-2">
          <span className="etiket w-14 shrink-0">MAC</span>
          <Kopyala deger={kutu.cihazMac} />
        </div>
        {kutu.ipAdresi && (
          <div className="flex items-center gap-2">
            <span className="etiket w-14 shrink-0">IP</span>
            <Kopyala deger={kutu.ipAdresi} />
          </div>
        )}
      </div>

      {yonetici && (
        <div className="flex flex-wrap items-center gap-2 pt-1">
          <span className="ipucu mr-1">Işığı test et:</span>
          {([
            ["Yesil", "bg-durum-yesil"],
            ["Sari", "bg-durum-sari"],
            ["Kirmizi", "bg-durum-kirmizi"],
          ] as [LedRengi, string][]).map(([renk, sinif]) => (
            <button key={renk} onClick={() => onTest(renk)}
              title={`${renk} ışığı 8 saniye yak`}
              data-test={`isik-test-${renk}`}
              className="flex h-8 w-8 items-center justify-center rounded-lg border border-zemin-cizgi
                         transition hover:border-marka-acik active:scale-95">
              <span className={`h-3.5 w-3.5 rounded-full ${sinif}`} />
            </button>
          ))}

          <button className="dugme-ikincil dugme-kucuk ml-auto" onClick={onDuzenle}>
            Düzenle
          </button>
          <button className={arsivli ? "dugme dugme-kucuk" : "dugme-tehlike dugme-kucuk"}
            title={arsivli
              ? "Kutuyu yeniden devreye al"
              : "Kutuyu devreden çıkar: toplamaya girmez, kayıtlar silinmez"}
            onClick={onArsiv}>
            {arsivli ? "Devreye al" : "Arşivle"}
          </button>
        </div>
      )}
    </article>
  );
}
