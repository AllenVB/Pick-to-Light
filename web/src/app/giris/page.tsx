"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { ApiHata, api, oturumYaz } from "@/lib/api";

export default function GirisSayfasi() {
  const router = useRouter();
  const [kullaniciAdi, setKullaniciAdi] = useState("operator");
  const [parola, setParola] = useState("");
  const [hata, setHata] = useState<string | null>(null);
  const [yukleniyor, setYukleniyor] = useState(false);

  async function gonder(e: React.FormEvent) {
    e.preventDefault();
    setHata(null);
    setYukleniyor(true);
    try {
      oturumYaz(await api.giris(kullaniciAdi, parola));
      router.replace("/");
    } catch (h) {
      setHata(h instanceof ApiHata ? h.message : "Giriş yapılamadı. Backend çalışıyor mu?");
    } finally {
      setYukleniyor(false);
    }
  }

  return (
    <div className="flex min-h-[85vh] items-center justify-center">
      <form onSubmit={gonder} className="kart w-full max-w-sm space-y-4">
        <div>
          <div className="flex gap-2">
            <span className="h-3 w-3 rounded-full bg-durum-yesil" />
            <span className="h-3 w-3 rounded-full bg-durum-sari" />
            <span className="h-3 w-3 rounded-full bg-durum-kirmizi" />
          </div>
          <h1 className="baslik mt-3">Pick-to-Light</h1>
          <p className="text-sm text-slate-400">Depo yönetim paneline giriş</p>
        </div>

        <div>
          <label className="etiket">Kullanıcı adı</label>
          <input className="girdi mt-1" value={kullaniciAdi}
            onChange={(e) => setKullaniciAdi(e.target.value)} autoComplete="username" />
        </div>

        <div>
          <label className="etiket">Parola</label>
          <input className="girdi mt-1" type="password" value={parola}
            onChange={(e) => setParola(e.target.value)} autoComplete="current-password" />
        </div>

        {hata && (
          <p className="rounded-lg border border-durum-kirmizi/40 bg-durum-kirmizi/10 px-3 py-2 text-sm text-durum-kirmizi">
            {hata}
          </p>
        )}

        <button className="dugme w-full" disabled={yukleniyor}>
          {yukleniyor ? "Giriş yapılıyor…" : "Giriş yap"}
        </button>

        <p className="text-center text-[11px] text-slate-500">
          Demo hesapları: <b>operator / operator123</b> · <b>admin / admin123</b>
        </p>
      </form>
    </div>
  );
}
