import type { Metadata } from "next";
import "./globals.css";
import { Kenar } from "@/components/Kenar";
import { ToastSaglayici } from "@/components/Toast";
import { TemaBaslatici } from "@/components/Tema";

export const metadata: Metadata = {
  title: "Pick-to-Light · Akıllı Depo",
  description: "Işıkla toplama ve anlık stok yönetim sistemi",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="tr" suppressHydrationWarning>
      <head>
        {/* Tema, gövde çizilmeden uygulanmalı: yoksa açık temada koyu ekran yanıp söner. */}
        <TemaBaslatici />
      </head>
      <body>
        {/* Küçük ekranda gezinme üstte yatay şerit, büyük ekranda sol kenar çubuğu olur. */}
        <ToastSaglayici>
          <div className="flex min-h-screen flex-col lg:flex-row">
            <Kenar />
            <main className="min-w-0 flex-1 overflow-x-hidden p-4 lg:p-6">{children}</main>
          </div>
        </ToastSaglayici>
      </body>
    </html>
  );
}
