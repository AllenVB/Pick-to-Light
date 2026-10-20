"use client";

import { useCallback, useEffect, useState } from "react";
import { ApiHata, api } from "@/lib/api";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import type { Urun } from "@/types";

interface Form { sku: string; ad: string; birimAgirlikG: string; guvenlikStogu: number }

const BOS: Form = { sku: "", ad: "", birimAgirlikG: "", guvenlikStogu: 5 };

export default function UrunlerSayfasi() {
  const oturum = useKorumaliSayfa();
  const yonetici = oturum?.rol === "Admin";

  const [urunler, setUrunler] = useState<Urun[]>([]);
  const [arsivGoster, setArsivGoster] = useState(false);
  const [duzenlenen, setDuzenlenen] = useState<number | "yeni" | null>(null);
  const [form, setForm] = useState<Form>(BOS);
  const [hata, setHata] = useState<string | null>(null);
  const [bilgi, setBilgi] = useState<string | null>(null);

  const yenile = useCallback(() => {
    api.urunler(arsivGoster).then(setUrunler).catch(() => setHata("Ürünler alınamadı."));
  }, [arsivGoster]);

  useEffect(() => { if (oturum) yenile(); }, [oturum, yenile]);

  function duzenlemeyiAc(u: Urun) {
    setDuzenlenen(u.id);
    setForm({
      sku: u.sku,
      ad: u.ad,
      birimAgirlikG: u.birimAgirlikG?.toString() ?? "",
      guvenlikStogu: u.guvenlikStogu,
    });
  }

  async function kaydet() {
    setHata(null); setBilgi(null);

    const govde = {
      sku: form.sku.trim(),
      ad: form.ad.trim(),
      birimAgirlikG: form.birimAgirlikG ? Number(form.birimAgirlikG) : undefined,
      guvenlikStogu: form.guvenlikStogu,
    };

    try {
      if (duzenlenen === "yeni") {
        await api.urunEkle(govde);
        setBilgi(`${govde.ad} eklendi.`);
      } else if (typeof duzenlenen === "number") {
        await api.urunGuncelle(duzenlenen, govde);
        setBilgi(`${govde.ad} güncellendi. Yeni güvenlik stoğu sarı ışık eşiğini belirler.`);
      }
      setDuzenlenen(null);
      setForm(BOS);
      yenile();
    } catch (h) {
      setHata(h instanceof ApiHata ? h.message : "Kaydedilemedi.");
    }
  }

  async function arsivle(u: Urun) {
    setHata(null); setBilgi(null);
    try {
      await api.urunAktiflik(u.id, u.aktif === false);
      setBilgi(u.aktif === false ? `${u.ad} yeniden kataloğa alındı.` : `${u.ad} arşivlendi.`);
      yenile();
    } catch (h) {
      setHata(h instanceof ApiHata ? h.message : "İşlem başarısız.");
    }
  }

  if (!oturum) return null;

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <p className="etiket">Katalog</p>
          <h1 className="baslik mt-1">Ürünler</h1>
          <p className="mt-1 text-sm text-slate-400">
            <b>Güvenlik stoğu (b)</b> sarı ışık eşiğini belirler: stok <code>q + b</code> altına
            inince kutu sarı yanar ve tedarik talebi açılır.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          {yonetici && (
            <label className="flex items-center gap-2 text-sm text-slate-300">
              <input type="checkbox" checked={arsivGoster} data-test="arsiv-goster"
                onChange={(e) => setArsivGoster(e.target.checked)} />
              Arşivdekileri de göster
            </label>
          )}
          {yonetici && (
            <button className="dugme" onClick={() => { setDuzenlenen("yeni"); setForm(BOS); }}>
              + Yeni ürün
            </button>
          )}
        </div>
      </header>

      {hata && <p className="rounded-lg border border-durum-kirmizi/40 bg-durum-kirmizi/10 px-4 py-2 text-sm text-durum-kirmizi">{hata}</p>}
      {bilgi && <p className="rounded-lg border border-durum-yesil/40 bg-durum-yesil/10 px-4 py-2 text-sm text-durum-yesil">{bilgi}</p>}

      {duzenlenen !== null && yonetici && (
        <section className="kart space-y-4">
          <h2 className="etiket">{duzenlenen === "yeni" ? "Yeni ürün" : "Ürünü düzenle"}</h2>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <div>
              <label className="etiket">SKU</label>
              <input className="girdi mt-1" value={form.sku} disabled={duzenlenen !== "yeni"}
                onChange={(e) => setForm({ ...form, sku: e.target.value })} placeholder="KBL-3M" />
              {duzenlenen !== "yeni" && <p className="mt-1 text-[11px] text-slate-500">SKU değiştirilemez</p>}
            </div>
            <div>
              <label className="etiket">Ad</label>
              <input className="girdi mt-1" value={form.ad}
                onChange={(e) => setForm({ ...form, ad: e.target.value })} placeholder="Kablo 3 m" />
            </div>
            <div>
              <label className="etiket">Birim ağırlık (g)</label>
              <input className="girdi mt-1" type="number" step="0.1" min="0" value={form.birimAgirlikG}
                onChange={(e) => setForm({ ...form, birimAgirlikG: e.target.value })} placeholder="48.5" />
              <p className="mt-1 text-[11px] text-slate-500">ağırlık sensörüyle doğrulama için</p>
            </div>
            <div>
              <label className="etiket">Güvenlik stoğu (b)</label>
              <input className="girdi mt-1" type="number" min={0} value={form.guvenlikStogu}
                onChange={(e) => setForm({ ...form, guvenlikStogu: Math.max(0, Number(e.target.value)) })} />
            </div>
          </div>
          <div className="flex gap-2">
            <button className="dugme" disabled={!form.sku.trim() || !form.ad.trim()} onClick={kaydet}>Kaydet</button>
            <button className="dugme-ikincil" onClick={() => { setDuzenlenen(null); setForm(BOS); }}>Vazgeç</button>
          </div>
        </section>
      )}

      <section className="kart overflow-x-auto">
        <table className="tablo">
          <thead>
            <tr><th>SKU</th><th>Ürün</th><th>Birim ağırlık</th><th>Güvenlik stoğu (b)</th>
              <th>Toplam stok</th><th>Durum</th>{yonetici && <th></th>}</tr>
          </thead>
          <tbody>
            {urunler.map((u) => {
              const kritik = u.toplamStok < u.guvenlikStogu;
              return (
                <tr key={u.id} className={u.aktif === false ? "opacity-50" : ""}>
                  <td className="font-mono text-xs">{u.sku}</td>
                  <td className="space-x-2 font-semibold">
                    <span>{u.ad}</span>
                    {u.aktif === false && (
                      <span className="rozet bg-slate-600/30 text-slate-400">ARŞİVDE</span>
                    )}
                  </td>
                  <td>{u.birimAgirlikG != null ? `${u.birimAgirlikG} g` : "—"}</td>
                  <td>{u.guvenlikStogu}</td>
                  <td className="font-bold">{u.toplamStok}</td>
                  <td>
                    {u.toplamStok === 0
                      ? <span className="text-durum-kirmizi">✕ tükendi</span>
                      : kritik
                        ? <span className="text-durum-sari">⚠ güvenlik stoğunun altında</span>
                        : <span className="text-durum-yesil">✓ yeterli</span>}
                  </td>
                  {yonetici && (
                    <td className="space-x-1 whitespace-nowrap">
                      <button className="dugme-ikincil dugme-kucuk" onClick={() => duzenlemeyiAc(u)}>
                        Düzenle
                      </button>
                      <button
                        className={u.aktif === false ? "dugme dugme-kucuk" : "dugme-tehlike dugme-kucuk"}
                        title={u.aktif === false
                          ? "Ürünü yeniden kataloğa al"
                          : "Ürünü arşivle: yeni siparişlerde seçilemez, geçmişi korunur"}
                        onClick={() => arsivle(u)}>
                        {u.aktif === false ? "Geri al" : "Arşivle"}
                      </button>
                    </td>
                  )}
                </tr>
              );
            })}
            {urunler.length === 0 && <tr><td colSpan={yonetici ? 7 : 6} className="text-slate-500">Ürün yok.</td></tr>}
          </tbody>
        </table>
      </section>
    </div>
  );
}
