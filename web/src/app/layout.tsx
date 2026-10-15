import type { Metadata } from "next";
import "./globals.css";
import { Kenar } from "@/components/Kenar";

export const metadata: Metadata = {
  title: "Pick-to-Light · Akıllı Depo",
  description: "Işıkla toplama ve anlık stok yönetim sistemi",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="tr">
      <body>
        <div className="flex min-h-screen">
          <Kenar />
          <main className="flex-1 overflow-x-hidden p-6">{children}</main>
        </div>
      </body>
    </html>
  );
}
