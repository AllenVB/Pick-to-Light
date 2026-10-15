"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { oturumOku } from "@/lib/api";
import type { Oturum } from "@/types";

/** Oturum yoksa giriş sayfasına yönlendirir. */
export function useKorumaliSayfa(): Oturum | null {
  const router = useRouter();
  const [oturum, setOturum] = useState<Oturum | null>(null);

  useEffect(() => {
    const mevcut = oturumOku();
    if (!mevcut) router.replace("/giris");
    else setOturum(mevcut);
  }, [router]);

  return oturum;
}
