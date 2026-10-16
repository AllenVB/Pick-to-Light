"use client";

import { useCallback, useEffect, useState } from "react";
import { ApiHata, api } from "@/lib/api";
import { useCanliOlaylar } from "@/hooks/useCanliOlaylar";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import type { Kutu, LedRengi, Urun } from "@/types";

export default function KutularSayfasi() {
  const oturum = useKorumaliSayfa();
  const yonetici = oturum?.rol === "Admin";

  const [kutular, setKutular] = useState<Kutu[]>([]);
  const [urunler, setUrunler] = useState<Urun[]>([]);
  const [hata, setHata] = useState<string | null>(null);
  const [bilgi, setBilgi] = useState<string | null>(null);
  const [formAcik, setFormAcik] = useState(false);

  const yenile = useCallback(() => {
    api.kutular().then(setKutular).catch(() => setHata("Kutu listesi alınamadı."));
    api.urunler().then(setUrunler).catch(() => undefined);
  }, []);

  useEffect(() => { if (oturum) yenile(); }, [oturum, yenile]);
  useCanliOlaylar({ CihazDurumDegisti: yenile, StokGuncellendi: yenile, KutuIsikDegisti: yenile });

  async function calistir(f: () => Promise<unknown>, basarim: string) {
    setHata(null); setBilgi(null);
    try { await f(); setBilgi(basarim); yenile(); }
    catch (h) { setHata(h instanceof ApiHata ? h.message : "İşlem başarısız."); }
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
          <button className="dugme" onClick={() => setFormAcik((a) => !a)}>
            {formAcik ? "Formu kapat" : "+ Yeni kutu"}
          </button>
        )}
      </header>

      {hata && <p className="rounded-lg border border-durum-kirmizi/40 bg-durum-kirmizi/10 px-4 py-2 text-sm text-durum-kirmizi">{hata}</p>}
      {bilgi && <p className="rounded-lg border border-durum-yesil/40 bg-durum-yesil/10 px-4 py-2 text-sm text-durum-yesil">{bilgi}</p>}

      {formAcik && yonetici && (
        <KutuFormu
          urunler={urunler}
          onKaydet={(istek) => calistir(() => api.kutuEkle(istek), `${istek.kod} eklendi.`).then(() => setFormAcik(false))}
        />
      )}

      <section className="kart overflow-x-auto">
        <table className="tablo">
          <thead>
            <tr>
              <th>Kod</th><th>Ürün</th><th>Raf</th><th>Cihaz MAC</th><th>MQTT topic</th>
              <th>Stok</th><th>Bağlantı</th><th>Son görülme</th>{yonetici && <th>LED testi</th>}
            </tr>
          </thead>
          <tbody>
            {kutular.map((k) => (
              <tr key={k.id} className={k.isikYaniyor ? "bg-marka/5" : ""}>
                <td className="font-mono text-xs font-bold">{k.kod}</td>
                <td>{k.urunAdi ?? <span className="text-slate-500">— atanmamış —</span>}</td>
                <td className="text-xs text-slate-400">{k.rafKonumu ?? "—"}</td>
                <td className="font-mono text-[11px] text-slate-400">{k.cihazMac}</td>
                <td className="font-mono text-[11px] text-slate-400">{k.mqttTopic}</td>
                <td className="font-bold">{k.adet}</td>
                <td>
                  <span className={k.baglantiDurumu === "Online" ? "text-durum-yesil" : "text-durum-gri"}>
                    ● {k.baglantiDurumu === "Online" ? "Çevrimiçi" : "Çevrimdışı"}
                  </span>
                  {k.isikYaniyor && <span className="ml-2 text-[11px] text-marka-acik">🔆 ışık yanıyor</span>}
                </td>
                <td className="text-xs text-slate-400">
                  {k.sonGorulme ? new Date(k.sonGorulme).toLocaleString("tr-TR") : "hiç"}
                </td>
                {yonetici && (
                  <td className="whitespace-nowrap">
                    {(["Yesil", "Sari", "Kirmizi"] as LedRengi[]).map((renk) => (
                      <button key={renk} title={`${renk} test`}
                        className="mr-1 rounded-md border border-zemin-cizgi px-2 py-1 text-xs hover:border-marka-acik"
                        onClick={() => calistir(() => api.ledTest(k.id, renk), `${k.kod}: ${renk} test ışığı gönderildi.`)}>
                        {renk === "Yesil" ? "🟢" : renk === "Sari" ? "🟡" : "🔴"}
                      </button>
                    ))}
                  </td>
                )}
              </tr>
            ))}
            {kutular.length === 0 && (
              <tr><td colSpan={yonetici ? 9 : 8} className="text-slate-500">Kutu yok.</td></tr>
            )}
          </tbody>
        </table>
      </section>

      {!yonetici && (
        <p className="text-xs text-slate-500">
          Kutu ekleme ve LED testi yönetici yetkisi gerektirir.
        </p>
      )}
    </div>
  );
}

function KutuFormu({ urunler, onKaydet }: {
  urunler: Urun[];
  onKaydet: (istek: { kod: string; urunId?: number; cihazMac: string; rafKonumu?: string; baslangicStogu: number }) => void;
}) {
  const [kod, setKod] = useState("");
  const [urunId, setUrunId] = useState<number | "">("");
  const [cihazMac, setCihazMac] = useState("");
  const [rafKonumu, setRafKonumu] = useState("");
  const [baslangicStogu, setBaslangicStogu] = useState(0);

  const macGecerli = /^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$/.test(cihazMac);
  const gecerli = kod.trim().length > 0 && macGecerli;

  return (
    <section className="kart space-y-4">
      <h2 className="etiket">Yeni kutu</h2>
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
          <label className="etiket">Başlangıç stoğu</label>
          <input className="girdi mt-1" type="number" min={0} value={baslangicStogu}
            onChange={(e) => setBaslangicStogu(Math.max(0, Number(e.target.value)))} />
        </div>
      </div>

      <button className="dugme" disabled={!gecerli}
        onClick={() => onKaydet({
          kod: kod.trim(),
          urunId: urunId === "" ? undefined : urunId,
          cihazMac: cihazMac.trim().toUpperCase(),
          rafKonumu: rafKonumu.trim() || undefined,
          baslangicStogu,
        })}>
        Kutuyu ekle
      </button>
      <p className="text-[11px] text-slate-500">
        Kayıttan sonra kutunun MQTT topic'i <span className="font-mono">depo/kutu/&#123;id&#125;</span> olarak atanır;
        aynı değeri ESP32 firmware'ine kutu kimliği olarak girin.
      </p>
    </section>
  );
}
