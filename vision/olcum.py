"""
Tanıma sağlamlığı ölçümü: ışık ve gürültü bozulmaları altında doğruluk ve güven.

    .venv\\Scripts\\python olcum.py

Gerçek kamerada sonuç farklı olur; bu betik sanal sahnede algoritmanın payını gösterir.
"""
import numpy as np

import kaynak
import tanima


def egit():
    depo = tanima.ProfilDeposu(None)
    for urun_id, tip in ((1, "civata"), (2, "somun"), (3, "rondela")):
        for tohum in range(4):
            kare = kaynak.sahne_ciz([{"tip": tip, "adet": 2}], tohum=100 + tohum)
            depo.ekle(urun_id, tip, [n.ozellik for n in tanima.nesneleri_bul(kare)])
    return depo


def parlaklik(kare, katsayi):
    return np.clip(kare.astype(np.float32) * katsayi, 0, 255).astype(np.uint8)


def calistir():
    depo = egit()
    bozulmalar = {
        "olduğu gibi": lambda k: k,
        "%15 daha karanlık": lambda k: parlaklik(k, 0.85),
        "%15 daha aydınlık": lambda k: parlaklik(k, 1.15),
        "gürültü σ=8": lambda k: np.clip(k.astype(np.float32) + np.random.default_rng(1).normal(0, 8, k.shape), 0, 255).astype(np.uint8),
    }
    toplam = dogru = 0
    print(f"{'bozulma':22} {'parça':8} {'doğru/toplam':14} ortalama güven")
    for ad, bozucu in bozulmalar.items():
        for urun_id, tip in ((1, "civata"), (2, "somun"), (3, "rondela")):
            d = g = 0
            guvenler = []
            for tohum in range(20):
                kare = bozucu(kaynak.sahne_ciz([{"tip": tip, "adet": 3}], tohum=500 + tohum))
                for t in tanima.tani(kare, depo, 0.0):
                    d += 1
                    if t.urun_id == urun_id and t.guven >= 0.5:
                        g += 1
                        guvenler.append(t.guven)
            toplam += d
            dogru += g
            print(f"{ad:22} {tip:8} {g:4}/{d:<9} {np.mean(guvenler) if guvenler else 0:.2f}")
    print(f"\nGenel doğruluk: {dogru}/{toplam} = %{100 * dogru / toplam:.1f}")


if __name__ == "__main__":
    calistir()
