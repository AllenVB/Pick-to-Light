"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { api, oturumOku, oturumSil } from "@/lib/api";
import { useCanliOlaylar } from "@/hooks/useCanliOlaylar";
import { TemaDugmesi } from "@/components/Tema";
import { BOLUMLER, bolumBul } from "@/lib/menu";
import type { PersonelRolu } from "@/types";

/**
 * Yan menü yalnızca dört ana bölümü gösterir (Sipariş Kontrol Paneli, Canlı Pano, Stok Durumu, Paketleme
 * İşlemleri). Bölümün içindeki sayfalar sayfa üstündeki sekme çubuğundan (AltMenu) açılır. Yönetici
 * sayfaları operatöre gösterilmez; bu ayrım backend yetkileriyle (`[Authorize(Roles = "ADMIN")]`) aynıdır.
 */
export function Kenar() {
  const yol = usePathname();
  const router = useRouter();
  const [adSoyad, setAdSoyad] = useState<string | null>(null);
  const [rol, setRol] = useState<PersonelRolu | null>(null);
  const [okunmamis, setOkunmamis] = useState(0);

  const alarmlariSay = () =>
    api.alarmlar(false).then((a) => setOkunmamis(a.length)).catch(() => setOkunmamis(0));

  useEffect(() => {
    const o = oturumOku();
    setAdSoyad(o?.adSoyad ?? null);
    setRol(o?.rol ?? null);
    if (o) alarmlariSay();
  }, [yol]);

  useCanliOlaylar({ AlarmOlustu: () => alarmlariSay() });

  // Giriş ve operatör (kiosk) ekranında kenar çubuğu gösterilmez.
  if (yol === "/giris" || yol === "/operator") return null;

  const yonetici = rol === "Admin";
  const rolAdi = yonetici ? "Yönetici" : "Operatör";
  const aktifBolum = bolumBul(yol)?.id;

  return (
    <aside className="flex shrink-0 flex-col border-b border-zemin-cizgi bg-zemin-kart p-4
                      lg:w-64 lg:border-b-0 lg:border-r">
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

      <nav className="flex gap-2 overflow-x-auto pb-1 lg:flex-1 lg:flex-col lg:overflow-visible lg:pb-0" data-test="ana-menu">
        {BOLUMLER.map((b) => {
          const aktif = b.id === aktifBolum;
          return (
            <Link key={b.id} href={b.giris} data-test="ana-bolum" data-bolum={b.id} data-aktif={aktif}
              aria-current={aktif ? "page" : undefined}
              className={`flex shrink-0 items-start justify-between gap-3 rounded-lg border px-3 py-3 transition ${
                aktif
                  ? "border-zemin-cizgi bg-marka-koyu font-semibold text-slate-50"
                  : "border-transparent text-slate-300 hover:bg-zemin"}`}>
              <span className="flex items-start gap-3">
                <span className="mt-0.5 text-base leading-none" aria-hidden>{b.ikon}</span>
                <span>
                  <span className="block whitespace-nowrap text-sm leading-tight">{b.ad}</span>
                  <span className="mt-0.5 hidden text-xs font-normal leading-snug text-slate-400 lg:block">
                    {b.aciklama}
                  </span>
                </span>
              </span>
              {/* text-white açıkça verilir: devralınan gri, kırmızı dolgu üstünde okunmuyordu. */}
              {b.id === "pano" && okunmamis > 0 && (
                <span className="rounded-full bg-dolgu-kirmizi px-2 py-0.5 text-[10px] font-bold text-white"
                  data-test="alarm-sayaci">
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
        {rol && (
          <span className={`rozet mt-1 ${yonetici ? "bg-marka/15 text-marka-metin" : "bg-slate-600/15 text-slate-300"}`}
            data-test="rol-rozeti">{rolAdi}</span>
        )}
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
