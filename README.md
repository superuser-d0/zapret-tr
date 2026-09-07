# ZapretTR

Windows için GUI'li [zapret](https://github.com/bol-van/zapret) dağıtımı — Türkiye'deki servis
sağlayıcılarına odaklanmış otomatik parametre bulma ile.

> **Durum: geliştirme aşamasında.** Arayüz, test motoru ve profil veritabanı yazıldı ve derleniyor;
> 37 test geçiyor. Ama **henüz gerçek bir bağlantıda çalıştırılmadı** — `winws.exe` bir kez bile
> başlatılmış değil. Aşağıdaki "Yol haritası" nerede olduğumuzu gösteriyor.

---

## Sorun

zapret güçlü bir anti-DPI aracı ama Windows'ta son kullanıcı için pratikte kullanılamıyor. Çalışan
bir strateji bulmanın tek yolu `blockcheck.sh` — cygwin üstünde çalışan, desync metodu × TTL × split
pozisyonu × fooling kombinasyonlarını **tek tek, sırayla** deneyen bir bash scripti. Tam bir tarama
tipik olarak 10-40 dakika sürüyor ve sonunda elinizde kalan şey, bir `.cmd` dosyasına elle
yapıştırmanız gereken bir komut satırı.

## Yaklaşım

ZapretTR, aramayı **sıralamayla** kısaltıyor. Her ISP'nin DPI kutusu tutarlı bir davranış
sergiliyor, dolayısıyla o ISP için daha önce çalıştığı bilinen adaylar önce denenir:

| Aşama | Kapsam | Tipik süre |
|---|---|---|
| Tier 1 | Seçilen ISP'nin profili (5-19 aday) | saniyeler |
| Tier 2 | Komşu TR profilleri | ~1-2 dakika |
| Tier 3 | Genel kombinatoryal arama (180 aday) | dakikalar |

Kullanıcı ISP'sini bilmiyorsa ASN/kuruluş adından otomatik tespit edilir.

### Çalışan strateji tek bir parametre değildir

Tasarımın merkezindeki gözlem şu: `--dpi-desync-fooling=md5sig` yalnızca hedef sunucu TCP MD5
seçeneğini reddettiğinde işe yarar. Yani **çalışan strateji ISP'nin olduğu kadar hedef sunucunun da
fonksiyonu** — aynı bağlantıda Discord'u açan parametre YouTube'u açmayabilir.

Bu yüzden adaylar bölümlere ayrılmış durumda (`tcp80`, `tcp443`, `quic`, `discord-voice`), her bölüm
bağımsız test edilip bağımsız kazananını buluyor, ve nihai komut bölümleri `--new` ile birleştiriyor —
upstream'in kendi `preset1_example.cmd` dosyasıyla aynı yapı.

### Parametre verisinin kaynağı belli

Her adayın nereden geldiği etiketli:

| Etiket | Anlamı |
|---|---|
| `community-unverified` | TR topluluğunda bildirilmiş, biz doğrulamadık |
| `hypothesis` | Belgelenen mekanizmadan türetildi; kimse bildirmedi, biz çıkardık |
| `upstream-preset` | zapret'in kendi örnek preset dosyasından |
| `verified` | Bizim saha testimizde gerçekten çalıştı |

Bu ayrım kozmetik değil: kullanıcıya "bu profil henüz doğrulanmadı" demek ile "bu çalışıyor" demek
arasındaki farkı korumak için.

## Kurulum (geliştirme)

Gereksinimler: .NET 8 SDK, PowerShell, Windows x64.

```bash
powershell -ExecutionPolicy Bypass -File tools/fetch-upstream.ps1
```

Bu, `winws.exe` ve WinDivert sürücüsünü sabitlenmiş bir upstream sürümünden indirip SHA256 ile
doğrular. `vendor/` git'e girmez.

```bash
dotnet build
dotnet test
```

## Depo yapısı

```
src/ZapretTr.Core/        winws süreç yönetimi, komut kurma, profil yükleme
src/ZapretTr.Prober/      parametre test motoru (blockcheck.sh'ın yerini alır)
src/ZapretTr.Prober.Cli/  saha testi için taşınabilir tek dosyalık araç
src/ZapretTr.App/         WPF GUI
profiles/isp/*.json       ISP başına sıralı aday listesi
profiles/generic-ladder.json   Tier 3 kombinatoryal arama tarifi
tools/fetch-upstream.ps1  upstream ikili indirme + SHA256 doğrulama
```

## Yol haritası

Tamamlananlar:

- [x] Repo iskeleti, upstream indirme + SHA256 doğrulama
- [x] ISP profil veritabanı (10 profil) + genel kombinatoryal merdiven
- [x] Komut kurucu, profil yükleyici, testler
- [x] winws süreç yönetimi + WinDivert temizliği
- [x] Test motoru: baseline tarama, protokol sınıfı testleri, BTK engel sayfası tespiti
- [x] WPF GUI (Başlat / Duraklat / Çıkış / Parametre Testi / Sıfırla)
- [x] Gerçek donanımda uçtan uca doğrulama — TTNET'te Discord, Pornhub, XVideos açıldı
- [x] `--ipset-ip` izolasyonunun çalıştığı doğrulandı (winws `--debug=1` çıktısıyla)
- [x] Şifreli DNS (dnscrypt-proxy) + her çıkış yolunda geri alma
- [x] Superonline saha testi paketi (taşınabilir, tek dosya, kendi kendini temizler)

Kalanlar:

- [ ] **Doğrulama kapsamı.** 89 adayın 1'i gerçek bir hatta doğrulandı. Bu bir kod
      eksiği değil saha verisi eksiği; kullanıcılar test çalıştırdıkça doluyor.
- [ ] **QUIC için çalışan strateji yok.** Discord QUIC bu hatta engelli ama
      merdivendeki 15 QUIC adayının hiçbiri açmadı. Yeni aday ailesi gerekiyor.
- [ ] **Superonline saha testi** — paket hazır, test kullanıcısında.
- [ ] Saha paketi 34 MB. `PublishTrimmed` denendi ve GERİ ALINDI: JSON yansımayla
      çalıştığı için kırpma, DNS yedeğinin geri yüklenmesi gibi güvenlik kritik
      yolları sessizce bozabiliyor. Güvenli hale getirmek profil yükleyicisi dahil
      tüm JSON yollarının kaynak üretimine taşınmasını gerektiriyor; 15 MB için
      alınacak risk değil.

Tamamlananlara eklenenler:

- [x] Kalıcılık: seçimler ve öğrenilen doğrulamalar `%ProgramData%\ZapretTR\`
- [x] Otomatik başlatma: `ZapretTR` ve `ZapretTR-DNS` Windows servisleri
- [x] Servis kaldırma yolu gerçek koşumda doğrulandı
- [x] ASN otomatik tespiti — "Bilmiyorum" artık çalışıyor
- [x] Kurulum paketi (Inno Setup, 53 MB, kendi kendine yeten)
- [x] Paralel hedef sınaması
- [x] Discord ses (UDP/STUN) ölçümü — bölüm artık sessiz değil

## Uyarılar

**Yönetici yetkisi zorunlu.** `winws.exe` çekirdek modunda çalışan WinDivert sürücüsünü kullanıyor ve
`--help` için bile yükseltilmiş yetki istiyor.

**Antivirüs uyarısı verebilir.** Paket yakalama sürücüsü + imzasız derleme birleşimi false-positive
üretir. Kod imzalama sertifikamız yok.

## Lisans

MIT — bkz. [LICENSE](LICENSE).

Bu bir **türev üründür**. DPI atlatma işini zapret'in `winws` motoru yapıyor; ZapretTR onu yöneten
arayüz ve otomatik parametre bulma katmanı. Üçüncü taraf bileşenler ve yükümlülükler için
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
