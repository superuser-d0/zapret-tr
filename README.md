# ZapretTR

Windows için GUI'li [zapret](https://github.com/bol-van/zapret) dağıtımı — Türkiye'deki servis
sağlayıcılarına odaklanmış otomatik parametre bulma ile.

> **Durum: geliştirme aşamasında.** Çekirdek katman ve profil veritabanı hazır, GUI henüz yok.
> Aşağıdaki "Yol haritası" bölümü nerede olduğumuzu gösteriyor.

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

- [x] Repo iskeleti, upstream indirme + doğrulama
- [x] ISP profil veritabanı (10 profil) + genel merdiven
- [x] Komut kurucu, profil yükleyici, testler
- [ ] winws süreç yönetimi + WinDivert temizliği
- [ ] Paralel test izolasyonu doğrulaması (`--ipset-ip` spike)
- [ ] Test motoru: baseline tarama, protokol sınıfı testleri, engel sayfası tespiti
- [ ] Superonline saha testi paketi
- [ ] ASN otomatik tespiti
- [ ] WPF GUI (Başlat / Duraklat / Çıkış / Parametre Testi / Sıfırla)
- [ ] Servis kurulumu + Inno Setup installer

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
