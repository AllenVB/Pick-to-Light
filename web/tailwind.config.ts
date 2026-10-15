import type { Config } from "tailwindcss";

const config: Config = {
  content: ["./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        // Sunum ve plan dokümanıyla aynı renk dili
        marka: { DEFAULT: "#0f766e", acik: "#14b8a6", koyu: "#0b3c47" },
        zemin: { DEFAULT: "#0d1520", kart: "#16212e", cizgi: "#27384a" },
        durum: { yesil: "#22c55e", sari: "#f59e0b", kirmizi: "#ef4444", gri: "#64748b" },
      },
    },
  },
  plugins: [],
};

export default config;
