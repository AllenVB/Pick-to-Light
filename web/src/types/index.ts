// Backend DTO'larıyla birebir eşleşen tipler (PickToLight.Application/Dtos).

export type LedRengi = "Yesil" | "Sari" | "Kirmizi";
export type SiparisDurumu =
  | "Bekliyor" | "Hazirlaniyor" | "Tamamlandi" | "KismiTamamlandi" | "Kilitli" | "Iptal";
export type SiparisOnceligi = "Normal" | "Acil";

export type KalemDurumu = "Bekliyor" | "IsikYandi" | "Alindi" | "Yetersiz" | "Iptal" | "Sirada";
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
  alinacakMiktar?: number;
  siparisNo?: string;
  aktifKalemId?: number;
  aktif?: boolean;
  siradakiSayisi?: number;
  teyitSonTarihi?: string | null;
  firmwareSurumu?: string | null;
  rssi?: number | null;
  ipAdresi?: string | null;
  /** Atanan ürünün birim ağırlığı (g). */
  birimAgirlikG?: number | null;
  /** Baskülün son ölçtüğü içerik ağırlığı (g); null ise kutuda baskül yok / ölçüm gelmedi. */
  agirlikG?: number | null;
  agirlikZamani?: string | null;
  /** Ağırlıktan hesaplanan adet (ağırlık ÷ birim ağırlık). */
  tahminiAdet?: number | null;
  /** Rafta olması gereken adet: stok + rezerve edilip henüz alınmamış adet. */
  beklenenFizikselAdet?: number | null;
  /** Işığı yanan kalemin başladığı andaki kutu ağırlığı (g). */
  baslangicAgirligiG?: number | null;
  /** Son ölçüm oturdu mu (yalnızca canlı SignalR güncellemesiyle dolar). */
  agirlikKararli?: boolean;
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
  isikYanmaZamani?: string;
  teyitSonTarihi?: string;
  /** Kalemi teyit eden kişi; kutu butonundan gelen teyitlerde boş (kimlik yok). */
  teyitEden?: string;
  /** Işığın yanmasından teyide kadar geçen süre (sn). */
  toplamaSuresiSn?: number;
  /** Teyit anında baskülün ölçtüğü ağırlık düşüşü (g). */
  olculenAgirlikG?: number | null;
  /** Baskülden hesaplanan gerçek adet; istenenden farklıysa stok bu adede göre düzeltilmiştir. */
  olculenAdet?: number | null;
  baslangicAgirligiG?: number | null;
  birimAgirlikG?: number | null;
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
  oncelik: SiparisOnceligi;
}

export interface SiparisDetay {
  id: number;
  siparisNo: string;
  durum: SiparisDurumu;
  olusturma: string;
  tamamlanma?: string;
  kalemler: Kalem[];
  alarmlar: Alarm[];
  oncelik: SiparisOnceligi;
}

export interface Urun {
  id: number;
  sku: string;
  ad: string;
  birimAgirlikG?: number;
  guvenlikStogu: number;
  toplamStok: number;
  aktif: boolean;
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

// ───────────────────────── Raporlar ─────────────────────────

export interface RaporOzet {
  gunSayisi: number;
  toplamSiparis: number;
  tamamlananSiparis: number;
  kilitliSiparis: number;
  toplananKalem: number;
  yetersizKalem: number;
  toplananAdet: number;
  ortalamaToplamaSaniye?: number;
  kirmiziOrani: number;
  acikTedarikTalebi: number;
  okunmamisAlarm: number;
}

export interface UrunHareket {
  urunId: number;
  sku: string;
  urunAdi: string;
  toplamCikis: number;
  toplamaSayisi: number;
}

export interface KutuPerformans {
  kutuId: number;
  kutuKod: string;
  urunAdi?: string;
  toplamaSayisi: number;
  toplamAdet: number;
  ortalamaToplamaSaniye?: number;
  mevcutStok: number;
}

export interface GunlukHareket {
  gun: string;
  cikisAdedi: number;
  girisAdedi: number;
}

export interface CihazOlay {
  id: number;
  kutuId?: number;
  kutuKod?: string;
  topic: string;
  payload: string;
  zaman: string;
}

export interface Personel {
  id: number;
  adSoyad: string;
  kullaniciAdi: string;
  rol: PersonelRolu;
  aktif: boolean;
}

export interface IceAktarSatirSonucu {
  siparisNo: string;
  basarili: boolean;
  kalemSayisi: number;
  hata: string | null;
  satirNumaralari: number[];
}

export interface IceAktarSonucu {
  toplamSiparis: number;
  olusturulan: number;
  basarisiz: number;
  sonuclar: IceAktarSatirSonucu[];
}

export interface IsikTestiSonucu {
  topic: string;
  mesajId: string;
  renk: LedRengi;
  saniye: number;
  cihazCevrimici: boolean;
}
