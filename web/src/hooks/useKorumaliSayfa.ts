"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { oturumOku } from "@/lib/api";
import type { Oturum, PersonelRolu } from "@/types";

/**
 * Oturum yoksa giriş sayfasına yönlendirir.
 *
 * `gerekliRol` verilirse rolü yetmeyen kullanıcı panoya gönderilir ve oturum
 * dönmez; sayfa verisini çekmeden hiçbir şey çizilmez. Asıl yetki denetimi
 * backend'dedir (403) — bu yalnızca kullanıcıyı erişemeyeceği bir sayfada bırakmaz.
 */
export function useKorumaliSayfa(gerekliRol?: PersonelRolu): Oturum | null {
  const router = useRouter();
  const [oturum, setOturum] = useState<Oturum | null>(null);

  useEffect(() => {
    const mevcut = oturumOku();
    if (!mevcut) router.replace("/giris");
    else if (gerekliRol && mevcut.rol !== gerekliRol) router.replace("/");
    else setOturum(mevcut);
  }, [router, gerekliRol]);

  return oturum;
}
