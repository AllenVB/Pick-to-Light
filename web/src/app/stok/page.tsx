"use client";

import { useCallback, useEffect, useState } from "react";
import { ApiHata, api, oturumOku } from "@/lib/api";
import { useCanliOlaylar } from "@/hooks/useCanliOlaylar";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import { useToast } from "@/components/Toast";
import { BosDurum, IskeletSatir } from "@/components/Durumlar";
import { RenkRozeti } from "@/components/Rozet";
import { BaskulSatiri, baskulVar, canliKutular, sayimSapmasi, useCanliAgirlik } from "@/components/Baskul";
import type { Bolme, Kutu } from "@/types";

export default function StokSayfasi() {
  const oturum = useKorumaliSayfa();
  const [hamKutular, setKutular] = useState<Kutu[]>([]);
  const kutular = canliKutular(hamKutular, useCanliAgirlik());
  const [hata, setHata] = useState<string | null>(null);
  const [yukleniyor, setYukleniyor] = useState(true);
  const toast = useToast();

  const yonetici = oturum?.rol === "Admin";
  const yenile = useCallback(() => {
    api.stokDurumu()
      .then(setKutular)
      .catch(() => setHata("Stok bilgisi alınamadı."))
      .finally(() => setYukleniyor(false));
  }, []);

  useEffect(() => { if (oturum) yenile(); }, [oturum, yenile]);
  useCanliOlaylar({ StokGuncellendi: yenile, CihazDurumDegisti: yenile, KutuIsikDegisti: yenile });

  async function calistir(f: () => Promise<unknown>, basarimMesaji: string) {
    setHata(null);
    try {
      await f();
      toast(basarimMesaji, "basari");
      yenile();
    } catch (h) {
      toast(h instanceof ApiHata ? h.message : "İşlem başarısız.", "hata");
    }
  }

  if (!oturum) return null;

  // Karışık kutu her bölmesiyle ayrı satırdır (stok bölme başınadır); LED ve baskül cihaza aittir.
  const satirlar = kutular.flatMap((k): { k: Kutu; uye: Bolme | undefined; ilk: boolean }[] =>
    k.bolmeler
      ? k.bolmeler.map((uye, i) => ({ k, uye, ilk: i === 0 }))
      : [{ k, uye: undefined, ilk: true }]);

  return (
    <div className="space-y-6">
      <header>
        <p className="etiket">Stok Yönetimi</p>
        <h1 className="baslik mt-1">Kutu stokları</h1>
        <p className="mt-1 text-sm text-slate-400">
          Mal kabul ve sayım yalnızca yönetici rolüyle yapılır. Her işlem hareket defterine yazılır.
        </p>
      </header>

      {hata && <p className="rounded-lg border border-durum-kirmizi/40 bg-durum-kirmizi/10 px-4 py-2 text-sm text-durum-kirmizi">{hata}</p>}

      <section className="kart overflow-x-auto">
        <table className="tablo">
          <thead>
            <tr>
              <th>Kutu</th><th>Ürün</th><th>Raf</th><th>Stok</th><th>Güvenlik</th>
              <th>Baskül</th><th>Durum</th><th>Bağlantı</th>{yonetici && <th>İşlem</th>}
            </tr>
          </thead>
          <tbody>
            {yukleniyor && <><IskeletSatir sutun={7} /><IskeletSatir sutun={7} /><IskeletSatir sutun={7} /></>}

            {!yukleniyor && kutular.length === 0 && (
              <tr><td colSpan={9}>
                <BosDurum simge="▤" baslik="Stok kaydı yok"
                  aciklama="Önce Kutu & Cihaz ekranından kutu tanımlayın; stok kutuya bağlı tutulur." />
              </td></tr>
            )}

            {!yukleniyor && satirlar.map(({ k, uye, ilk }) => (
              <tr key={uye?.id ?? k.id} data-test="stok-satiri" data-kutu={uye?.kod ?? k.kod}>
                <td className="font-mono text-xs">{uye?.kod ?? k.kod}</td>
                <td>{uye ? (uye.urunAdi ?? "—") : (k.urunAdi ?? "—")}</td>
                <td className="text-xs text-slate-400">{k.rafKonumu ?? "—"}</td>
                <td className="text-lg font-bold">{uye ? uye.adet : k.adet}</td>
                <td className="text-slate-400">{uye ? uye.guvenlikStogu : k.guvenlikStogu}</td>
                <td>
                  {/* Karışık kutuda baskül kutunun toplamını ölçer: yalnızca ilk satırda gösterilir. */}
                  {ilk && <BaskulSatiri kutu={k} />}
                  {ilk && !baskulVar(k) && <span className="text-slate-600">—</span>}
                  {!ilk && <span className="ipucu">kutu toplamı üstte</span>}
                </td>
                <td><RenkRozeti renk={(uye ? uye.renk : k.anlikRenk) ?? undefined} /></td>
                <td>
                  <span className={k.baglantiDurumu === "Online" ? "text-durum-yesil" : "text-durum-gri"}>
                    ● {k.baglantiDurumu === "Online" ? "Çevrimiçi" : "Çevrimdışı"}
                  </span>
                </td>
                {yonetici && (
                  <td className="space-x-1 whitespace-nowrap">
                    <button className="dugme-ikincil px-2 py-1 text-xs"
                      onClick={() => {
                        const m = prompt(`${uye?.kod ?? k.kod} — kaç adet giriş yapılacak?`, "10");
                        if (m) calistir(() => api.stokGiris(uye?.id ?? k.id, Number(m), "Mal kabul"), `${uye?.kod ?? k.kod}: giriş yapıldı.`);
                      }}>+ Giriş</button>
                    <button className="dugme-ikincil px-2 py-1 text-xs"
                      onClick={() => {
                        const m = prompt(`${uye?.kod ?? k.kod} — sayım sonucu kaç adet?`, String(uye ? uye.adet : k.adet));
                        if (m) calistir(() => api.stokSayim(uye?.id ?? k.id, Number(m), "Fiziksel sayım"), `${uye?.kod ?? k.kod}: sayım kaydedildi.`);
                      }}>Sayım</button>
                    {!k.bolmeler && baskulVar(k) && k.birimAgirlikG != null && sayimSapmasi(k) != null && (
                      <button className="dugme-ikincil px-2 py-1 text-xs" data-test="agirliktan-sayim"
                        title="Baskülün saydığı adedi stoka yaz (rezerve edilen adet düşülür)"
                        onClick={() => {
                          if (confirm(`${k.kod}: baskül ${k.tahminiAdet} adet sayıyor. Stok ağırlığa göre düzeltilsin mi?`))
                            calistir(() => api.agirliktanSayim(k.id), `${k.kod}: baskül sayımı uygulandı.`);
                        }}>⚖ Ağırlıktan say</button>
                    )}
                    {ilk && <button className="dugme-ikincil px-2 py-1 text-xs"
                      onClick={() => calistir(() => api.ledTest(k.id), `${k.kod}: test ışığı gönderildi.`)}>
                      LED test
                    </button>}
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </div>
  );
}
