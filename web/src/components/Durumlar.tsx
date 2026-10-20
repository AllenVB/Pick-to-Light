"use client";

/**
 * Yükleniyor ve boş durum bileşenleri.
 *
 * Veri gelene kadar boş tablo göstermek "kayıt yok" izlenimi verir; iskelet
 * satırlar beklemeyi görünür kılar. Boş durumda da ne yapılacağı yazılır.
 */

export function IskeletSatir({ sutun = 4 }: { sutun?: number }) {
  return (
    <tr>
      {Array.from({ length: sutun }).map((_, i) => (
        <td key={i} className="px-3 py-3">
          <div className="iskelet h-3" style={{ width: `${55 + ((i * 17) % 35)}%` }} />
        </td>
      ))}
    </tr>
  );
}

export function IskeletKart({ adet = 4 }: { adet?: number }) {
  return (
    <>
      {Array.from({ length: adet }).map((_, i) => (
        <div key={i} className="kart space-y-3">
          <div className="iskelet h-2.5 w-16" />
          <div className="iskelet h-7 w-24" />
          <div className="iskelet h-2.5 w-20" />
        </div>
      ))}
    </>
  );
}

export function BosDurum({
  baslik,
  aciklama,
  eylem,
  simge = "◍",
}: {
  baslik: string;
  aciklama?: string;
  eylem?: React.ReactNode;
  simge?: string;
}) {
  return (
    <div className="flex flex-col items-center justify-center gap-2 px-6 py-12 text-center"
      data-test="bos-durum">
      <span aria-hidden className="text-3xl text-slate-600">{simge}</span>
      <p className="text-sm font-semibold text-slate-300">{baslik}</p>
      {aciklama && <p className="max-w-md text-xs leading-relaxed text-slate-500">{aciklama}</p>}
      {eylem && <div className="mt-2">{eylem}</div>}
    </div>
  );
}
