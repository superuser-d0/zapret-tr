# -*- coding: utf-8 -*-
"""
Seed ISP profillerini uretir.

Neden script? Superonline disindaki profillerin buyuk kismi ayni tabani paylasiyor
(HTTP / QUIC / Discord-ses bolumleri her ISP'de ayni upstream kuralindan basliyor).
Bunlari elle kopyalamak, birinde duzeltme yapip digerlerini unutmaya davetiye.
Tablo burada tek yerde duruyor, ciktilar profiles/isp/ altina yaziliyor ve commit ediliyor.

Superonline elle yazildi ve bu script ONA DOKUNMAZ -- oncelikli profil oldugu icin
derinlestirilmis, elle bakim gorecek bir dosya.

Kullanim (depo kokunden):  python tools/gen-seed-profiles.py
"""
import json
import pathlib

OUT = pathlib.Path("profiles/isp")

# Upstream'in kendi preset1_example.cmd dosyasindaki genel 443 kurali.
# ISP'ye ozel veri olmayan her profilde baslangic noktasi olarak kullaniliyor.
UPSTREAM_443 = ("--dpi-desync=fake,multidisorder --dpi-desync-split-pos=midsld "
                "--dpi-desync-repeats=6 --dpi-desync-fooling=badseq,md5sig")


def quic_base(prefix):
    return [
        dict(id=prefix + "-quic-fake-google", section="quic",
             args="--dpi-desync=fake --dpi-desync-repeats=11 --dpi-desync-fake-quic={FAKE_QUIC_GOOGLE}",
             protocols=["quic"], weight=100, source="upstream-preset",
             note="UDP parcalanamaz; QUIC'te tek yol sahte Initial paketi."),
        dict(id=prefix + "-quic-fake-plain", section="quic",
             args="--dpi-desync=fake --dpi-desync-repeats=11",
             protocols=["quic"], weight=90, source="upstream-preset",
             note="Hazir yuk olmadan sahte QUIC."),
    ]


def voice_base(prefix):
    return [
        dict(id=prefix + "-voice-fake", section="discord-voice",
             args="--dpi-desync=fake", protocols=["udp"], weight=100,
             source="upstream-preset",
             note="Discord ses/STUN bolumu. TCP stratejisinden bagimsiz test edilir."),
    ]


def http_base(prefix):
    return [
        dict(id=prefix + "-80-fake-fakedsplit", section="tcp80",
             args="--dpi-desync=fake,fakedsplit --dpi-desync-autottl=2 --dpi-desync-fooling=md5sig",
             protocols=["http"], weight=100, source="upstream-preset",
             note="Upstream'in duz HTTP kurali."),
        dict(id=prefix + "-80-multisplit-method", section="tcp80",
             args="--dpi-desync=multisplit --dpi-desync-split-pos=method+2",
             protocols=["http"], weight=80, source="hypothesis",
             note="Host basligini ayri segmente atar."),
    ]


# (id, gorunen ad, asns, orgKeywords, priority, notlar, 443 adaylari)
#
# asns bos olan ISP'lerde ASN sorgusu sonuc vermedi. Numara uydurmak yerine
# orgKeywords eslesmesine birakildi: "turknet" gibi adlar org kaydinda benzersiz.
ISPS = [
    ("turk-telekom", "Turk Telekom / TTNET", [9121, 47331],
     ["turk telekom", "ttnet", "turk telekomunikasyon"], 2,
     "Gelistirme makinesinin baglantisi bu ISP uzerinde, dolayisiyla yerelde uctan uca "
     "dogrulanabilen tek profil. Toplulukta bildirilen cekirdek yontem sabit dusuk TTL ile sahte paket.",
     [("tt-443-fake-ttl4", "--dpi-desync=fake --dpi-desync-ttl=4", 100, "community-unverified",
       "TTNET icin en sik bildirilen deger."),
      ("tt-443-fake-ttl3", "--dpi-desync=fake --dpi-desync-ttl=3", 95, "community-unverified",
       "Bildirilen alternatif TTL."),
      ("tt-443-fake-autottl", "--dpi-desync=fake --dpi-desync-autottl=-1:3-20", 90, "hypothesis",
       "Sabit TTL yerine hop sayisini olcup secer; farkli mesafedeki hedeflerde sabit TTL kirilgan."),
      ("tt-443-fake-multidisorder", UPSTREAM_443, 85, "upstream-preset",
       "Upstream genel 443 kurali."),
      ("tt-443-multisplit-midsld", "--dpi-desync=multisplit --dpi-desync-split-pos=midsld", 75, "hypothesis",
       "Sahte paket kullanmayan aile. DPI sahte paketleri eliyorsa tek calisan bu olur."),
      ("tt-443-multidisorder-pure", "--dpi-desync=multidisorder --dpi-desync-split-pos=1,midsld", 70, "hypothesis",
       "Saf bolmenin ters sirali hali."),
      ("tt-443-fake-badseq", "--dpi-desync=fake --dpi-desync-fooling=badseq", 65, "hypothesis",
       "Sunucudan bagimsiz fooling."),
      ]),

    ("vodafone-net", "Vodafone Net", [], ["vodafone"], 3,
     "ASN dogrulanamadi; org adi anahtar kelimesiyle eslesir. Bildirilen strateji sahte paket ve "
     "bolmeyi birlikte kullaniyor.",
     [("vf-443-fake-multisplit-badseq",
       "--dpi-desync=fake,multisplit --dpi-desync-fooling=badseq --dpi-desync-split-pos=1,midsld",
       100, "community-unverified", "Vodafone Net icin bildirilen strateji."),
      ("vf-443-fake-multidisorder", UPSTREAM_443, 85, "upstream-preset", "Upstream genel 443 kurali."),
      ("vf-443-multisplit-midsld", "--dpi-desync=multisplit --dpi-desync-split-pos=midsld", 70,
       "hypothesis", "Saf bolme ailesi."),
      ]),

    ("turknet", "TurkNet", [], ["turknet"], 4,
     "ASN sorgusu sonuc vermedi; turknet org adinda benzersiz oldugu icin anahtar kelime eslesmesi "
     "guvenli. ISP'ye ozel bildirilmis strateji bulunamadi, genel merdivenle baslanir.",
     [("tn-443-fake-multidisorder", UPSTREAM_443, 100, "upstream-preset",
       "Upstream genel 443 kurali; ISP'ye ozel veri olmadigi icin baslangic noktasi."),
      ("tn-443-multisplit-midsld", "--dpi-desync=multisplit --dpi-desync-split-pos=midsld", 85,
       "hypothesis", "Saf bolme ailesi."),
      ("tn-443-fake-badseq", "--dpi-desync=fake --dpi-desync-fooling=badseq", 70, "hypothesis",
       "Sunucudan bagimsiz fooling."),
      ]),

    ("millenicom", "Millenicom", [34296], ["millenicom"], 5,
     "ISP'ye ozel bildirilmis strateji bulunamadi; genel merdivenle baslanir.",
     [("ml-443-fake-multidisorder", UPSTREAM_443, 100, "upstream-preset", "Upstream genel 443 kurali."),
      ("ml-443-multisplit-midsld", "--dpi-desync=multisplit --dpi-desync-split-pos=midsld", 85,
       "hypothesis", "Saf bolme ailesi."),
      ]),

    ("turksat", "Turksat Kablonet", [], ["turksat", "kablonet"], 6,
     "ASN dogrulanamadi; org adi anahtar kelimesiyle eslesir.",
     [("ts-443-fake-multidisorder", UPSTREAM_443, 100, "upstream-preset", "Upstream genel 443 kurali."),
      ("ts-443-multisplit-midsld", "--dpi-desync=multisplit --dpi-desync-split-pos=midsld", 85,
       "hypothesis", "Saf bolme ailesi."),
      ]),

    ("netspeed", "Netspeed", [206375], ["netspeed"], 7,
     "ISP'ye ozel bildirilmis strateji bulunamadi.",
     [("ns-443-fake-multidisorder", UPSTREAM_443, 100, "upstream-preset", "Upstream genel 443 kurali."),
      ("ns-443-multisplit-midsld", "--dpi-desync=multisplit --dpi-desync-split-pos=midsld", 85,
       "hypothesis", "Saf bolme ailesi."),
      ]),

    ("turkcell-mobil", "Turkcell Mobil", [16135], ["turkcell"], 8,
     "Mobil sebekeler sabit hatlardan farkli DPI davranisi gosterebiliyor; ayri profil tutuluyor.",
     [("tcm-443-fake-autottl", "--dpi-desync=fake --dpi-desync-ttl=1 --dpi-desync-autottl=3", 100,
       "community-unverified", "Turkcell mobil icin bildirilen strateji."),
      ("tcm-443-fake-multidisorder", UPSTREAM_443, 85, "upstream-preset", "Upstream genel 443 kurali."),
      ]),

    ("vodafone-mobil", "Vodafone Mobil", [], ["vodafone"], 9,
     "Bildirilen strateji sahte paket kullanmiyor, saf bolme.",
     [("vfm-443-multisplit-pos2", "--dpi-desync=multisplit --dpi-desync-split-pos=2", 100,
       "community-unverified",
       "Vodafone mobil icin bildirilen strateji. Sahte paket yok -- mobil sebekede sahte paketler "
       "suzuluyor olabilir."),
      ("vfm-443-multisplit-midsld", "--dpi-desync=multisplit --dpi-desync-split-pos=midsld", 85,
       "hypothesis", "Ayni ailenin SLD ortasindan bolen hali."),
      ]),

    ("turk-telekom-mobil", "Turk Telekom Mobil", [], ["turk telekom", "avea", "tt mobil"], 10,
     "Sabit hat TTNET profilinden ayri: bildirilen TTL degeri farkli.",
     [("ttm-443-fake-ttl5", "--dpi-desync=fake --dpi-desync-ttl=5", 100, "community-unverified",
       "TT mobil icin bildirilen strateji."),
      ("ttm-443-fake-autottl", "--dpi-desync=fake --dpi-desync-autottl=-1:3-20", 85, "hypothesis",
       "Otomatik TTL."),
      ]),
]

ORDER = ["id", "section", "args", "protocols", "weight", "source",
         "verifiedFor", "lastVerified", "note"]


def main():
    if not OUT.is_dir():
        raise SystemExit("profiles/isp bulunamadi -- script'i depo kokunden calistir.")

    for pid, name, asns, kws, prio, notes, c443 in ISPS:
        prefix = c443[0][0].split("-")[0]
        cands = []
        for cid, args, weight, src, note in c443:
            cands.append(dict(id=cid, section="tcp443", args=args,
                              protocols=["tls12", "tls13"], weight=weight, source=src,
                              verifiedFor=[], lastVerified=None, note=note))
        for cand in http_base(prefix) + quic_base(prefix) + voice_base(prefix):
            cand.setdefault("verifiedFor", [])
            cand.setdefault("lastVerified", None)
            cands.append(cand)

        doc = {
            "$schema": "../profile.schema.json",
            "id": pid,
            "displayName": name,
            "asns": asns,
            "orgKeywords": kws,
            "engine": "winws",
            "priority": prio,
            "notes": notes,
            "candidates": [{k: c[k] for k in ORDER if k in c} for c in cands],
        }

        ids = [c["id"] for c in doc["candidates"]]
        if len(ids) != len(set(ids)):
            raise SystemExit(pid + ": tekrar eden aday id")

        (OUT / (pid + ".json")).write_text(
            json.dumps(doc, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print("%-22s %2d aday  asn=%s" % (pid, len(cands), asns or "-"))


if __name__ == "__main__":
    main()
