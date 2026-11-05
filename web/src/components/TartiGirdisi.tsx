"use client";

import { useState } from "react";
import { gramMetni } from "@/components/Baskul";
import type { Kutu } from "@/types";

/**
 * Tartı girdisi: gram ya elle girilir ya da bir kutunun baskülünden canlı okunur.
 *
 * Kutu baskülü içindeki TÜM parçaları tartar; paket tartılacaksa önce "Tartıyı sıfırla" ile o anki ağırlık
 * dara olarak düşülür. Tartı & Sayım ve Paketleme sayfaları aynı bileşeni kullanır.
 */
export function useTartiGirdisi(kutular: Kutu[]) {
  const [kaynak, setKaynak] = useState<number | "elle">("elle");
  const [elleGram, setElleGram] = useState("");
  const [tare, setTare] = useState(0);
  const [kap, setKap] = useState("");

  const elle = kaynak === "elle";
  const tartiKutu = elle ? undefined : kutular.find((k) => k.id === kaynak);
  const brut = elle
    ? (elleGram.trim() === "" ? null : Number(elleGram.replace(",", ".")))
    : tartiKutu?.agirlikG ?? null;
  const net = brut != null && Number.isFinite(brut) ? brut - (elle ? 0 : tare) : null;

  const kapSayi = kap.trim() === "" ? null : Number(kap.replace(",", "."));
  const kapG = kapSayi != null && Number.isFinite(kapSayi) && kapSayi > 0 ? kapSayi : null;

  return { kaynak, setKaynak, elleGram, setElleGram, tare, setTare, kap, setKap, kapG, elle, tartiKutu, brut, net };
}

export type TartiDurumu = ReturnType<typeof useTartiGirdisi>;

export function TartiGirdisiKarti({ tarti, kutular, kapAlani = false }: {
  tarti: TartiDurumu;
  kutular: Kutu[];
  /** Paketleme: koli/kap ağırlığı ayrı girilir ve sunucuda netten düşülür. */
  kapAlani?: boolean;
}) {
  const tartili = kutular.filter((k) => k.agirlikG != null);

  return (
    <section className="kart space-y-3" data-test="tarti-karti">
      <h2 className="etiket">Tartı</h2>

      <div>
        <label className="etiket">Tartı kaynağı</label>
        <select className="girdi mt-1" value={tarti.kaynak} data-test="tarti-kaynak"
          onChange={(e) => { tarti.setKaynak(e.target.value === "elle" ? "elle" : Number(e.target.value)); tarti.setTare(0); }}>
          <option value="elle">Gramı elle gireceğim</option>
          {tartili.map((k) => (
            <option key={k.id} value={k.id}>{k.kod} baskülü (şu an {gramMetni(k.agirlikG ?? 0)})</option>
          ))}
        </select>
      </div>

      {tarti.elle ? (
        <div>
          <label className="etiket">Tartıdaki ağırlık (g)</label>
          <input className="girdi mt-1" inputMode="decimal" placeholder="örn. 20" value={tarti.elleGram}
            onChange={(e) => tarti.setElleGram(e.target.value)} data-test="tarti-elle" />
        </div>
      ) : (
        <p className="ipucu">
          Kutu baskülü içindeki <b>tüm</b> parçaları tartar. Paketi koymadan önce <b>Tartıyı sıfırla</b>ya basın,
          sonra paketi koyun; net ağırlık paketin ağırlığı olur.
        </p>
      )}

      {kapAlani && (
        <div>
          <label className="etiket">Koli / kap ağırlığı (g) <span className="normal-case text-slate-500">— opsiyonel</span></label>
          <input className="girdi mt-1" inputMode="decimal" placeholder="örn. 35" value={tarti.kap}
            onChange={(e) => tarti.setKap(e.target.value)} data-test="tarti-kap" />
        </div>
      )}

      <div className="flex items-end justify-between gap-3 border-t border-zemin-cizgi/60 pt-3">
        <div>
          <p className="etiket">Net ağırlık</p>
          <p className="mt-1 font-mono text-3xl font-extrabold tabular-nums" data-test="tarti-net">
            {tarti.net != null ? gramMetni(tarti.net) : "—"}
          </p>
        </div>
        <button className="dugme-ikincil dugme-kucuk" disabled={tarti.elle || tarti.brut == null} hidden={tarti.elle}
          title="Tartı boşken basın: şu anki ağırlık sıfır kabul edilir (kutu/kap ağırlığı düşülür)"
          onClick={() => tarti.setTare(tarti.tartiKutu?.agirlikG ?? 0)} data-test="tarti-sifirla">
          Tartıyı sıfırla
        </button>
      </div>
      {!tarti.elle && tarti.tare > 0 && <p className="ipucu">Dara: {gramMetni(tarti.tare)} düşüldü.</p>}
    </section>
  );
}
