"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useState } from "react";
import { oturumOku } from "@/lib/api";
import { bolumBul, gorunenSayfalar, seciliSayfa } from "@/lib/menu";
import type { PersonelRolu } from "@/types";

/**
 * Bölüm başlığı ve o bölümün sayfaları arasında geçiş sekmeleri. Yan menü yalnızca dört bölümü
 * gösterir; bölümün içindeki sayfalar buradan açılır. Bölümde tek sayfa varsa (operatörde paketleme)
 * sekme çubuğu çizilmez, yalnızca başlık görünür.
 */
export function AltMenu() {
  const yol = usePathname();
  const [rol, setRol] = useState<PersonelRolu | null>(null);

  useEffect(() => { setRol(oturumOku()?.rol ?? null); }, [yol]);

  // Giriş ve operatör (kiosk) ekranında gezinme gösterilmez.
  if (yol === "/giris" || yol === "/operator") return null;

  const bolum = bolumBul(yol);
  if (!bolum) return null;

  const sayfalar = gorunenSayfalar(bolum, rol);
  const secili = seciliSayfa(bolum, yol);

  return (
    <nav className="mb-5" aria-label={bolum.ad} data-test="alt-menu" data-bolum={bolum.id}>
      <p className="etiket" data-test="bolum-basligi">{bolum.ad}</p>
      {sayfalar.length > 1 && (
        <div className="mt-2 flex gap-1 overflow-x-auto border-b border-zemin-cizgi">
          {sayfalar.map((s) => {
            const aktif = s.yol === secili;
            return (
              <Link key={s.yol} href={s.yol} data-test="alt-sayfa" data-aktif={aktif}
                aria-current={aktif ? "page" : undefined}
                className={`-mb-px shrink-0 whitespace-nowrap border-b-2 px-3 py-2 text-sm transition ${
                  aktif
                    ? "border-marka font-semibold text-slate-50"
                    : "border-transparent text-slate-400 hover:border-zemin-cizgi hover:text-slate-100"}`}>
                {s.ad}
              </Link>
            );
          })}
        </div>
      )}
    </nav>
  );
}
