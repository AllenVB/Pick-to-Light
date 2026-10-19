"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { ApiHata, api } from "@/lib/api";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import { UrunSecici } from "@/components/UrunSecici";
import type { Urun } from "@/types";

interface Satir { urunId: number; miktar: number }

export default function YeniSiparisSayfasi() {
  const oturum = useKorumaliSayfa();
  const router = useRouter();
  const [urunler, setUrunler] = useState<Urun[]>([]);
  const [siparisNo, setSiparisNo] = useState(onerilenNo());
  const [satirlar, setSatirlar] = useState<Satir[]>([]);
  const [hata, setHata] = useState<string | null>(null);
  const [yukleniyor, setYukleniyor] = useState(false);

  useEffect(() => {
    if (!oturum) return;
    api.urunler().then((u) => {
      setUrunler(u);
      if (u.length) setSatirlar([{ urunId: u[0].id, miktar: 1 }]);
    }).catch(() => setHata("Ürünler yüklenemedi."));
  }, [oturum]);

  function satirDegistir(i: number, alan: keyof Satir, deger: number) {
    setSatirlar((o) => o.map((s, idx) => (idx === i ? { ...s, [alan]: deger } : s)));
  }

  async function olusturVeBaslat(baslat: boolean) {
    setHata(null);
    setYukleniyor(true);
    try {
      const detay = await api.siparisOlustur(siparisNo.trim(), satirlar);
      if (baslat) await api.siparisBaslat(detay.id);
      router.push(`/siparis/${encodeURIComponent(detay.siparisNo)}`);
    } catch (h) {
      setHata(h instanceof ApiHata ? h.message : "Sipariş oluşturulamadı.");
      setYukleniyor(false);
    }
  }

  if (!oturum) return null;

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <header>
        <p className="etiket">Sipariş Girişi</p>
        <h1 className="baslik mt-1">Yeni sipariş</h1>
        <p className="mt-1 text-sm text-slate-400">
          Sipariş başlatıldığında sistem her kalem için kutuyu seçer, stoğu rezerve eder ve
          ilgili kutunun ışığını yakar. Stok yetersizse ışık yanmaz, alarm üretilir.
        </p>
      </header>

      <div className="kart space-y-4">
        <div>
          <label className="etiket">Sipariş numarası</label>
          <input className="girdi mt-1" value={siparisNo} onChange={(e) => setSiparisNo(e.target.value)} />
        </div>

        <div className="space-y-2">
          <label className="etiket">Kalemler</label>
          {satirlar.map((s, i) => {
            const urun = urunler.find((u) => u.id === s.urunId);
            return (
              <div key={i} className="flex items-center gap-2">
                <UrunSecici urunler={urunler} secili={s.urunId}
                  onSec={(urunId) => satirDegistir(i, "urunId", urunId)} />
                <input className="girdi w-24" type="number" min={1} value={s.miktar}
                  onChange={(e) => satirDegistir(i, "miktar", Math.max(1, Number(e.target.value)))} />
                <button className="dugme-ikincil px-3" title="Kalemi sil"
                  onClick={() => setSatirlar((o) => o.filter((_, idx) => idx !== i))}>✕</button>
                {urun && s.miktar > urun.toplamStok && (
                  <span className="whitespace-nowrap text-xs font-bold text-durum-kirmizi">stok yetmez</span>
                )}
              </div>
            );
          })}

          <button className="dugme-ikincil" disabled={!urunler.length}
            onClick={() => setSatirlar((o) => [...o, { urunId: urunler[0].id, miktar: 1 }])}>
            + Kalem ekle
          </button>
        </div>

        {hata && (
          <p className="rounded-lg border border-durum-kirmizi/40 bg-durum-kirmizi/10 px-3 py-2 text-sm text-durum-kirmizi">
            {hata}
          </p>
        )}

        <div className="flex gap-2">
          <button className="dugme" disabled={yukleniyor || !satirlar.length || !siparisNo.trim()}
            onClick={() => olusturVeBaslat(true)}>
            {yukleniyor ? "İşleniyor…" : "Oluştur ve başlat (ışıkları yak)"}
          </button>
          <button className="dugme-ikincil" disabled={yukleniyor || !satirlar.length}
            onClick={() => olusturVeBaslat(false)}>
            Sadece oluştur
          </button>
        </div>
      </div>
    </div>
  );
}

function onerilenNo() {
  const d = new Date();
  const p = (n: number) => String(n).padStart(2, "0");
  return `SIP-${d.getFullYear()}-${p(d.getMonth() + 1)}${p(d.getDate())}-${p(d.getHours())}${p(d.getMinutes())}`;
}
