<div align="center">

# ZapretTR

**Türkiye'deki DPI engellemelerini aşan parametreyi sizin yerinize bulan Windows uygulaması.**

[zapret](https://github.com/bol-van/zapret)'in `winws` motoru üzerine kurulu bir arayüz —
elle parametre denemek yerine, sizin hattınızda gerçekten ne çalışıyorsa onu ölçerek buluyor.

[![yayın](https://img.shields.io/github/v/release/superuser-d0/zapret-tr?label=s%C3%BCr%C3%BCm&color=2b7489)](https://github.com/superuser-d0/zapret-tr/releases/latest)
[![derle ve test](https://github.com/superuser-d0/zapret-tr/actions/workflows/ci.yml/badge.svg)](https://github.com/superuser-d0/zapret-tr/actions/workflows/ci.yml)
[![lisans](https://img.shields.io/badge/lisans-MIT-blue)](LICENSE)
[![platform](https://img.shields.io/badge/platform-Windows%20x64-0078d4)](https://github.com/superuser-d0/zapret-tr/releases/latest)

### [⬇ İndir](https://github.com/superuser-d0/zapret-tr/releases/latest) · [Kolay kullanım](#kolay-kullanım) · [Sık sorulanlar](#sık-sorulanlar)

<img src="docs/ekran-goruntusu.png" alt="ZapretTR arayüzü" width="380">

</div>

---

## Ne yapıyor

`blockcheck.sh` ile parametre aramak 10-40 dakika sürüyor ve sonunda elinizde bir `.cmd`
dosyasına yapıştırmanız gereken komut satırı kalıyor. ZapretTR bunu **iki düğmeye** indiriyor:
hattınızı tespit ediyor, o hat için bilinen adayları sırayla ölçüyor ve çalışanı buluyor.

| | |
|---|---|
| **Otomatik ISS tespiti** | ASN'den hattınızı bulur; "Bilmiyorum" birinci sınıf bir seçenek |
| **Ölçerek bulur** | Her aday gerçekten bağlanılarak sınanır, tahmin edilmez |
| **Bölüm bölüm** | `tcp80`, `tcp443`, `quic`, `discord-voice` bağımsız aranır ve birleştirilir |
| **Şifreli DNS** | Türkiye'de engelleme çoğu zaman iki katmanlı; DNS katmanı da aşılır |
| **Dokunmadığı yeri bozmaz** | Sorunu olmayan bölüme denenmemiş strateji uygulanmaz |
| **Ne bildiğini söyler** | Her aday "doğrulandı / doğrulanmadı" etiketiyle gelir |

> **Durum: çalışıyor.** Kurulum paketi indirilip gerçek bir makineye kuruldu ve normal bir
> kullanıcı akışıyla kullanıldı: parametre testi hattı tespit etti, çalışan stratejiyi buldu ve
> Başlat'tan sonra engelli adresler açıldı — bağımsız bir istemciyle (`curl`) doğrulandı, ölçüm
> motorunun kendi raporuyla değil. Türk Telekom / AS9121 hattında **25 aday** doğrulanmış durumda.
>
> Eksik olan kod değil **kapsam**: 10 profilin 8'inde henüz saha verisi yok, çünkü o hatlara
> erişimimiz yok. **Testçi arıyoruz** — [hangi hatların eksik olduğu](#hangi-hatlarda-doğrulandı).

---

## Kolay kullanım

**1. İndir.** [Releases](https://github.com/superuser-d0/zapret-tr/releases) sayfasından
`ZapretTR-Setup-<sürüm>.exe` dosyasını indirin.

**2. Kur.** Dosyaya çift tıklayın.

- Windows **"bilgisayarınızı korudu"** uyarısı verirse: bu **beklenen**. Paket imzalı değil
  (kod imzalama sertifikamız yok). "Ek bilgi" → "Yine de çalıştır".
- **Yönetici izni** ister. Gerekli: uygulama çekirdek modunda çalışan bir ağ sürücüsü kullanıyor.
- İndirdiğiniz dosyanın bu yayından geldiğini doğrulamak isterseniz yayındaki `SHA256SUMS.txt`
  ile karşılaştırın:
  ```powershell
  Get-FileHash .\ZapretTR-Setup-<sürüm>.exe -Algorithm SHA256
  ```

**3. Üç tıkla kullan.** Uygulama açıldığında:

| Adım | Ne yapacaksınız | Ne göreceksiniz |
|---|---|---|
| 1 | Hiçbir şeyi değiştirmeyin | Servis sağlayıcı: **"Bilmiyorum / otomatik tespit et"**, Başlat kapalı |
| 2 | **PARAMETRE TESTİ YAP** | Hattınız tespit edilir, çalışan parametre aranır (**birkaç dakika**) |
| 3 | **ZAPRET'İ BAŞLAT** | Test bitince "STRATEJİ BULUNDU" yazar ve Başlat açılır |

Hepsi bu. **"Şifreli DNS kullan" işaretli kalsın** — Türkiye'de engelleme çoğu zaman iki katmanlı
ve o kutu kapalıyken alttaki katman aşılamaz.

**Açılmayan kendi adresiniz varsa** "Açılmayan site" kutusuna yazıp testi öyle çalıştırın. Asıl
doğru kullanım budur: kimin neye erişemediği kişiye göre değişiyor.

**Her açılışta çalışsın istiyorsanız** "Servis Olarak Yükle" düğmesi Windows servisi kurar.

### Sık sorulanlar

**Test neden birkaç dakika sürüyor?** Her aday için gerçekten bağlantı kurulup ölçülüyor.
Sonuç kaydedilir; bir sonraki açılışta test tekrar gerekmez.

**"ENGEL BULUNAMADI" derse?** Test hedeflerinin hepsi zaten açılıyor demektir. Sizde açılmayan
adresi "Açılmayan site" kutusuna girip tekrar deneyin.

**Antivirüs uyarırsa?** Paket sürücüsü + imzasız derleme birleşimi false-positive üretebilir.
Windows Defender bu paketi işaretlemiyor (ölçüldü), diğerleri için garanti veremeyiz.

**İnternetim gitti / adresler çözülmüyor?** Uygulama şifreli DNS için sistem DNS'ini kendine
yönlendiriyor ve her çıkışta geri alıyor. Bir şekilde yarım kaldıysa, kurulum klasöründeki
araçla geri alabilirsiniz:
```powershell
& "$env:ProgramFiles\ZapretTR\ZapretTR.exe" --uninstall-services
```
Kaldırma (Program Ekle/Kaldır) da aynı temizliği yapar.

**Sesli görüşme (Discord voice) çalışıyor mu?** Evet. Türk Telekom hattında gerçek kullanımda
denendi: **sesli görüşme de ekran paylaşımı da çalıştı.** Ekran paylaşımı ayrıca anlamlı, çünkü
sesten çok daha ağır bir medya akışı.

Mekanizması şu (bir sonraki soruyu baştan cevaplasın diye): o hatta ses zaten engelli değil,
metin ve bağlantı engeli aşılınca kendiliğinden kuruluyor. ZapretTR ses trafiğine **hiç
dokunmuyor** — "sorunu olmayan bölüme dokunma" kuralı gereği o bölüm komuta hiç girmiyor.

Sizde ses **çalışmıyorsa** dürüst cevap: o durum için doğrulanmış bir stratejimiz yok ve araç
size denenmemiş bir şey uygulamaz. Sebebi teknik — Discord'un ses yolu kendi IP-keşif
protokolünü kullanıyor ve sunucu adresi ancak kimlik doğrulamalı bir ses oturumundan alınıyor,
dolayısıyla dışarıdan ölçemiyoruz.

---

## Neden var

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
| Tier 3 | Genel kombinatoryal arama (221 aday) | dakikalar |

Kullanıcı ISP'sini bilmiyorsa ASN/kuruluş adından otomatik tespit edilir.

Genel aramada sıra **aileler arasında** dolaşıyor: önce her strateji ailesinden birer aday,
sonra derinleşiliyor. Aksi halde tek bir ailenin onlarca varyantı bütçeyi yiyor ve hiç
denenmemiş mekanizmalara sıra gelmiyordu — ölçülen bir kullanıcıda tam bu oldu.

### Çalışan strateji tek bir parametre değildir

Tasarımın merkezindeki gözlem şu: `--dpi-desync-fooling=md5sig` yalnızca hedef sunucu TCP MD5
seçeneğini reddettiğinde işe yarar. Yani **çalışan strateji ISP'nin olduğu kadar hedef sunucunun da
fonksiyonu** — aynı bağlantıda Discord'u açan parametre YouTube'u açmayabilir.

Bu, doğrulanmış adayların ne kadar genellenebildiğini de bir soru haline getiriyor: hedeflerimizin
çoğu Cloudflare arkasında, dolayısıyla "doğrulandı" damgası yalnızca tek bir sunucu ailesini
yansıtıyor olabilirdi. TTNET hattında ölçüldü ve öyle değil — aynı strateji, farklı barındırıcılarda
da gerçek sunucuya ulaştırıyor:

| Hedef | Barındıran | Koruma açıkken |
|---|---|---|
| discord.com | Cloudflare | HTTP 200 |
| pornhub.com | Cloudflare **değil** | HTTP 301 |
| xvideos.com | Cloudflare **değil** | HTTP 301 |
| www.youtube.com | Google (engelli değil) | HTTP 200 — etkilenmedi |

Hedef listesi bu yüzden dar ama **keyfî değil**: bir hedef ancak yeni bilgi veriyorsa ekleniyor.
"Barındırıcıya göre değişiyor mu" sorusu yukarıdaki ölçümle cevaplandığı için genel site listesi
büyütülmedi. Buna karşılık `updates.discord.com` eklendi — çünkü Discord istemcisi güncelleme için
oraya gidiyor ve o adres açılmazsa uygulama güncelleme ekranında takılı kalıyor; test ise
"başarılı" diyordu.

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

### Hangi hatlarda doğrulandı

"Doğrulandı" burada dar bir anlam taşır: **gerçek bir hatta, ölçümle** — aynı komut üç bağımsız
koşumda 3/3 geçtiyse. Toplulukta bildirilmiş ya da mekanizmadan türetilmiş şeyler sayılmaz.

| Servis sağlayıcı | tcp80 | tcp443 | QUIC | Durum |
|---|:---:|:---:|:---:|---|
| Türk Telekom (AS9121) | 6 | 10 | 7 | ✅ doğrulandı |
| Turkcell Mobil (AS16135) | — | 1 | 1 | ✅ doğrulandı |
| Superonline | — | — | — | ⬜ **testçi aranıyor** |
| TurkNet | — | — | — | ⬜ **testçi aranıyor** |
| Türksat | — | — | — | ⬜ **testçi aranıyor** |
| Vodafone (sabit / mobil) | — | — | — | ⬜ **testçi aranıyor** |
| Millenicom · NetSpeed · TT Mobil | — | — | — | ⬜ **testçi aranıyor** |

Bu hatlardan birindeyseniz: uygulamayı kurup **Parametre Testi**'ni çalıştırmanız ve sonucu
[bir issue'da](https://github.com/superuser-d0/zapret-tr/issues) paylaşmanız yeter. Test hiçbir
yere veri göndermiyor; ne paylaşacağınıza siz karar veriyorsunuz.

Aynı ISS içinde bile davranış değişebiliyor: üç ayrı Türk Telekom hattında üç farklı sonuç
alındı (birinde düz HTTP engelliydi, diğerinde değil). Yani "profil var" demek "sizde çalışır"
demek değil — ölçüm bunun için var.

Kalanlar:

- [ ] **Doğrulama kapsamı — asıl eksik bu.** Yukarıdaki tabloya bakın: 10 profilin
      8'inde sıfır saha verisi. Kod eksiği değil, o hatlara erişim eksiği.
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
