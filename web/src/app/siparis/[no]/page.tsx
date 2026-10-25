"use client";

import { useCallback, useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { ApiHata, api } from "@/lib/api";
import { useCanliOlaylar } from "@/hooks/useCanliOlaylar";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import { useToast } from "@/components/Toast";
import { GeriSayim } from "@/components/GeriSayim";
import { KalemRozeti, OncelikRozeti, RenkRozeti, SiparisRozeti } from "@/components/Rozet";
import { IskeletKart } from "@/components/Durumlar";
import type { Kalem, SiparisDetay, TeyitYontemi } from "@/types";

/**
 * Sipariş detayı — vardiya sorumlusunun "bu sipariş ne durumda?" ekranı.
 * Kiosk diliyle aynı görsel kodu kullanır: her kalem bir kart, ışığı yanan kalem
 * öne çıkar. Tablo yerine kart seçildi çünkü bir kalemin anlaşılması için
 * birbirinden farklı yedi bilgi (kutu, raf, miktar, renk, süre, kim, durum)
 * gerekiyor ve bunlar satıra sığdırıldığında okunmuyordu.
 */

const TEYIT_METNI: Record<TeyitYontemi, string> = {
  Buton: "kutu butonu",
  Ir: "IR sensörü",
  Agirlik: "ağırlık sensörü",
  Manuel: "ekrandan",
};

/** Saniyeyi insan okunur hale getirir: 42 sn · 3 dk 05 sn */
function sureMetni(saniye: number) {
  if (saniye < 60) return `${saniye} sn`;
  const dk = Math.floor(saniye / 60);
  return `${dk} dk ${String(saniye % 60).padStart(2, "0")} sn`;
}

/**
 * Teyit izi: nasıl, kim, ne kadar sürede, saat kaçta.
 * Kutu butonundan gelen teyitte kimlik yoktur — uydurmak yerine açıkça yazılır,
 * çünkü denetimde "kim aldı" sorusuna yanlış isim vermek bilgisizlikten kötüdür.
 */
function teyitIzi(kalem: Kalem) {
  const parcalar = [
    kalem.teyitYontemi ? TEYIT_METNI[kalem.teyitYontemi] : null,
    kalem.teyitEden ?? "kimlik yok",
    kalem.toplamaSuresiSn != null ? sureMetni(kalem.toplamaSuresiSn) : null,
    kalem.alinmaZamani ? new Date(kalem.alinmaZamani).toLocaleTimeString("tr-TR") : null,
  ].filter(Boolean);

  return parcalar.join(" · ");
}

function KalemKarti({ kalem, sira, islem, onTeyit }: {
  kalem: Kalem;
  sira: number;
  islem: boolean;
  onTeyit: () => void;
}) {
  const yaniyor = kalem.durum === "IsikYandi";
  const alindi = kalem.durum === "Alindi";
  const sorunlu = kalem.durum === "Yetersiz";

  const cerceve = yaniyor
    ? "border-marka/60 bg-marka/5 shadow-[0_0_0_1px_rgba(13,148,136,0.25)]"
    : sorunlu
      ? "border-durum-kirmizi/40"
      : alindi
        ? "border-kenar/60 opacity-75"
        : "border-kenar";

  return (
    <article data-test="kalem-karti" data-durum={kalem.durum}
      className={`kart border transition-all ${cerceve}`}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex min-w-0 items-start gap-3">
          <span className={`mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-xs font-bold
            ${alindi ? "bg-durum-yesil/20 text-durum-yesil" : "bg-zemin-kart text-slate-400"}`}>
            {alindi ? "✓" : sira}
          </span>
          <div className="min-w-0">
            <h3 className="truncate text-base font-semibold text-slate-100">{kalem.urunAdi}</h3>
            <p className="font-mono text-[11px] text-slate-500">{kalem.sku}</p>
          </div>
        </div>

        <div className="flex flex-wrap items-center justify-end gap-2">
          {kalem.ledRengi && <RenkRozeti renk={kalem.ledRengi} />}
          <KalemRozeti durum={kalem.durum} />
          {yaniyor && (
            <button className="dugme px-3 py-1.5 text-xs" disabled={islem} onClick={onTeyit}>
              Manuel teyit
            </button>
          )}
        </div>
      </div>

      <dl className="mt-4 grid grid-cols-2 gap-x-4 gap-y-3 sm:grid-cols-4">
        <div>
          <dt className="etiket text-[10px]">Kutu</dt>
          <dd className="mt-0.5 font-mono text-sm text-slate-200">{kalem.kutuKod ?? "—"}</dd>
        </div>
        <div>
          <dt className="etiket text-[10px]">Raf</dt>
          <dd className="mt-0.5 text-sm text-slate-300">{kalem.rafKonumu ?? "—"}</dd>
        </div>
        <div>
          <dt className="etiket text-[10px]">Alınacak</dt>
          <dd className="mt-0.5 text-lg font-bold leading-none text-slate-100">{kalem.miktar}</dd>
        </div>
        <div>
          <dt className="etiket text-[10px]">Kutuda kalan</dt>
          <dd className="mt-0.5 text-sm text-slate-300">{kalem.kalanStok ?? "—"}</dd>
        </div>
      </dl>

      {/* Baskül ölçümü: gram düşüşü birim ağırlığa bölünerek gerçekten alınan adet bulunur. */}
      {kalem.olculenAdet != null && (
        <p className="mt-3 flex flex-wrap items-center gap-2 border-t border-kenar/60 pt-3 text-xs text-slate-400"
          data-test="olculen-adet">
          <span aria-hidden>⚖</span>
          <span>
            Baskül: <b className="text-slate-200">{kalem.olculenAdet} adet</b>
            {kalem.olculenAgirlikG != null && ` (${kalem.olculenAgirlikG.toLocaleString("tr-TR", { maximumFractionDigits: 1 })} g)`}
          </span>
          {kalem.olculenAdet === kalem.miktar ? (
            <span className="rozet bg-durum-yesil/15 text-durum-yesil">istenenle aynı</span>
          ) : (
            <span className="rozet bg-durum-sari/20 text-durum-sari">
              istenen {kalem.miktar} · stok {kalem.olculenAdet > kalem.miktar ? "ek düşüldü" : "iade edildi"}
            </span>
          )}
        </p>
      )}

      {yaniyor && (
        <p className="mt-3 flex flex-wrap items-center gap-2 border-t border-kenar/60 pt-3 text-xs text-slate-400">
          <span className="font-semibold text-marka-metin">Işık yanıyor</span>
          <span className="text-slate-600">·</span>
          <GeriSayim sonTarih={kalem.teyitSonTarihi} />
        </p>
      )}

      {kalem.durum === "Sirada" && (
        <p className="mt-3 border-t border-kenar/60 pt-3 text-xs text-durum-sari">
          Kutu başka bir sipariş için yanıyor; sırası gelince ışık kendiliğinden yanacak.
        </p>
      )}

      {alindi && (
        <p className="mt-3 border-t border-kenar/60 pt-3 text-xs text-slate-400" data-test="kalem-teyit-izi">
          <span className="font-semibold text-durum-yesil">Alındı</span>
          {" · "}
          {teyitIzi(kalem)}
        </p>
      )}

      {kalem.uyari && (
        <p className="mt-3 rounded-lg bg-durum-sari/10 px-3 py-2 text-xs text-durum-sari">⚠ {kalem.uyari}</p>
      )}
      {kalem.hata && (
        <p className="mt-3 rounded-lg bg-durum-kirmizi/10 px-3 py-2 text-xs text-durum-kirmizi">✕ {kalem.hata}</p>
      )}
    </article>
  );
}

export default function SiparisDetaySayfasi() {
  const oturum = useKorumaliSayfa();
  const { no } = useParams<{ no: string }>();
  const [detay, setDetay] = useState<SiparisDetay | null>(null);
  const [hata, setHata] = useState<string | null>(null);
  const toast = useToast();
  const [islem, setIslem] = useState(false);

  const yenile = useCallback(async () => {
    try {
      setDetay(await api.siparisDetay(decodeURIComponent(no)));
    } catch (h) {
      setHata(h instanceof ApiHata ? h.message : "Sipariş yüklenemedi.");
    }
  }, [no]);

  useEffect(() => { if (oturum) yenile(); }, [oturum, yenile]);

  useCanliOlaylar({
    KalemTeyitEdildi: yenile,
    SiparisDurumDegisti: yenile,
    StokGuncellendi: yenile,
    KutuIsikDegisti: yenile,
  }, [no]);

  async function calistir(f: () => Promise<unknown>) {
    setIslem(true);
    setHata(null);
    try { await f(); await yenile(); }
    catch (h) { toast(h instanceof ApiHata ? h.message : "İşlem başarısız.", "hata"); }
    finally { setIslem(false); }
  }

  if (!oturum) return null;

  if (!detay) {
    return hata
      ? <p className="kart border border-durum-kirmizi/40 text-sm text-durum-kirmizi">{hata}</p>
      : <div className="space-y-4"><IskeletKart adet={3} /></div>;
  }

  const alinan = detay.kalemler.filter((k) => k.durum === "Alindi").length;
  const toplam = detay.kalemler.length;
  const bitti = detay.durum === "Tamamlandi" || detay.durum === "Iptal";
  const yuzde = Math.round((alinan / Math.max(toplam, 1)) * 100);

  // Toplama hızı: yalnızca ölçülmüş kalemlerin ortalaması (ışığı hiç yanmayan sayılmaz).
  const olculenler = detay.kalemler
    .map((k) => k.toplamaSuresiSn)
    .filter((s): s is number => s != null);
  const ortalama = olculenler.length
    ? Math.round(olculenler.reduce((a, b) => a + b, 0) / olculenler.length)
    : null;

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <p className="etiket">Toplama ekranı</p>
          <h1 className="baslik mt-1 flex flex-wrap items-center gap-3" data-test="siparis-basligi">
            {detay.siparisNo}
            <SiparisRozeti durum={detay.durum} />
            <OncelikRozeti oncelik={detay.oncelik} />
          </h1>
          <p className="mt-1 text-sm text-slate-400">
            {new Date(detay.olusturma).toLocaleString("tr-TR")}
            {detay.tamamlanma && <> · {new Date(detay.tamamlanma).toLocaleTimeString("tr-TR")} tamamlandı</>}
            {ortalama != null && <> · kalem başına ortalama {sureMetni(ortalama)}</>}
          </p>
        </div>

        <div className="flex gap-2">
          {!bitti && (
            <button className="dugme" disabled={islem}
              onClick={() => calistir(() => api.siparisBaslat(detay.id))}>
              {detay.durum === "Bekliyor" ? "Başlat (ışıkları yak)" : "Yeniden dene"}
            </button>
          )}
          {!bitti && (
            <button className="dugme-ikincil" disabled={islem}
              onClick={() => calistir(() => api.siparisIptal(detay.id))}>
              İptal et (stoğu iade et)
            </button>
          )}
        </div>
      </header>

      <section className="kart" aria-label="Toplama ilerlemesi">
        <div className="flex items-end justify-between gap-3">
          <p className="text-sm text-slate-300">
            <b className="text-xl text-slate-100">{alinan}</b>
            <span className="text-slate-500"> / {toplam}</span> kalem alındı
          </p>
          <p className="text-sm font-semibold text-slate-400">%{yuzde}</p>
        </div>
        <div className="mt-2 h-2 w-full overflow-hidden rounded-full bg-zemin">
          <div className="h-full rounded-full bg-durum-yesil transition-all duration-500"
            style={{ width: `${yuzde}%` }} />
        </div>
      </section>

      {hata && (
        <p className="rounded-lg border border-durum-kirmizi/40 bg-durum-kirmizi/10 px-4 py-3 text-sm text-durum-kirmizi">
          {hata}
        </p>
      )}

      <section className="space-y-3" data-test="kalem-listesi">
        {detay.kalemler.map((k, i) => (
          <KalemKarti key={k.kalemId} kalem={k} sira={k.sira ?? i + 1} islem={islem}
            onTeyit={() => calistir(() => api.kalemTeyit(k.kalemId))} />
        ))}
      </section>

      {detay.alarmlar.length > 0 && (
        <section className="kart" data-test="siparis-alarmlari">
          <h2 className="etiket mb-3">Bu siparişin alarmları</h2>
          <ul className="space-y-2">
            {detay.alarmlar.map((a) => (
              <li key={a.id} className="rounded-lg border border-durum-kirmizi/30 bg-durum-kirmizi/10 px-3 py-2 text-sm">
                <b className="text-durum-kirmizi">{a.tip}</b> · {a.mesaj}
                <span className="ml-2 text-[11px] text-slate-500">
                  {new Date(a.zaman).toLocaleTimeString("tr-TR")}
                </span>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}
