import type { Kutu } from "@/types";

const LAMBA: Record<string, string> = {
  Yesil: "bg-durum-yesil",
  Sari: "bg-durum-sari nabiz-sari",
  Kirmizi: "bg-durum-kirmizi nabiz-kirmizi",
};

const CERCEVE: Record<string, string> = {
  Yesil: "border-durum-yesil/50",
  Sari: "border-durum-sari/60",
  Kirmizi: "border-durum-kirmizi/60",
};

const METIN: Record<string, string> = {
  Yesil: "Yeterli",
  Sari: "Azaldı",
  Kirmizi: "Yetersiz",
};

export function KutuKarti({ kutu }: { kutu: Kutu }) {
  const cevrimdisi = kutu.baglantiDurumu === "Offline";
  const renk = kutu.anlikRenk ?? "Yesil";

  return (
    <div
      className={`kart relative transition ${cevrimdisi ? "opacity-50" : CERCEVE[renk]} ${
        kutu.isikYaniyor ? "ring-2 ring-marka-acik" : ""
      }`}
    >
      <div className="flex items-start justify-between">
        <div>
          <p className="etiket">{kutu.kod}</p>
          <p className="mt-0.5 text-sm font-semibold">{kutu.urunAdi ?? "— ürün atanmamış —"}</p>
          <p className="text-xs text-slate-400">{kutu.rafKonumu ?? "konum yok"}</p>
        </div>
        <span
          className={`h-6 w-6 shrink-0 rounded-full ${cevrimdisi ? "bg-durum-gri" : LAMBA[renk]}`}
          title={cevrimdisi ? "Bağlantı yok" : METIN[renk]}
        />
      </div>

      <div className="mt-4 flex items-end justify-between">
        <div>
          <p className="text-3xl font-extrabold leading-none">{kutu.adet}</p>
          <p className="text-[11px] text-slate-400">adet · güvenlik {kutu.guvenlikStogu}</p>
        </div>
        <div className="text-right text-[11px]">
          {cevrimdisi ? (
            <span className="font-bold text-durum-gri">⚡ BAĞLANTI YOK</span>
          ) : (
            <span className="font-bold text-slate-300">
              {kutu.isikYaniyor ? "🔆 IŞIK YANIYOR" : METIN[renk]}
            </span>
          )}
        </div>
      </div>
    </div>
  );
}
