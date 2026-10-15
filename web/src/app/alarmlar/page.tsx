"use client";

import { useCallback, useEffect, useState } from "react";
import { api } from "@/lib/api";
import { useCanliOlaylar } from "@/hooks/useCanliOlaylar";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import type { Alarm, TedarikTalebi } from "@/types";

const TIP_RENK: Record<string, string> = {
  StokYok: "border-durum-kirmizi/40 bg-durum-kirmizi/10 text-durum-kirmizi",
  StokAzaldi: "border-durum-sari/40 bg-durum-sari/10 text-durum-sari",
  CihazOffline: "border-slate-500/40 bg-slate-500/10 text-slate-300",
  TeyitZamanAsimi: "border-durum-sari/40 bg-durum-sari/10 text-durum-sari",
  BeklenmeyenTeyit: "border-durum-kirmizi/40 bg-durum-kirmizi/10 text-durum-kirmizi",
  AgirlikUyusmazligi: "border-durum-sari/40 bg-durum-sari/10 text-durum-sari",
};

export default function AlarmlarSayfasi() {
  const oturum = useKorumaliSayfa();
  const [alarmlar, setAlarmlar] = useState<Alarm[]>([]);
  const [talepler, setTalepler] = useState<TedarikTalebi[]>([]);
  const [sadeceOkunmamis, setSadeceOkunmamis] = useState(true);

  const yenile = useCallback(() => {
    api.alarmlar(sadeceOkunmamis ? false : undefined).then(setAlarmlar).catch(() => setAlarmlar([]));
    api.tedarikTalepleri().then(setTalepler).catch(() => setTalepler([]));
  }, [sadeceOkunmamis]);

  useEffect(() => { if (oturum) yenile(); }, [oturum, yenile]);
  useCanliOlaylar({ AlarmOlustu: yenile }, [sadeceOkunmamis]);

  if (!oturum) return null;

  return (
    <div className="space-y-6">
      <header className="flex items-end justify-between">
        <div>
          <p className="etiket">Alarmlar</p>
          <h1 className="baslik mt-1">Uyarı ve alarm paneli</h1>
        </div>
        <label className="flex items-center gap-2 text-sm text-slate-300">
          <input type="checkbox" checked={sadeceOkunmamis}
            onChange={(e) => setSadeceOkunmamis(e.target.checked)} />
          Sadece okunmamışlar
        </label>
      </header>

      <section className="space-y-2">
        {alarmlar.map((a) => (
          <div key={a.id}
            className={`flex items-start justify-between gap-4 rounded-lg border px-4 py-3 text-sm ${
              TIP_RENK[a.tip] ?? "border-zemin-cizgi bg-zemin-kart"}`}>
            <div>
              <p className="font-bold">{a.tip}</p>
              <p className="text-slate-200">{a.mesaj}</p>
              <p className="mt-1 text-[11px] text-slate-500">
                {new Date(a.zaman).toLocaleString("tr-TR")}
                {a.kutuKod ? ` · ${a.kutuKod}` : ""}
              </p>
            </div>
            {!a.okundu && (
              <button className="dugme-ikincil shrink-0 px-3 py-1 text-xs"
                onClick={() => api.alarmOkundu(a.id).then(yenile)}>Okundu</button>
            )}
          </div>
        ))}
        {alarmlar.length === 0 && <p className="text-sm text-slate-500">Gösterilecek alarm yok. ✔</p>}
      </section>

      <section className="kart">
        <h2 className="etiket mb-3">Tedarik talepleri</h2>
        <table className="tablo">
          <thead><tr><th>SKU</th><th>Ürün</th><th>Talep</th><th>Durum</th><th>Açılış</th></tr></thead>
          <tbody>
            {talepler.map((t) => (
              <tr key={t.id}>
                <td className="font-mono text-xs">{t.sku}</td>
                <td>{t.urunAdi}</td>
                <td className="font-bold">{t.talepMiktari}</td>
                <td>{t.durum}</td>
                <td className="text-xs text-slate-400">{new Date(t.olusturma).toLocaleString("tr-TR")}</td>
              </tr>
            ))}
            {talepler.length === 0 && <tr><td colSpan={5} className="text-slate-500">Açık talep yok.</td></tr>}
          </tbody>
        </table>
      </section>
    </div>
  );
}
