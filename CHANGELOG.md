# Değişiklik günlüğü

Biçim [Keep a Changelog](https://keepachangelog.com/tr/1.1.0/) temelli,
sürümleme [Semantic Versioning](https://semver.org/lang/tr/).

Bu dosyada "doğrulandı" kelimesi dar bir anlam taşır: **gerçek bir hatta, ölçümle**.
Toplulukta bildirilmiş ya da mekanizmadan türetilmiş şeyler doğrulanmış sayılmaz.

## [Yayınlanmamış]

## [0.1.0] — ilk yayın

İlk kamuya açık sürüm.

### Eklendi

- **Test motoru.** `blockcheck.sh`'ın yerine geçen, ISP profiliyle sıralanmış
  parametre araması. Bölümler (`tcp80`, `tcp443`, `quic`, `discord-voice`)
  bağımsız aranır ve bağımsız kazananı olur; nihai komut bölümleri `--new` ile
  birleştirir.
- **ISP profil veritabanı.** 10 profil ve 221 adaylık genel kombinatoryal
  merdiven. Her adayın kaynağı etiketli: `verified`, `community-unverified`,
  `hypothesis`, `upstream-preset`.
- **WPF arayüz.** Başlat / Duraklat / Çıkış / Parametre Testi / Sıfırla.
- **Şifreli DNS (dnscrypt-proxy).** Türkiye'de engelleme çoğu zaman iki katmanlı;
  DNS katmanı aşılmadan DPI stratejisi sonuç vermiyor. Sistem DNS'i yalnızca
  çözümleyicinin cevap verdiği doğrulandıktan sonra yönlendirilir ve her çıkış
  yolunda geri alınır.
- **Kalıcılık ve otomatik başlatma.** Seçimler ve öğrenilen doğrulamalar
  `%ProgramData%\ZapretTR\`; `ZapretTR` ve `ZapretTR-DNS` Windows servisleri.
- **ASN otomatik tespiti.** Kullanıcı sağlayıcısını bilmiyorsa tespit edilir.
- **Kurulum paketi** (Inno Setup) ve **taşınabilir saha testi paketi**.
- **Discord ses (UDP/STUN) ölçümü.** Bölüm daha önce hiç sınanmıyordu.
- **`--exhaustive`.** İlk başarıda durmaz, bütçe bitene kadar dener. Doğrulama
  verisi toplamak için; normal kullanımda gereksiz.
- **`--save-learned`.** CLI'nin bulduğu doğrulamaları `learned.json`'a yazar.
  Öncesinde bunu yalnızca arayüz yapıyordu, yani saha aracıyla bulunan hiçbir
  doğrulama arayüze ya da servise geçmiyordu.
- **`--detect-isp`**, **`--diagnose`**, **`--engage-check`**, **`--cleanup`**.
- **`--target` artık `--section` ile bölüm seçebiliyor.** Öncesinde kullanıcının
  eklediği her adres tcp443 sayılıyordu, yani düz HTTP'de açılmayan bir adres
  yanlış bölümde ölçülüyordu.
- **CI ve yayın akışı.** Her itmede derleme, test ve kırpılmış yayın kontrolü;
  tag itildiğinde kurulum paketi, saha paketi ve SHA256 özetleriyle taslak yayın.

### Düzeltildi

- **İlk açılışta tespit edilmemiş bir servis sağlayıcı seçili geliyordu.** Uygulama
  listedeki ilk profili seçiyordu (Turkcell Superonline); Türk Telekom hattında
  kurulup açıldığında bu görüldü. Kullanıcının doğrudan "Başlat"a basması, kendi
  hattında hiç denenmemiş bir stratejiyi trafiğe uygulaması demekti. Artık
  "Bilmiyorum / otomatik tespit et" seçili geliyor ve sağlayıcı bilinmeden
  "Başlat" açılmıyor. Tespit açılışta kendiliğinden çalıştırılmıyor: ASN sorgusu
  kullanıcının IP'sini üçüncü bir servise gönderiyor ve bu, kullanıcı bir şey
  istemeden yapılacak bir şey değil.
- **`fetch-upstream.ps1`'in SHA256 doğrulaması hiç koşmuyordu.** Artımlı indirmeye
  geçişten kalan bir dal, StrictMode altında betiği her koşumda doğrulamadan önce
  düşürüyordu. Dosyalar iniyor, doğrulama atlanıyordu.
- **Aynı strateji birden çok kez deneniyordu.** Aday tekilleştirmesi tier'ların
  içinde vardı, arasında yoktu; ölçüldü: 40 denemenin 8'i (%20) tekrardı. Ayrıca
  yalnızca bayrak sırası farklı olan adaylar da ayrı sayılıyordu.

### Doğrulanmış parametreler

Gerçek hatta ölçümle doğrulanan aday sayısı:

| Profil | tcp80 | tcp443 | quic | discord-voice |
|---|---|---|---|---|
| turk-telekom (AS9121) | 6 | 10 | 7 | — |
| turkcell-mobil (AS16135) | — | 1 | 1 | — |
| diğer 8 profil | — | — | — | — |

Doğrulama yöntemi: aynı komut üç bağımsız koşum, yalnızca **3/3** geçen adaylar
`verified`. Bir aday 1/3 çıktığı için elendi.

### Bilinen sınırlar

- **Paket imzalı değil.** Kod imzalama sertifikası yok, dolayısıyla SmartScreen
  "bilinmeyen yayımcı" uyarısı verecek. Antivirüs tarafı ölçüldü: Windows Defender
  (motor 1.1.26080.3, imza 1.459.93.0) kurulum paketini, saha paketini ve kurulu
  dizini taradı ve **sıfır tespit** verdi. Yani beklenen sorun imzasızlığın kendisi;
  çekirdek modunda paket yakalama sürücüsü barındıran imzasız bir paketin başka
  tarayıcılarda false-positive üretmesi hâlâ mümkün.
- **`discord-voice` hiçbir profilde doğrulanmadı** ve dışarıdan doğrulanamıyor:
  Discord'un ses yolu kendi IP-keşif protokolünü kullanıyor ve sunucu adresi
  ancak kimlik doğrulamalı bir ses oturumundan alınıyor. Genel STUN bu hatta
  engellenmiyor, dolayısıyla vekil bir hedefle de ölçülemiyor.
- **8 profilde sıfır saha verisi.** O hatlara erişim yok. Arayüz bu durumu
  "doğrulanmadı" rozetiyle açıkça gösterir.

[Yayınlanmamış]: https://github.com/superuser-d0/zapret-tr/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/superuser-d0/zapret-tr/releases/tag/v0.1.0
