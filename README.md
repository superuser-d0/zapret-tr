# ZapretTR

Windows için GUI'li [zapret](https://github.com/bol-van/zapret) dağıtımı — Türkiye'deki servis
sağlayıcılarına odaklanmış otomatik parametre bulma ile.

> **Durum: çalışıyor, saha verisi toplanıyor.** Uygulama, test motoru ve kurulum paketi gerçek
> donanımda uçtan uca koşuldu; 116 test geçiyor. İki hatta (Türk Telekom / AS9121 ve Turkcell
> Mobil / AS16135) çalışan stratejiler **ölçümle** doğrulandı. Eksik olan kod değil **kapsam**:
> 10 profilin 8'inde henüz tek bir doğrulanmış aday yok, çünkü o hatlara erişimimiz yok.
> Aşağıdaki "Yol haritası" nerede olduğumuzu gösteriyor.

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

Sıra önemli: arayüz duman testleri `vendor/` içindeki ikilileri arıyor, dolayısıyla
`fetch-upstream.ps1` çalıştırılmadan `dotnet test` iki testte başarısız olur.

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
- [x] ISP profil veritabanı (10 profil) + genel kombinatoryal merdiven (221 aday)
- [x] Komut kurucu, profil yükleyici, testler (116 test)
- [x] winws süreç yönetimi + WinDivert temizliği
- [x] Test motoru: baseline tarama, protokol sınıfı testleri, BTK engel sayfası tespiti
- [x] WPF GUI (Başlat / Duraklat / Çıkış / Parametre Testi / Sıfırla)
- [x] Gerçek donanımda uçtan uca doğrulama — TTNET'te Discord, Pornhub, XVideos açıldı
- [x] `--ipset-ip` izolasyonunun çalıştığı doğrulandı (winws `--debug=1` çıktısıyla)
- [x] Şifreli DNS (dnscrypt-proxy) + her çıkış yolunda geri alma
- [x] Superonline saha testi paketi (taşınabilir, kendi kendini temizler)
- [x] Kalıcılık: seçimler ve öğrenilen doğrulamalar `%ProgramData%\ZapretTR\`
- [x] Otomatik başlatma: `ZapretTR` ve `ZapretTR-DNS` Windows servisleri
- [x] Servis kaldırma yolu gerçek koşumda doğrulandı
- [x] ASN otomatik tespiti — "Bilmiyorum" artık çalışıyor
- [x] Kurulum paketi (Inno Setup, kendi kendine yeten) — tam yaşam döngüsü koşuldu
- [x] Paralel hedef sınaması
- [x] Discord ses (UDP/STUN) ölçümü — bölüm artık sessiz değil
- [x] **QUIC.** Hem ölçüm yolu hem çalışan strateji bulundu. Uzun süre "engelli"
      sanılan şeyin bir kısmı bizim ölçüm hatamızmış; ayrıntısı `docs/DEVAM.md`'de.
- [x] **Paket boyutu.** `PublishTrimmed` açık ve güvenli: bütün JSON yolları kaynak
      üretimine taşındı, kırpma analizörü hata verecek şekilde açık. 34.3 → 12.5 MB.

Kalanlar:

- [ ] **Doğrulama kapsamı — asıl eksik bu.** Gerçek bir hatta doğrulanmış aday
      sayısı 25: turk-telekom'da 23 (tcp443 10, tcp80 6, quic 7), turkcell-mobil'de 2.
      Kalan 8 profilde sıfır. Kod eksiği değil, saha verisi eksiği.
- [ ] **`discord-voice` hiçbir profilde doğrulanmadı ve dışarıdan doğrulanamıyor.**
      Discord'un ses yolu kendi IP-keşif protokolünü kullanıyor; sunucu adresi ancak
      kimlik doğrulamalı bir ses oturumundan alınıyor. Genel STUN engellenmediği için
      vekil hedefle de ölçülemiyor. (`tcp80` artık doğrulandı — eksik olan hattın
      temizliği değil, hedef listesinde engelli bir tcp80 adresi bulunmamasıydı.)
- [ ] **Superonline saha testi** — paket hazır, test kullanıcısında. Turkcell Mobil
      hotspot bunun yerine geçmiyor: ayrı ağ, ayrı ASN, ölçülen davranışı da farklı.
- [ ] **Kod imzalama sertifikası yok.** Defender bu paketi işaretlemiyor (ölçüldü),
      ama SmartScreen "bilinmeyen yayımcı" uyarısı verecek. Sertifika alınana kadar
      kullanıcının elindeki tek doğrulama aracı yayındaki SHA256 özetleri.
- [ ] **`--dns test` bir makinede geçmiyordu, orada yeniden üretilemedi.** Başka bir
      makinede sıcak ve soğuk başlangıçta sorunsuz geçti. Güvenli tarafa düşüyor
      (sistem DNS'ine dokunmuyor, temiz geri alıyor); tekrar görülürse sebebini
      söylemesi için hata mesajı artık süreç ve port durumunu taşıyor.

## Yayın ve sürüm

Sürüm tek kaynaktan gelir: git tag'i. `Directory.Build.props` derlenen exe'lerin
sürümünü, `installer/setup.iss` kurulum paketininkini taşır; ikisi de yayın akışında
tag'den beslenir (`-p:Version=`, `/DAppVersion=`).

- `.github/workflows/ci.yml` — her itmede: upstream indirme, derleme, testler,
  kırpılmış yayın (kırpma analizörü hata verirse burada patlar) ve `msquic.dll`
  kontrolü.
- `.github/workflows/release.yml` — `v*` tag'i itildiğinde kurulum paketini, saha
  testi paketini ve `SHA256SUMS.txt` dosyasını üretip **taslak** yayın açar.

Yayın taslak olarak açılır; yayımlamadan önce içeriğinin gözden geçirilmesi kasıtlı.

Değişiklikler [CHANGELOG.md](CHANGELOG.md) dosyasında.

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
