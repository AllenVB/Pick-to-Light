"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import type { Urun } from "@/types";

/**
 * Yazarak arama yapılan ürün seçici.
 *
 * Açılır liste yüzlerce üründe kullanılamaz; burada SKU veya ürün adının bir
 * parçası yazılır, liste süzülür. Barkod okuyucular bir klavye gibi davranıp
 * SKU'yu yazıp Enter'a bastığı için, tam SKU eşleşmesinde Enter doğrudan seçer —
 * ayrıca fareye gerek kalmaz.
 */
export function UrunSecici({
  urunler,
  secili,
  onSec,
}: {
  urunler: Urun[];
  secili: number;
  onSec: (urunId: number) => void;
}) {
  const [acik, setAcik] = useState(false);
  const [yazi, setYazi] = useState("");
  const [vurgu, setVurgu] = useState(0);
  const kutuRef = useRef<HTMLDivElement>(null);

  const seciliUrun = urunler.find((u) => u.id === secili);

  const sonuclar = useMemo(() => {
    const q = yazi.trim().toLocaleLowerCase("tr");
    if (!q) return urunler.slice(0, 50);
    return urunler
      .filter((u) => `${u.sku} ${u.ad}`.toLocaleLowerCase("tr").includes(q))
      .slice(0, 50);
  }, [urunler, yazi]);

  useEffect(() => setVurgu(0), [yazi]);

  // Dışarı tıklayınca kapan
  useEffect(() => {
    function disariTikla(o: MouseEvent) {
      if (kutuRef.current && !kutuRef.current.contains(o.target as Node)) setAcik(false);
    }
    document.addEventListener("mousedown", disariTikla);
    return () => document.removeEventListener("mousedown", disariTikla);
  }, []);

  function sec(urun: Urun) {
    onSec(urun.id);
    setYazi("");
    setAcik(false);
  }

  function tusaBas(o: React.KeyboardEvent<HTMLInputElement>) {
    if (o.key === "Enter") {
      o.preventDefault();
      // Barkod okuyucu tam SKU yazıp Enter'a basar: önce birebir eşleşmeye bak.
      const q = yazi.trim().toLocaleLowerCase("tr");
      const tamEslesme = urunler.find((u) => u.sku.toLocaleLowerCase("tr") === q);
      const hedef = tamEslesme ?? sonuclar[vurgu];
      if (hedef) sec(hedef);
      return;
    }
    if (o.key === "ArrowDown") { o.preventDefault(); setVurgu((v) => Math.min(v + 1, sonuclar.length - 1)); }
    if (o.key === "ArrowUp") { o.preventDefault(); setVurgu((v) => Math.max(v - 1, 0)); }
    if (o.key === "Escape") { setAcik(false); setYazi(""); }
  }

  return (
    <div className="relative flex-1" ref={kutuRef}>
      <input
        className="girdi w-full"
        data-test="urun-secici"
        value={acik ? yazi : seciliUrun ? `${seciliUrun.ad} (${seciliUrun.sku})` : ""}
        placeholder="SKU veya ürün adı yazın / barkod okutun"
        onFocus={() => { setAcik(true); setYazi(""); }}
        onChange={(e) => { setYazi(e.target.value); setAcik(true); }}
        onKeyDown={tusaBas}
      />

      {acik && (
        <ul className="absolute z-20 mt-1 max-h-64 w-full overflow-auto rounded-lg border border-zemin-cizgi
                       bg-zemin-kart shadow-xl" data-test="urun-sonuclari">
          {sonuclar.map((u, i) => (
            <li key={u.id}>
              <button
                type="button"
                className={`flex w-full items-center justify-between gap-3 px-3 py-2 text-left text-sm
                            ${i === vurgu ? "bg-marka/20" : "hover:bg-zemin-cizgi/40"}`}
                onMouseEnter={() => setVurgu(i)}
                onClick={() => sec(u)}>
                <span>
                  <span className="font-semibold">{u.ad}</span>
                  <span className="ml-2 font-mono text-xs text-slate-400">{u.sku}</span>
                </span>
                <span className={`whitespace-nowrap text-xs ${u.toplamStok > 0 ? "text-slate-400" : "text-durum-kirmizi"}`}>
                  {u.toplamStok > 0 ? `stok ${u.toplamStok}` : "stok yok"}
                </span>
              </button>
            </li>
          ))}

          {sonuclar.length === 0 && (
            <li className="px-3 py-2 text-sm text-slate-500">&ldquo;{yazi}&rdquo; ile eşleşen ürün yok.</li>
          )}
        </ul>
      )}
    </div>
  );
}
