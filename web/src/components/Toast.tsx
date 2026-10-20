"use client";

import { createContext, useCallback, useContext, useEffect, useState } from "react";

type ToastTuru = "basari" | "hata" | "bilgi";

interface Toast {
  id: number;
  tur: ToastTuru;
  metin: string;
}

const ToastBaglami = createContext<(metin: string, tur?: ToastTuru) => void>(() => {});

/** Kısa işlem geri bildirimleri için: "stok güncellendi", "komut gönderildi" gibi. */
export function useToast() {
  return useContext(ToastBaglami);
}

const SINIF: Record<ToastTuru, string> = {
  basari: "border-durum-yesil/40 bg-durum-yesil/10 text-durum-yesil",
  hata: "border-durum-kirmizi/40 bg-durum-kirmizi/10 text-durum-kirmizi",
  bilgi: "border-marka-acik/40 bg-marka/10 text-marka-acik",
};

const SIMGE: Record<ToastTuru, string> = { basari: "✓", hata: "✕", bilgi: "ℹ" };

export function ToastSaglayici({ children }: { children: React.ReactNode }) {
  const [toastlar, setToastlar] = useState<Toast[]>([]);

  const goster = useCallback((metin: string, tur: ToastTuru = "bilgi") => {
    const id = Date.now() + Math.random();
    setToastlar((o) => [...o, { id, tur, metin }]);
  }, []);

  return (
    <ToastBaglami.Provider value={goster}>
      {children}

      {/* Ekranın altına sabitlenir: operatör elini klavyeden kaldırmadan görür. */}
      <div className="pointer-events-none fixed inset-x-0 bottom-4 z-50 flex flex-col items-center gap-2 px-4"
        role="status" aria-live="polite">
        {toastlar.map((t) => (
          <ToastKutusu key={t.id} toast={t}
            kapat={() => setToastlar((o) => o.filter((x) => x.id !== t.id))} />
        ))}
      </div>
    </ToastBaglami.Provider>
  );
}

function ToastKutusu({ toast, kapat }: { toast: Toast; kapat: () => void }) {
  useEffect(() => {
    const zaman = setTimeout(kapat, toast.tur === "hata" ? 6000 : 3500);
    return () => clearTimeout(zaman);
  }, [toast.tur, kapat]);

  return (
    <div className={`toast-gir pointer-events-auto flex max-w-lg items-start gap-3 rounded-xl border
                     px-4 py-3 text-sm font-medium shadow-lg shadow-black/40 backdrop-blur
                     ${SINIF[toast.tur]}`}
      data-test="toast" data-tur={toast.tur}>
      <span aria-hidden className="text-base leading-none">{SIMGE[toast.tur]}</span>
      <span className="flex-1">{toast.metin}</span>
      <button onClick={kapat} aria-label="Kapat" className="opacity-60 hover:opacity-100">✕</button>
    </div>
  );
}
