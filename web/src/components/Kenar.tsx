"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { api, oturumOku, oturumSil } from "@/lib/api";
import { useCanliOlaylar } from "@/hooks/useCanliOlaylar";
import { TemaDugmesi } from "@/components/Tema";

const BAGLANTILAR = [
  { yol: "/", ad: "Canlı Pano", kisa: "Pano", ikon: "▦" },
  { yol: "/siparis/yeni", ad: "Sipariş Girişi", kisa: "Yeni", ikon: "＋" },
  { yol: "/siparis/ice-aktar", ad: "Toplu Aktarma", kisa: "Aktar", ikon: "⇪" },
  { yol: "/siparisler", ad: "Siparişler", kisa: "Siparişler", ikon: "☰" },
  { yol: "/operator", ad: "Operatör Ekranı", kisa: "Operatör", ikon: "◎" },
  { yol: "/stok", ad: "Stok Yönetimi", kisa: "Stok", ikon: "▤" },
  { yol: "/urunler", ad: "Ürünler", kisa: "Ürün", ikon: "◫" },
  { yol: "/kutular", ad: "Kutu & Cihaz", kisa: "Kutu", ikon: "▣" },
  { yol: "/alarmlar", ad: "Alarmlar", kisa: "Alarm", ikon: "⚠" },
  { yol: "/hareketler", ad: "Hareket Defteri", kisa: "Defter", ikon: "⇅" },
  { yol: "/raporlar", ad: "Raporlar", kisa: "Rapor", ikon: "◴" },
  { yol: "/cihaz-olaylari", ad: "Cihaz Olayları", kisa: "Cihaz", ikon: "⟳" },
  { yol: "/personeller", ad: "Personel", kisa: "Personel", ikon: "☺" },
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

  // Giriş ve operatör (kiosk) ekranında kenar çubuğu gösterilmez.
  if (yol === "/giris" || yol === "/operator") return null;

  return (
    <aside className="flex shrink-0 flex-col border-b border-zemin-cizgi bg-zemin-kart/40 p-4
                      lg:w-60 lg:border-b-0 lg:border-r">
      <div className="mb-3 flex items-center justify-between lg:mb-6 lg:block">
        {/* Marka işareti: sistemin üç LED rengi — fiziksel ışığı temsil eder. */}
        <div className="flex items-center gap-2">
          <span className="h-3 w-3 rounded-full bg-led-yesil" />
          <span className="h-3 w-3 rounded-full bg-led-sari" />
          <span className="h-3 w-3 rounded-full bg-led-kirmizi" />
        </div>
        <div className="lg:mt-3">
          <h1 className="text-lg font-extrabold leading-tight">Pick-to-Light</h1>
          <p className="hidden text-xs text-slate-400 lg:block">Akıllı Depo Yönetimi</p>
        </div>

        {/* Küçük ekranda oturum bilgisi başlıkta durur */}
        <div className="text-right text-[11px] text-slate-400 lg:hidden">
          <p className="font-semibold text-slate-200">{adSoyad ?? "—"}</p>
          <button onClick={() => { oturumSil(); router.replace("/giris"); }} className="underline">
            Çıkış
          </button>
        </div>
      </div>

      <nav className="flex gap-1 overflow-x-auto pb-1 lg:flex-1 lg:flex-col lg:gap-0 lg:space-y-1 lg:overflow-visible lg:pb-0">
        {BAGLANTILAR.map((b) => {
          const aktif = yol === b.yol || (b.yol !== "/" && yol.startsWith(b.yol));
          return (
            <Link
              key={b.yol}
              href={b.yol}
              className={`flex shrink-0 items-center justify-between gap-2 whitespace-nowrap rounded-lg px-3 py-2 text-sm transition lg:shrink ${
                aktif ? "bg-marka/20 font-semibold text-marka-metin" : "text-slate-300 hover:bg-zemin-kart"
              }`}
            >
              <span>
                <span className="mr-2">{b.ikon}</span>
                <span className="hidden sm:inline lg:inline">{b.ad}</span>
                <span className="sm:hidden">{b.kisa}</span>
              </span>
              {/* text-white açıkça verilir: devralınan gri, kırmızı dolgu üstünde okunmuyordu. */}
              {b.yol === "/alarmlar" && okunmamis > 0 && (
                <span className="rounded-full bg-dolgu-kirmizi px-2 py-0.5 text-[10px] font-bold text-white">
                  {okunmamis}
                </span>
              )}
            </Link>
          );
        })}
      </nav>

      <div className="mt-4 hidden border-t border-zemin-cizgi pt-4 text-xs lg:block">
        <TemaDugmesi />

        <p className="mt-4 text-slate-400">Oturum</p>
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
