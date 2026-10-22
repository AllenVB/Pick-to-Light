"use client";

import { useEffect, useState } from "react";

export type Tema = "koyu" | "acik";

const ANAHTAR = "ptl-tema";

/**
 * Tema anahtarı. Varsayılan koyudur (depo ekranları çoğunlukla loş ışıkta ve
 * uzaktan okunur); açık tema parlak aydınlatma ve yazıcı çıktısı içindir.
 *
 * Seçim localStorage'da tutulur: aynı kiosk ekranı her açılışta aynı temayla
 * gelsin. Gizli sekmede veya depolama kapalıysa okuma hata fırlatabilir,
 * bu yüzden her erişim korumalıdır.
 */
export function temaOku(): Tema {
  try {
    return localStorage.getItem(ANAHTAR) === "acik" ? "acik" : "koyu";
  } catch {
    return "koyu";
  }
}

function temaUygula(tema: Tema) {
  document.documentElement.dataset.tema = tema;
  try {
    localStorage.setItem(ANAHTAR, tema);
  } catch {
    // Depolama yoksa tema yine de uygulanır, sadece hatırlanmaz.
  }
}

export function TemaDugmesi() {
  // Sunucuda localStorage yoktur; ilk çizim koyu varsayar, etkiden sonra düzeltilir.
  const [tema, setTema] = useState<Tema>("koyu");
  const [hazir, setHazir] = useState(false);

  useEffect(() => {
    setTema(temaOku());
    setHazir(true);
  }, []);

  function degistir() {
    const yeni: Tema = tema === "koyu" ? "acik" : "koyu";
    setTema(yeni);
    temaUygula(yeni);
  }

  // Hazır olmadan etiket basılırsa sunucu/istemci çizimi uyuşmaz ve yanıp söner.
  if (!hazir) return <span className="block h-9" aria-hidden />;

  const acik = tema === "acik";

  return (
    <button type="button" onClick={degistir} data-test="tema-dugmesi" data-tema={tema}
      aria-label={acik ? "Koyu temaya geç" : "Açık temaya geç"}
      title={acik ? "Koyu temaya geç" : "Açık temaya geç"}
      className="inline-flex h-9 w-full items-center justify-center gap-2 rounded-lg border border-zemin-cizgi
                 bg-zemin-kart/60 px-3 text-xs font-semibold text-slate-300
                 hover:border-marka-acik hover:text-marka-metin">
      <span aria-hidden className="text-sm leading-none">{acik ? "☾" : "☀"}</span>
      {acik ? "Koyu tema" : "Açık tema"}
    </button>
  );
}

/**
 * Sayfa çizilmeden önce temayı uygular. Bu olmadan açık tema seçili kullanıcı
 * her yenilemede önce koyu ekranı görür (flash of wrong theme).
 */
export function TemaBaslatici() {
  const betik = `(function(){try{var t=localStorage.getItem("${ANAHTAR}");if(t==="acik")document.documentElement.dataset.tema="acik";}catch(e){}})();`;
  return <script dangerouslySetInnerHTML={{ __html: betik }} />;
}
