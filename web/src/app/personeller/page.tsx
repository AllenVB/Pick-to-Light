"use client";

import { useCallback, useEffect, useState } from "react";
import { ApiHata, api } from "@/lib/api";
import { useKorumaliSayfa } from "@/hooks/useKorumaliSayfa";
import { useToast } from "@/components/Toast";
import { BosDurum } from "@/components/Durumlar";
import type { Personel } from "@/types";

export default function PersonellerSayfasi() {
  const oturum = useKorumaliSayfa("Admin");
  const yonetici = oturum?.rol === "Admin";

  const [personeller, setPersoneller] = useState<Personel[]>([]);
  const [formAcik, setFormAcik] = useState(false);
  const [adSoyad, setAdSoyad] = useState("");
  const [kullaniciAdi, setKullaniciAdi] = useState("");
  const [parola, setParola] = useState("");
  const [rol, setRol] = useState("Operator");
  const [hata, setHata] = useState<string | null>(null);
  const toast = useToast();

  const yenile = useCallback(() => {
    api.personeller().then(setPersoneller)
      .catch((h) => setHata(h instanceof ApiHata ? h.message : "Personel listesi alınamadı."));
  }, []);

  useEffect(() => { if (oturum && yonetici) yenile(); }, [oturum, yonetici, yenile]);

  async function calistir(f: () => Promise<unknown>, basarim: string) {
    setHata(null);
    try {
      await f();
      toast(basarim, "basari");
      yenile();
    } catch (h) {
      toast(h instanceof ApiHata ? h.message : "İşlem başarısız.", "hata");
    }
  }

  if (!oturum) return null;

  if (!yonetici) {
    return (
      <div className="kart">
        <p className="text-sm text-slate-300">Personel yönetimi yalnızca yönetici rolüyle görüntülenir.</p>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <p className="etiket">Yönetim</p>
          <h1 className="baslik mt-1">Personel</h1>
          <p className="mt-1 text-sm text-slate-400">
            Her stok hareketi bir personele bağlanır; bu yüzden kayıtlar silinmez,
            ayrılan personel <b>pasifleştirilir</b> (geçmiş korunur).
          </p>
        </div>
        <button className="dugme" onClick={() => setFormAcik((a) => !a)}>
          {formAcik ? "Formu kapat" : "+ Yeni personel"}
        </button>
      </header>

      {hata && <p className="rounded-lg border border-durum-kirmizi/40 bg-durum-kirmizi/10 px-4 py-2 text-sm text-durum-kirmizi">{hata}</p>}

      {formAcik && (
        <section className="kart space-y-4">
          <h2 className="etiket">Yeni personel</h2>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <div>
              <label className="etiket">Ad soyad</label>
              <input className="girdi mt-1" value={adSoyad} onChange={(e) => setAdSoyad(e.target.value)} />
            </div>
            <div>
              <label className="etiket">Kullanıcı adı</label>
              <input className="girdi mt-1" value={kullaniciAdi}
                onChange={(e) => setKullaniciAdi(e.target.value)} placeholder="operator2" />
            </div>
            <div>
              <label className="etiket">Parola</label>
              <input className="girdi mt-1" type="password" value={parola}
                onChange={(e) => setParola(e.target.value)} placeholder="en az 6 karakter" />
            </div>
            <div>
              <label className="etiket">Rol</label>
              <select className="girdi mt-1" value={rol} onChange={(e) => setRol(e.target.value)}>
                <option value="Operator">Operatör — toplama, teyit, alarm</option>
                <option value="Admin">Yönetici — tüm işlemler</option>
              </select>
            </div>
          </div>
          <button className="dugme"
            disabled={!adSoyad.trim() || kullaniciAdi.trim().length < 3 || parola.length < 6}
            onClick={() => calistir(
              () => api.personelEkle({ adSoyad: adSoyad.trim(), kullaniciAdi: kullaniciAdi.trim(), parola, rol }),
              `${adSoyad} eklendi.`
            ).then(() => { setFormAcik(false); setAdSoyad(""); setKullaniciAdi(""); setParola(""); })}>
            Personeli ekle
          </button>
        </section>
      )}

      <section className="kart overflow-x-auto">
        <table className="tablo">
          <thead><tr><th>Ad soyad</th><th>Kullanıcı adı</th><th>Rol</th><th>Durum</th><th>İşlem</th></tr></thead>
          <tbody>
            {personeller.map((p) => (
              // Pasif personel sönük durmalı ama adı okunmalı: satırın tamamını
              // soluklaştırmak yerine metin tonu kısılır (opacity-50 ile ad 2:1'e düşüyordu).
              <tr key={p.id} className={p.aktif ? "" : "bg-zemin/40 text-slate-400"}>
                <td className="font-semibold">{p.adSoyad}</td>
                <td className="font-mono text-xs">{p.kullaniciAdi}</td>
                <td>
                  <select className="girdi w-32 py-1 text-xs" value={p.rol}
                    onChange={(e) => calistir(
                      () => api.personelGuncelle(p.id, { adSoyad: p.adSoyad, rol: e.target.value, aktif: p.aktif }),
                      `${p.adSoyad}: rol güncellendi.`)}>
                    <option value="Operator">Operatör</option>
                    <option value="Admin">Yönetici</option>
                  </select>
                </td>
                <td>
                  {p.aktif
                    ? <span className="text-durum-yesil">● aktif</span>
                    : <span className="text-slate-500">● pasif</span>}
                </td>
                <td className="space-x-1 whitespace-nowrap">
                  <button className="dugme-ikincil px-2 py-1 text-xs"
                    onClick={() => calistir(
                      () => api.personelGuncelle(p.id, { adSoyad: p.adSoyad, rol: p.rol, aktif: !p.aktif }),
                      `${p.adSoyad}: ${p.aktif ? "pasifleştirildi" : "aktifleştirildi"}.`)}>
                    {p.aktif ? "Pasifleştir" : "Aktifleştir"}
                  </button>
                  <button className="dugme-ikincil px-2 py-1 text-xs"
                    onClick={() => {
                      const yeni = prompt(`${p.adSoyad} için yeni parola (en az 6 karakter):`);
                      if (yeni) calistir(() => api.personelParola(p.id, yeni), `${p.adSoyad}: parola sıfırlandı.`);
                    }}>
                    Parola sıfırla
                  </button>
                </td>
              </tr>
            ))}
            {personeller.length === 0 && (
              <tr><td colSpan={5}>
                <BosDurum simge="☺" baslik="Kayıtlı personel yok"
                  aciklama="Her stok hareketi bir personele bağlanır; operatörleri buradan tanımlayın." />
              </td></tr>
            )}
          </tbody>
        </table>
      </section>
    </div>
  );
}
