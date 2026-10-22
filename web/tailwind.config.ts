import type { Config } from "tailwindcss";

/**
 * Renkler doğrudan değil, CSS değişkenleri üzerinden tanımlanır: tema
 * (koyu/açık) globals.css'te değişkenleri değiştirir, 400'den fazla çağrı
 * yerine dokunmak gerekmez.
 *
 * Değişkenler kanal üçlüsü ("13 21 32") olarak tutulur; Tailwind'in opaklık
 * ekleri (bg-zemin-kart/80) ancak bu biçimde çalışır.
 */
const degisken = (ad: string) => `rgb(var(--${ad}) / <alpha-value>)`;

const config: Config = {
  content: ["./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        // Sunum ve plan dokümanıyla aynı renk dili
        marka: {
          DEFAULT: degisken("marka"),
          acik: degisken("marka-acik"),
          metin: degisken("marka-metin"),
          koyu: degisken("marka-koyu"),
        },
        zemin: {
          DEFAULT: degisken("zemin"),
          kart: degisken("zemin-kart"),
          cizgi: degisken("zemin-cizgi"),
        },
        /** Kutudaki fiziksel ışığın rengi — iki temada da aynı kalır. */
        led: {
          yesil: degisken("led-yesil"),
          sari: degisken("led-sari"),
          kirmizi: degisken("led-kirmizi"),
        },
        /** Beyaz yazı taşıyan dolgular için koyu tonlar. */
        dolgu: {
          kirmizi: degisken("dolgu-kirmizi"),
        },
        durum: {
          yesil: degisken("durum-yesil"),
          sari: degisken("durum-sari"),
          kirmizi: degisken("durum-kirmizi"),
          gri: degisken("durum-gri"),
        },
        /**
         * Metin ölçeği. Koyu temada 50 en parlak, 600 en soluktur; açık temada
         * ölçek ters çevrilir (50 en koyu). Böylece "text-slate-400 = soluk
         * metin" anlamı her iki temada da korunur.
         */
        slate: {
          50: degisken("s-50"),
          100: degisken("s-100"),
          200: degisken("s-200"),
          300: degisken("s-300"),
          400: degisken("s-400"),
          500: degisken("s-500"),
          600: degisken("s-600"),
          700: degisken("s-700"),
          800: degisken("s-800"),
          900: degisken("s-900"),
        },
      },
    },
  },
  plugins: [],
};

export default config;
