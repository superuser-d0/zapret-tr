# Devam notları

Bu dosya, projeye sıfırdan başlayan bir oturumun ucuza yeniden türetemeyeceği
bağlamı taşır. Kod okunarak anlaşılabilecek şeyler burada yok; **ölçümle
öğrenilmiş** şeyler var.

---

## Proje nedir

`bol-van/zapret`'in `winws` motoru üzerine kurulmuş, Türkiye'ye odaklı Windows
uygulaması. zapret fork'lanmadı: ikililer `tools/fetch-upstream.ps1` ile
sabitlenmiş sürümden indirilip SHA256 ile doğrulanıyor, `vendor/` git'e girmiyor.

Asıl değer `blockcheck.sh`'ın yerine geçen test motorunda: arama, ISP profiliyle
5-19 adaya iniyor, tutmazsa kademeli genişliyor.

Depo: https://github.com/superuser-d0/zapret-tr (private)

---

## Bitmiş ve gerçek donanımda doğrulanmış

- Test motoru (baseline tarama, protokol sınıfları, BTK engel sayfası tespiti)
- Şifreli DNS (dnscrypt-proxy) + her çıkış yolunda geri alma
- WPF arayüz, kalıcılık, otomatik başlatma servisleri
- ASN otomatik tespiti ("Bilmiyorum" akışı)
- Kurulum paketi (Inno Setup) — tam yaşam döngüsü koşuldu
- Paralel hedef sınaması
- Discord ses (UDP/STUN) ölçümü

**Ölçülmüş sonuç (TTNET, gerçek hat):** şifreli DNS + `--dpi-desync=fake
--dpi-desync-ttl=4` ile discord.com, pornhub.com, xvideos.com açılıyor.

---

## Yapılacaklar

### 1. QUIC için çalışan strateji yok — tek gerçek işlevsel açık

Discord QUIC bu hatta engelli (zaman aşımı) ama `generic-ladder.json`'daki
15 QUIC adayının **hiçbiri** açmıyor. Mevcut aileler: `fake-quic` (sahte Initial
paketi, hazır yükle/yüksüz, farklı tekrar sayıları), `udplen`, `ipfrag`.

Yapılacak: yeni aday ailesi türetmek. Bakılacak yerler — upstream'in
`--dpi-desync-fake-quic` için ürettiği farklı yükler (`files/fake/` altında
`quic_initial_*` varyantları var, şu an yalnızca `www_google_com` indiriliyor),
`--dpi-desync-udplen-pattern`, `--dpi-desync-fake-tls-mod` benzeri QUIC
karşılıkları, ve `--dpi-desync-repeats` ile birlikte `--dpi-desync-cutoff`.

Doğrulama: `zapret-tr-test.exe --doh --isp turk-telekom --max-candidates 40`

### 2. Doğrulama kapsamı — 89 adayın 1'i doğrulanmış

Kod eksiği değil saha verisi eksiği. Yalnızca `tt-443-fake-ttl4` gerçek bir hatta
doğrulandı. Kullanıcılar test çalıştırdıkça `%ProgramData%\ZapretTR\learned.json`
doluyor ve profillerin üzerine bindiriliyor.

Hızlandırmak için: bu makinede `--max-candidates` yüksek tutup `stopAtFirstSuccess`
kapalı koşumlar yapılabilir (şu an CLI'da bunun bayrağı yok, eklenebilir).

### 3. Superonline saha testi — dış bağımlılık

Paket hazır: `dist/zapret-tr-saha-testi.zip` (34 MB). Test kullanıcısında.
Rapor gelince `learned.json`'a aktarılıp `profiles/isp/superonline.json`
`verified`e çekilecek. Superonline'ın 19 adayının hiçbiri doğrulanmadı.

### 4. Paket boyutu (düşük öncelik)

Saha paketi 34 MB, paylaşım limitlerinin üstünde. `PublishTrimmed` **denendi ve
geri alındı**: JSON yansımayla çalıştığı için kırpma, DNS yedeğinin geri
yüklenmesi gibi güvenlik kritik yolları sessizce bozabiliyor. Güvenli hale
getirmek profil yükleyicisi, hedef listesi, DoH yanıtları ve rapor DTO'su dahil
tüm JSON yollarını kaynak üretimine taşımayı gerektirir. DNS ve yapılandırma
yolları zaten taşındı (`CoreJsonContext`).

---

## Tuzaklar — hepsi bir kez ısırdı

**XML yorumlarında çift tire yasak.** `app.manifest` içinde `--help` yazmıştım;
uygulama "side-by-side configuration is incorrect" ile **hiç açılmadı** ve
derleme bunu yakalamıyor (manifest ancak işletim sistemi süreç oluştururken
ayrıştırılıyor). Artık `XmlCommentTests` bunu koruyor.

**`RuntimeIdentifier` çıktı yolunu kaydırıyor.** Debug çıktısı
`bin/Debug/net8.0-windows/win-x64/` altında. Eski konumdaki bayat exe bir koşumu
boşa harcattı.

**Yükseltilmiş süreçlerin çalışma dizini `C:\Windows\System32`.** Göreli `--out`
yolu raporu oraya düşürüyordu; artık `AppContext.BaseDirectory` ile mutlaklaştırılıyor.

**Bash heredoc + Python + `\n` kaçışları bozuluyor.** İçinde kaçış olan C# dizgileri
için Python değil **Edit aracı** kullan. Birkaç kez derleme kırdı.

**PowerShell koruması `/b` ve `/d` bayraklarını yol sanıp engelliyor.** `cd /d`,
`start /b` içeren komut metinleri reddediliyor.

**UAC istemleri makinenin başında onay istiyor.** Reddedilirse süreç sessizce
çalışmıyor; birkaç kez test yarıda kaldı. Uzaktan çalışan bir oturum bunu
kendisi geçemez.

---

## Tasarım kararları — geri alınmadan önce oku

**"Başarı" = gerçek sunucuya ulaşıldı**, HTTP 200 değil. 301 (www yönlendirmesi)
ve 404 (sunucunun normal cevabı) başarıdır. İlk sürümde yalnızca 2xx başarı
sayılıyordu ve çalışan bir strateji "başarısız" raporlanıyordu. İstisnalar: engel
sayfası ve HTTP 400.

**Sorunu olmayan bölüme dokunma.** Yalnızca kullanıcının seçtiği strateji ve
**doğrulanmış** adaylar uygulanır (`RuntimeSelection`). Ölçülmüş zarar: sorunsuz
çalışan QUIC bağlantısı, üzerine denenmemiş bir QUIC stratejisi uygulanınca
bozuldu.

**Engel sayfası tespitinde güçlü/zayıf ayrımı.** `erisime_engellenmis`,
`btk.gov.tr` tek başına yeterli; "koruma tedbiri" gibi genel ifadeler en az iki
eşleşme ister — sansürü *anlatan* bir haber sayfası engel sayfası değildir.

**DNS yedeğinde sahiplik alanı.** Servis kurulduğunda yönlendirme `service`
sahipliğiyle yapılır ve uygulama kapanırken ona dokunmaz.

**Türkiye'de engelleme iki katmanlı.** Önce DNS kaçırma (engel sunucusuna
çözümleme), altta SNI'ye bakan DPI. winws yalnızca ikincisini aşar; birincisi
durdukça hiçbir strateji işe yaramaz. Test `--doh` olmadan koşulursa alttaki
katman hiç görünmez.

**Paralellik hedef bazında, aday bazında değil.** Aynı hedefe iki strateji birden
uygulanamaz; ikisi de aynı paketleri görür ve sonuç belirsizleşir.

---

## Komutlar

```
# upstream ikilileri (winws + dnscrypt-proxy)
powershell -ExecutionPolicy Bypass -File tools/fetch-upstream.ps1

dotnet build
dotnet test tests/ZapretTr.Tests

# CLI (yönetici gerekir) — bin/Debug/net8.0-windows/win-x64/
zapret-tr-test.exe --doh --isp turk-telekom --max-candidates 20 -y
zapret-tr-test.exe --diagnose discord.com     # tek adres, dört protokol
zapret-tr-test.exe --engage-check discord.com # winws paketleri görüyor mu
zapret-tr-test.exe --dns test                 # DNS döngüsü, kendini geri alır
zapret-tr-test.exe --service kur|kaldir|durum
zapret-tr-test.exe --cleanup                  # sürücüyü kaldır, DNS'i geri al

# kurulum paketi
dotnet publish src/ZapretTr.App -c Release -o publish/app -p:SelfContained=true -p:RuntimeIdentifier=win-x64 -p:DebugType=none
"%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\setup.iss

# saha paketi
powershell -ExecutionPolicy Bypass -File tools/build-field-package.ps1
```

---

## Makine durumu (son oturum sonu)

Temiz: kurulum kaldırıldı, servis yok, DNS `192.168.8.1` (DHCP), süreç yok,
internet normal. `%ProgramData%\ZapretTR\config.json` duruyor (kullanıcı ayarı,
kaldırma bunu silmiyor).

Test hattı: Türk Telekom / AS9121, VPN yok. Superonline erişimi yok.
