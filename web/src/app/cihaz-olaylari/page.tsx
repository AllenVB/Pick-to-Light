"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import { useCanliOlaylar } from "@/hooks/useCanliOlaylar";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import type { CihazOlay, Kutu } from "@/types";

/** Topic'in son parçasına göre renk: komut / olay / durum / telemetri. */
const TOPIC_SINIF: Record<string, string> = {
  olay: "text-marka-acik",
  durum: "text-durum-yesil",
  telemetri: "text-slate-400",
  komut: "text-durum-sari",
};

export default function CihazOlaylariSayfasi() {
  const oturum = useKorumaliSayfa();
  const [olaylar, setOlaylar] = useState<CihazOlay[]>([]);
  const [kutular, setKutular] = useState<Kutu[]>([]);
  const [kutuId, setKutuId] = useState<number | "">("");
  const [otomatik, setOtomatik] = useState(true);

  const yenile = useCallback(() => {
    api.cihazOlaylari(kutuId === "" ? undefined : kutuId, 150)
      .then(setOlaylar)
      .catch(() => setOlaylar([]));
  }, [kutuId]);

  useEffect(() => { if (oturum) { yenile(); api.kutular().then(setKutular).catch(() => undefined); } },
    [oturum, yenile]);

  // Cihazdan mesaj geldikçe liste tazelenir.
  useCanliOlaylar({
    KalemTeyitEdildi: () => otomatik && yenile(),
    CihazDurumDegisti: () => otomatik && yenile(),
    KutuIsikDegisti: () => otomatik && yenile(),
  }, [kutuId, otomatik]);

  if (!oturum) return null;

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <p className="etiket">Hata Ayıklama</p>
          <h1 className="baslik mt-1">Cihaz olayları (ham MQTT)</h1>
          <p className="mt-1 text-sm text-slate-400">
            Kutulardan gelen mesajlar olduğu gibi saklanır. &quot;Işık neden yanmadı?&quot;,
            &quot;teyit neden işlenmedi?&quot; sorularının cevabı burada aranır.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <select className="girdi w-44" value={kutuId}
            onChange={(e) => setKutuId(e.target.value ? Number(e.target.value) : "")}>
            <option value="">Tüm kutular</option>
            {kutular.map((k) => <option key={k.id} value={k.id}>{k.kod}</option>)}
          </select>
          <label className="flex items-center gap-1 text-xs text-slate-300">
            <input type="checkbox" checked={otomatik} onChange={(e) => setOtomatik(e.target.checked)} />
            canlı
          </label>
          <button className="dugme-ikincil px-3 py-2 text-xs" onClick={yenile}>Yenile</button>
        </div>
      </header>

      <section className="kart overflow-x-auto">
        <table className="tablo">
          <thead><tr><th>Zaman</th><th>Kutu</th><th>Topic</th><th>Payload</th></tr></thead>
          <tbody>
            {olaylar.map((o) => {
              const son = o.topic.split("/").pop() ?? "";
              return (
                <tr key={o.id}>
                  <td className="whitespace-nowrap text-xs text-slate-400">
                    {new Date(o.zaman).toLocaleString("tr-TR")}
                  </td>
                  <td className="font-mono text-xs">{o.kutuKod ?? "—"}</td>
                  <td className={`font-mono text-[11px] ${TOPIC_SINIF[son] ?? ""}`}>{o.topic}</td>
                  <td className="max-w-xl truncate font-mono text-[11px] text-slate-300" title={o.payload}>
                    {o.payload}
                  </td>
                </tr>
              );
            })}
            {olaylar.length === 0 && (
              <tr><td colSpan={4} className="text-slate-500">
                Kayıt yok. Kutular bağlandığında (veya simülatör çalıştığında) mesajlar buraya düşer.
              </td></tr>
            )}
          </tbody>
        </table>
      </section>

      <p className="text-xs text-slate-500">
        Kutu listesi ve LED testi için <Link href="/kutular" className="text-marka-acik hover:underline">Kutu &amp; Cihaz</Link> sayfasına bakın.
      </p>
    </div>
  );
}
