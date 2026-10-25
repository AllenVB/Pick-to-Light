"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import { useCanliOlaylar } from "@/hooks/useCanliOlaylar";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import { BosDurum, IskeletSatir } from "@/components/Durumlar";
import { Kopyala } from "@/components/Cihaz";
import type { CihazOlay, Kutu } from "@/types";

/** Topic'in son parçasına göre renk: komut / olay / durum / telemetri. */
const TOPIC_SINIF: Record<string, string> = {
  olay: "text-marka-metin",
  durum: "text-durum-yesil",
  telemetri: "text-slate-400",
  komut: "text-durum-sari",
};

const TUR_ETIKETI: Record<string, string> = {
  komut: "sunucu → cihaz",
  olay: "cihaz → sunucu",
  durum: "kalp atışı / LWT",
  telemetri: "ölçüm",
};

/**
 * Payload içindeki anahtar alanları okunur kılar.
 * Ham JSON'u gözle taramak yerine tip, renk, miktar gibi alanlar öne çıkar.
 */
function ozet(payload: string): { etiket: string; deger: string }[] {
  try {
    const n = JSON.parse(payload) as Record<string, unknown>;
    const ilgi = ["tip", "renk", "miktar", "yontem", "baglanti", "fw", "rssi", "ip", "kalemId"];

    return ilgi
      .filter((a) => n[a] !== undefined && n[a] !== null)
      .map((a) => ({ etiket: a, deger: String(n[a]) }));
  } catch {
    return [];
  }
}

export default function CihazOlaylariSayfasi() {
  const oturum = useKorumaliSayfa("Admin");
  const [olaylar, setOlaylar] = useState<CihazOlay[]>([]);
  const [kutular, setKutular] = useState<Kutu[]>([]);
  const [kutuId, setKutuId] = useState<number | "">("");
  const [tur, setTur] = useState("");
  const [otomatik, setOtomatik] = useState(true);
  const [yukleniyor, setYukleniyor] = useState(true);
  const [acikSatir, setAcikSatir] = useState<number | null>(null);

  const yenile = useCallback(() => {
    api.cihazOlaylari(kutuId === "" ? undefined : kutuId, 200)
      .then(setOlaylar)
      .catch(() => setOlaylar([]))
      .finally(() => setYukleniyor(false));
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

  // Tür süzmesi istemci tarafında: kayıtlar zaten sayfalı geliyor.
  const gosterilecek = tur
    ? olaylar.filter((o) => (o.topic.split("/").pop() ?? "") === tur)
    : olaylar;

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
          <select className="girdi w-40" value={kutuId}
            onChange={(e) => setKutuId(e.target.value ? Number(e.target.value) : "")}>
            <option value="">Tüm kutular</option>
            {kutular.map((k) => <option key={k.id} value={k.id}>{k.kod}</option>)}
          </select>

          <select className="girdi w-48" value={tur} data-test="olay-turu"
            onChange={(e) => setTur(e.target.value)}>
            <option value="">Tüm mesajlar</option>
            <option value="komut">Komut (sunucu → cihaz)</option>
            <option value="olay">Olay (cihaz → sunucu)</option>
            <option value="durum">Durum (kalp atışı / LWT)</option>
            <option value="telemetri">Telemetri</option>
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
          <thead>
            <tr><th className="w-36">Zaman</th><th className="w-20">Kutu</th><th>Topic</th>
              <th>Mesaj</th></tr>
          </thead>
          <tbody>
            {yukleniyor && <><IskeletSatir sutun={4} /><IskeletSatir sutun={4} /><IskeletSatir sutun={4} /></>}

            {!yukleniyor && gosterilecek.map((o) => {
              const son = o.topic.split("/").pop() ?? "";
              const alanlar = ozet(o.payload);
              const acik = acikSatir === o.id;

              return (
                <tr key={o.id} className="cursor-pointer" data-test="olay-satiri"
                  onClick={() => setAcikSatir(acik ? null : o.id)}>
                  <td className="whitespace-nowrap text-xs text-slate-400">
                    {new Date(o.zaman).toLocaleTimeString("tr-TR")}
                    <span className="ml-1 text-slate-600">
                      {new Date(o.zaman).toLocaleDateString("tr-TR")}
                    </span>
                  </td>
                  <td className="font-mono text-xs">{o.kutuKod ?? "—"}</td>
                  <td className={`font-mono text-[11px] ${TOPIC_SINIF[son] ?? ""}`}>
                    {o.topic}
                    <span className="ml-2 text-[10px] text-slate-600">{TUR_ETIKETI[son] ?? ""}</span>
                  </td>
                  <td className="font-mono text-[11px] text-slate-300">
                    {acik ? (
                      <pre className="whitespace-pre-wrap break-all rounded-lg bg-zemin/70 p-2">
                        {(() => { try { return JSON.stringify(JSON.parse(o.payload), null, 2); }
                                  catch { return o.payload; } })()}
                      </pre>
                    ) : alanlar.length > 0 ? (
                      <span className="flex flex-wrap gap-x-3 gap-y-1">
                        {alanlar.map((a) => (
                          <span key={a.etiket}>
                            <span className="text-slate-500">{a.etiket}=</span>
                            <span className="text-slate-200">{a.deger}</span>
                          </span>
                        ))}
                      </span>
                    ) : (
                      <span className="line-clamp-1 break-all">{o.payload}</span>
                    )}
                  </td>
                </tr>
              );
            })}

            {!yukleniyor && gosterilecek.length === 0 && (
              <tr><td colSpan={4}>
                <BosDurum simge="⟳" baslik="Henüz mesaj yok"
                  aciklama="Kutular bağlandığında (veya kutu simülatörü çalıştığında) gelen ve giden
                            her MQTT mesajı buraya düşer. ESP32 kurarken ilk bakılacak yer burasıdır."
                  eylem={<Kopyala deger="mosquitto_sub -h localhost -t 'depo/#' -v"
                    etiket="mosquitto_sub -h localhost -t 'depo/#' -v" />} />
              </td></tr>
            )}
          </tbody>
        </table>
      </section>

      <p className="ipucu">
        {gosterilecek.length} mesaj gösteriliyor · satıra tıklayınca ham JSON açılır.
      </p>

      <p className="text-xs text-slate-500">
        Kutu listesi ve LED testi için <Link href="/kutular" className="text-marka-metin hover:underline">Kutu &amp; Cihaz</Link> sayfasına bakın.
      </p>
    </div>
  );
}
