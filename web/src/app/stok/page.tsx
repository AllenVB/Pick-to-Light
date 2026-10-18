"use client";

import { useCallback, useEffect, useState } from "react";
import { ApiHata, api, oturumOku } from "@/lib/api";
import { useCanliOlaylar } from "@/hooks/useCanliOlaylar";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import { RenkRozeti } from "@/components/Rozet";
import type { Kutu } from "@/types";

export default function StokSayfasi() {
  const oturum = useKorumaliSayfa();
  const [kutular, setKutular] = useState<Kutu[]>([]);
  const [hata, setHata] = useState<string | null>(null);
  const [bilgi, setBilgi] = useState<string | null>(null);

  const yonetici = oturum?.rol === "Admin";
  const yenile = useCallback(() => {
    api.stokDurumu().then(setKutular).catch(() => setHata("Stok bilgisi alınamadı."));
  }, []);

  useEffect(() => { if (oturum) yenile(); }, [oturum, yenile]);
  useCanliOlaylar({ StokGuncellendi: yenile, CihazDurumDegisti: yenile, KutuIsikDegisti: yenile });

  async function calistir(f: () => Promise<unknown>, basarimMesaji: string) {
    setHata(null); setBilgi(null);
    try { await f(); setBilgi(basarimMesaji); yenile(); }
    catch (h) { setHata(h instanceof ApiHata ? h.message : "İşlem başarısız."); }
  }

  if (!oturum) return null;

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
      {bilgi && <p className="rounded-lg border border-durum-yesil/40 bg-durum-yesil/10 px-4 py-2 text-sm text-durum-yesil">{bilgi}</p>}

      <section className="kart overflow-x-auto">
        <table className="tablo">
          <thead>
            <tr>
              <th>Kutu</th><th>Ürün</th><th>Raf</th><th>Stok</th><th>Güvenlik</th>
              <th>Durum</th><th>Bağlantı</th>{yonetici && <th>İşlem</th>}
            </tr>
          </thead>
          <tbody>
            {kutular.map((k) => (
              <tr key={k.id} data-test="stok-satiri" data-kutu={k.kod}>
                <td className="font-mono text-xs">{k.kod}</td>
                <td>{k.urunAdi ?? "—"}</td>
                <td className="text-xs text-slate-400">{k.rafKonumu ?? "—"}</td>
                <td className="text-lg font-bold">{k.adet}</td>
                <td className="text-slate-400">{k.guvenlikStogu}</td>
                <td><RenkRozeti renk={k.anlikRenk} /></td>
                <td>
                  <span className={k.baglantiDurumu === "Online" ? "text-durum-yesil" : "text-durum-gri"}>
                    ● {k.baglantiDurumu === "Online" ? "Çevrimiçi" : "Çevrimdışı"}
                  </span>
                </td>
                {yonetici && (
                  <td className="space-x-1 whitespace-nowrap">
                    <button className="dugme-ikincil px-2 py-1 text-xs"
                      onClick={() => {
                        const m = prompt(`${k.kod} — kaç adet giriş yapılacak?`, "10");
                        if (m) calistir(() => api.stokGiris(k.id, Number(m), "Mal kabul"), `${k.kod}: giriş yapıldı.`);
                      }}>+ Giriş</button>
                    <button className="dugme-ikincil px-2 py-1 text-xs"
                      onClick={() => {
                        const m = prompt(`${k.kod} — sayım sonucu kaç adet?`, String(k.adet));
                        if (m) calistir(() => api.stokSayim(k.id, Number(m), "Fiziksel sayım"), `${k.kod}: sayım kaydedildi.`);
                      }}>Sayım</button>
                    <button className="dugme-ikincil px-2 py-1 text-xs"
                      onClick={() => calistir(() => api.ledTest(k.id), `${k.kod}: test ışığı gönderildi.`)}>
                      LED test
                    </button>
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
