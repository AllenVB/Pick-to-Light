import type { KalemDurumu, LedRengi, SiparisDurumu } from "@/types";

const RENK_SINIF: Record<LedRengi, string> = {
  Yesil: "bg-durum-yesil/15 text-durum-yesil border-durum-yesil/40",
  Sari: "bg-durum-sari/15 text-durum-sari border-durum-sari/40",
  Kirmizi: "bg-durum-kirmizi/15 text-durum-kirmizi border-durum-kirmizi/40",
};

const RENK_METIN: Record<LedRengi, string> = {
  Yesil: "✓ YEŞİL · yeterli",
  Sari: "⚠ SARI · azaldı",
  Kirmizi: "✕ KIRMIZI · yetersiz",
};

/** Renk körlüğüne karşı renk + ikon + metin birlikte gösterilir (NFR-08). */
export function RenkRozeti({ renk }: { renk?: LedRengi }) {
  if (!renk) return <span className="text-xs text-slate-500">—</span>;
  return (
    <span data-test="led-rengi" data-renk={renk}
      className={`inline-block rounded-full border px-2 py-0.5 text-[11px] font-bold ${RENK_SINIF[renk]}`}>
      {RENK_METIN[renk]}
    </span>
  );
}

const SIPARIS_SINIF: Record<SiparisDurumu, string> = {
  Bekliyor: "bg-slate-500/20 text-slate-300",
  Hazirlaniyor: "bg-marka/25 text-marka-acik",
  Tamamlandi: "bg-durum-yesil/20 text-durum-yesil",
  KismiTamamlandi: "bg-durum-sari/20 text-durum-sari",
  Kilitli: "bg-durum-kirmizi/20 text-durum-kirmizi",
  Iptal: "bg-slate-700/40 text-slate-400",
};

const SIPARIS_METIN: Record<SiparisDurumu, string> = {
  Bekliyor: "BEKLİYOR",
  Hazirlaniyor: "HAZIRLANIYOR",
  Tamamlandi: "TAMAMLANDI",
  KismiTamamlandi: "KISMİ TAMAMLANDI",
  Kilitli: "KİLİTLİ",
  Iptal: "İPTAL",
};

export function SiparisRozeti({ durum }: { durum: SiparisDurumu }) {
  return (
    <span data-test="siparis-durumu" data-durum={durum}
      className={`rounded-full px-2.5 py-0.5 text-[11px] font-bold ${SIPARIS_SINIF[durum]}`}>
      {SIPARIS_METIN[durum]}
    </span>
  );
}

const KALEM_SINIF: Record<KalemDurumu, string> = {
  Bekliyor: "bg-slate-500/20 text-slate-300",
  IsikYandi: "bg-marka/25 text-marka-acik",
  Alindi: "bg-durum-yesil/20 text-durum-yesil",
  Yetersiz: "bg-durum-kirmizi/20 text-durum-kirmizi",
  Iptal: "bg-slate-700/40 text-slate-400",
  Sirada: "bg-durum-sari/20 text-durum-sari",
};

const KALEM_METIN: Record<KalemDurumu, string> = {
  Bekliyor: "BEKLİYOR",
  IsikYandi: "IŞIK YANDI",
  Alindi: "ALINDI",
  Yetersiz: "YETERSİZ",
  Iptal: "İPTAL",
  Sirada: "SIRADA",
};

export function KalemRozeti({ durum }: { durum: KalemDurumu }) {
  return (
    <span data-test="kalem-durumu" data-durum={durum}
      className={`rounded-full px-2.5 py-0.5 text-[11px] font-bold ${KALEM_SINIF[durum]}`}>
      {KALEM_METIN[durum]}
    </span>
  );
}
