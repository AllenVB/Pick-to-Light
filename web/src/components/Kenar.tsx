"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { api, oturumOku, oturumSil } from "@/lib/api";
import { useCanliOlaylar } from "@/hooks/useCanliOlaylar";

const BAGLANTILAR = [
  { yol: "/", ad: "Canlı Pano", ikon: "▦" },
  { yol: "/siparis/yeni", ad: "Sipariş Girişi", ikon: "＋" },
  { yol: "/siparisler", ad: "Siparişler", ikon: "☰" },
  { yol: "/stok", ad: "Stok Yönetimi", ikon: "▤" },
  { yol: "/alarmlar", ad: "Alarmlar", ikon: "⚠" },
  { yol: "/hareketler", ad: "Hareket Defteri", ikon: "⇅" },
];

export function Kenar() {
  const yol = usePathname();
  const router = useRouter();
  const [adSoyad, setAdSoyad] = useState<string | null>(null);
  const [okunmamis, setOkunmamis] = useState(0);

  const alarmlariSay = () =>
    api.alarmlar(false).then((a) => setOkunmamis(a.length)).catch(() => setOkunmamis(0));

  useEffect(() => {
    const o = oturumOku();
    setAdSoyad(o?.adSoyad ?? null);
    if (o) alarmlariSay();
  }, [yol]);

  useCanliOlaylar({ AlarmOlustu: () => alarmlariSay() });

  if (yol === "/giris") return null;

  return (
    <aside className="flex w-60 shrink-0 flex-col border-r border-zemin-cizgi bg-zemin-kart/40 p-4">
      <div className="mb-6">
        <div className="flex items-center gap-2">
          <span className="h-3 w-3 rounded-full bg-durum-yesil" />
          <span className="h-3 w-3 rounded-full bg-durum-sari" />
          <span className="h-3 w-3 rounded-full bg-durum-kirmizi" />
        </div>
        <h1 className="mt-3 text-lg font-extrabold leading-tight">Pick-to-Light</h1>
        <p className="text-xs text-slate-400">Akıllı Depo Yönetimi</p>
      </div>

      <nav className="flex-1 space-y-1">
        {BAGLANTILAR.map((b) => {
          const aktif = yol === b.yol || (b.yol !== "/" && yol.startsWith(b.yol));
          return (
            <Link
              key={b.yol}
              href={b.yol}
              className={`flex items-center justify-between rounded-lg px-3 py-2 text-sm transition ${
                aktif ? "bg-marka/20 font-semibold text-marka-acik" : "text-slate-300 hover:bg-zemin-kart"
              }`}
            >
              <span>
                <span className="mr-2 opacity-70">{b.ikon}</span>
                {b.ad}
              </span>
              {b.yol === "/alarmlar" && okunmamis > 0 && (
                <span className="rounded-full bg-durum-kirmizi px-2 py-0.5 text-[10px] font-bold">
                  {okunmamis}
                </span>
              )}
            </Link>
          );
        })}
      </nav>

      <div className="mt-4 border-t border-zemin-cizgi pt-4 text-xs">
        <p className="text-slate-400">Oturum</p>
        <p className="font-semibold">{adSoyad ?? "—"}</p>
        <button
          onClick={() => { oturumSil(); router.replace("/giris"); }}
          className="mt-2 text-slate-400 underline hover:text-slate-200"
        >
          Çıkış yap
        </button>
      </div>
    </aside>
  );
}
