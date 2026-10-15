// Backend DTO'larıyla birebir eşleşen tipler (PickToLight.Application/Dtos).

export type LedRengi = "Yesil" | "Sari" | "Kirmizi";
export type SiparisDurumu =
  | "Bekliyor" | "Hazirlaniyor" | "Tamamlandi" | "KismiTamamlandi" | "Kilitli" | "Iptal";
export type KalemDurumu = "Bekliyor" | "IsikYandi" | "Alindi" | "Yetersiz" | "Iptal";
export type TeyitYontemi = "Buton" | "Ir" | "Agirlik" | "Manuel";
export type BaglantiDurumu = "Online" | "Offline";
export type AlarmTipi =
  | "StokAzaldi" | "StokYok" | "CihazOffline" | "TeyitZamanAsimi"
  | "BeklenmeyenTeyit" | "AgirlikUyusmazligi";
export type TedarikDurumu = "Acik" | "Verildi" | "Kapandi";
export type PersonelRolu = "Operator" | "Admin";

export interface Kutu {
  id: number;
  kod: string;
  urunId?: number;
  urunAdi?: string;
  cihazMac: string;
  mqttTopic: string;
  rafKonumu?: string;
  baglantiDurumu: BaglantiDurumu;
  sonGorulme?: string;
  adet: number;
  guvenlikStogu: number;
  anlikRenk?: LedRengi;
  isikYaniyor: boolean;
}

export interface Kalem {
  kalemId: number;
  urunId: number;
  urunAdi: string;
  sku: string;
  kutuId?: number;
  kutuKod?: string;
  rafKonumu?: string;
  miktar: number;
  kalanStok?: number;
  ledRengi?: LedRengi;
  durum: KalemDurumu;
  teyitYontemi?: TeyitYontemi;
  sira?: number;
  alinmaZamani?: string;
  uyari?: string;
  hata?: string;
}

export interface Alarm {
  id: number;
  tip: AlarmTipi;
  mesaj: string;
  kutuId?: number;
  kutuKod?: string;
  siparisId?: number;
  okundu: boolean;
  zaman: string;
}

export interface SiparisOzet {
  id: number;
  siparisNo: string;
  durum: SiparisDurumu;
  olusturma: string;
  tamamlanma?: string;
  kalemSayisi: number;
  alinanKalemSayisi: number;
}

export interface SiparisDetay {
  id: number;
  siparisNo: string;
  durum: SiparisDurumu;
  olusturma: string;
  tamamlanma?: string;
  kalemler: Kalem[];
  alarmlar: Alarm[];
}

export interface Urun {
  id: number;
  sku: string;
  ad: string;
  birimAgirlikG?: number;
  guvenlikStogu: number;
  toplamStok: number;
}

export interface StokHareket {
  id: number;
  kutuId: number;
  kutuKod: string;
  urunAdi?: string;
  tip: "Cikis" | "Giris" | "Sayim";
  miktar: number;
  oncekiAdet: number;
  sonrakiAdet: number;
  personelAdi?: string;
  aciklama?: string;
  zaman: string;
}

export interface TedarikTalebi {
  id: number;
  urunId: number;
  urunAdi: string;
  sku: string;
  talepMiktari: number;
  durum: TedarikDurumu;
  olusturma: string;
}

export interface Oturum {
  token: string;
  gecerlilikSonu: string;
  adSoyad: string;
  rol: PersonelRolu;
}

/** RFC 7807 Problem Details — backend'in hata sözleşmesi. */
export interface ApiHatasi {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  kod?: string;
  [ek: string]: unknown;
}
