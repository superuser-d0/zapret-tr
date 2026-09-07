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
- **QUIC** — ölçüm yolu ve çalışan strateji (aşağıda)

**Ölçülmüş sonuç (TTNET, gerçek hat):** şifreli DNS + `--dpi-desync=fake
--dpi-desync-ttl=4` ile discord.com, pornhub.com, xvideos.com açılıyor.

**Ölçülmüş sonuç — QUIC (TTNET, gerçek hat):**

```
--dpi-desync=fake --dpi-desync-any-protocol=1 --dpi-desync-cutoff=n2 \
--dpi-desync-fake-quic={FAKE_QUIC_GOOGLE}
```

discord.com QUIC el sıkışmasını açıyor (kontrollü tekrar: 3/3 başarılı;
`any-protocol`'süz birebir aynı aday 2/2 zaman aşımı). Profilde
`tt-quic-anyproto-cutoff` olarak `verified`. Tam koşumda profilin ilk QUIC adayı
olarak saniyeler içinde bulunuyor.

**Neden `any-protocol` gerekiyor** (winws `--debug=1` çıktısından): discord.com'un
İLK Initial paketi sorunsuz ayrışıyor — `hostname: discord.com` bulunuyor. Ama
Discord istemcisi Chromium tabanlı ve Kyber anahtar paylaşımıyla ClientHello'yu
~2.5 KB'a yayıyor; DEVAM paketlerinde `QUIC initial defrag CRYPTO failed` /
`decryption failed` çıkıyor ve winws bunları
`not applying tampering because desync_any_proto is not set` diyerek atlıyor.
Yani strateji paketlerin bir kısmına hiç uygulanmıyordu.

`--dpi-desync-cutoff` ZORUNLU. winws'in kendi uyarısı:
`WARNING !!! ... --dpi-desync-any-protocol without --dpi-desync-cutoff`.
Cutoff'suz any-protocol bağlantının TÜM paketlerine müdahale eder — çalışan bir
bağlantıyı bozmak, açmayan bir bağlantıyı açamamaktan kötü.

**Ölçülmüş sonuç — Turkcell Mobil (AS16135, gerçek hat, tethering):**

```
tcp443 : --dpi-desync=fake --dpi-desync-ttl=1 --dpi-desync-autottl=3
quic   : --dpi-desync=fake --dpi-desync-repeats=11
```

İkisi de kontrollü tekrarda 3/3. Profilde `tcm-443-fake-autottl` ve
`tcm-quic-fake-plain` olarak `verified`.

**Mobil, sabit hattan gerçekten farklı — türetilemez.** İki somut fark:

1. Discord TCP: TTNET'te **RST**, Turkcell Mobil'de **zaman aşımı**.
2. QUIC: TTNET'te `any-protocol`+`cutoff` şart; mobilde düz
   `fake --repeats=11` yetiyor. Dahası mobilde **sahte yük eklemek zarar
   veriyor** — yüklü aday 0/2, yüksüz aday 3/3. `tcm-quic-fake-google`'ın
   ağırlığı bu yüzden yüksüzün altına çekildi.

Yani sahte yük seçimi hatta göre değişen bir eksen; "google yükü hep iyidir"
varsayımı yanlış.

---

## Yapılacaklar

### 1. Doğrulama kapsamı — 4 aday doğrulanmış

Kod eksiği değil saha verisi eksiği. Gerçek hatta doğrulanan adaylar:

| Profil | Bölüm | Aday |
|---|---|---|
| turk-telekom (AS9121) | tcp443 | `tt-443-fake-ttl4` |
| turk-telekom (AS9121) | quic | `tt-quic-anyproto-cutoff` |
| turkcell-mobil (AS16135) | tcp443 | `tcm-443-fake-autottl` |
| turkcell-mobil (AS16135) | quic | `tcm-quic-fake-plain` |

Hiçbir profilde `tcp80` ve `discord-voice` doğrulanmadı — o bölümler bu iki hatta
zaten engelli değil, dolayısıyla buradan ölçülemezler. Kullanıcılar test
çalıştırdıkça `%ProgramData%\ZapretTR\learned.json` doluyor ve profillerin üzerine
bindiriliyor.

Not: paralel sınama hatası düzeltilene kadar QUIC sonuçları kararsızdı (tuzaklar
bölümüne bak). Bu düzeltmeden ÖNCE toplanmış `learned.json` verisi varsa QUIC
bölümü için güvenilmez.

Hızlandırmak için: bu makinede `--max-candidates` yüksek tutup `stopAtFirstSuccess`
kapalı koşumlar yapılabilir (şu an CLI'da bunun bayrağı yok, eklenebilir).

### 2. Superonline saha testi — dış bağımlılık

Paket hazır: `dist/zapret-tr-saha-testi.zip`. Test kullanıcısında.
Rapor gelince `learned.json`'a aktarılıp `profiles/isp/superonline.json`
`verified`e çekilecek. Superonline'ın 19 adayının hiçbiri doğrulanmadı.

**Turkcell Mobil hotspot bunun yerine GEÇMEDİ — denendi ve ölçüldü.** Aynı şirket
ama ayrı ağ ve ayrı ASN: Superonline AS34984 (sabit), Turkcell Mobil AS16135.
Hotspot koşumu `turkcell-mobil` profilini doğruladı, `superonline` hâlâ 19 adayla
ve sıfır saha verisiyle duruyor. İki hattın DPI davranışı ölçümle farklı çıktı
(yukarıdaki Turkcell Mobil bölümüne bak), yani sabit hat profilini mobilden
türetmek de mümkün değil.

Superonline için hâlâ gerçek bir AS34984 hattı gerekiyor.

### 3. Paket boyutu (düşük öncelik)

Saha paketi 34 MB, paylaşım limitlerinin üstünde. `PublishTrimmed` **denendi ve
geri alındı**: JSON yansımayla çalıştığı için kırpma, DNS yedeğinin geri
yüklenmesi gibi güvenlik kritik yolları sessizce bozabiliyor. Güvenli hale
getirmek profil yükleyicisi, hedef listesi, DoH yanıtları ve rapor DTO'su dahil
tüm JSON yollarını kaynak üretimine taşımayı gerektirir. DNS ve yapılandırma
yolları zaten taşındı (`CoreJsonContext`).

---

## Tuzaklar — hepsi bir kez ısırdı

**HTTP/3 IP'ye SABİTLENEMİYOR ve bu QUIC ölçümünü tümüyle geçersiz kılmıştı.**
`SocketsHttpHandler.ConnectCallback` yalnızca TCP bağlantılarında çağrılıyor;
HTTP/3 adresi kendisi, **sistem DNS'i** ile çözüyor. Sonuç: `--doh` ile gerçek IP
bulunup winws'e `--ipset-ip` olarak veriliyordu ama QUIC bağlantısı kaçırılmış
sistem DNS'inin verdiği engel sunucusuna (`195.175.254.2`) gidiyordu. winws'in
ipset kontrolü her pakette `negative` dönüyor, strateji hiç uygulanmıyor, paket
`reinject unmodified` geçiyordu. Dışarıdan bu "strateji işe yaramadı" ile birebir
aynı görüntü — **15 QUIC adayının tamamı birbirinin aynı işlemsiz koşumdu.**
Çözüm: `QuicProbeClient`, IP ile SNI'yi ayrı verebilen ham `QuicConnection`.
HttpClient QUIC ölçümü için bir daha kullanılmamalı.

**QUIC istemcisi akış sınırlarını ilan etmezse sunucu bağlantıyı ANINDA kapatır —
ve bu DPI engeliyle karıştırılır.** HTTP/3 sunucusu el sıkışmasından hemen sonra
üç tek yönlü akış açmak zorunda (kontrol, QPACK encoder, QPACK decoder).
`QuicClientConnectionOptions.MaxInboundUnidirectionalStreams` varsayılan olarak 0
geliyor; o zaman sunucu akışlarını açamıyor ve bağlantıyı taşıma hatasıyla
kapatıyor. Belirti: **~35 ms'de `TransportError`**.

Bu, `www.youtube.com` QUIC'inin **engelli sanılmasına** yol açtı — bir süre
"çözülmemiş iş" olarak bu dosyada durdu. Google uçları kuralı sıkı uyguluyor,
Cloudflare uygulamıyordu; dolayısıyla iki Cloudflare hedefi (kontrol ve discord)
çalışıp tek Google hedefi hep başarısız olunca tablo tutarlı görünüyordu.

**Ayırt etme kuralı: gerçek DPI engeli bu hatta ~10 sn ZAMAN AŞIMI olarak
görünüyor, anlık protokol hatası olarak değil.** Milisaniyelerde dönen bir QUIC
hatası neredeyse her zaman bizim tarafımızdaki bir kusurdur. Şüphelenince
engelli OLMADIĞI bilinen bir adresle (`www.google.com`) karşılaştır:

```
zapret-tr-test.exe --diagnose www.google.com --doh
```

**winws, filtresi birebir aynı olan ikinci bir örneği reddediyor.**
`A copy of winws is already running with the same filter`. `--ipset-ip` GLOBAL
WinDivert filtresine girmiyor — yalnızca süreç içindeki profil eşleşmesinde
kullanılıyor. Dolayısıyla paralel hedef sınamasında her iki işçi de aynı filtreyi
kuruyordu ve ikincisi anında 1 koduyla ölüyordu. Belirti sessizdi: aday
hedeflerin yalnızca birinde ölçülüyor, diğerinde "çalıştırılamadı" yazıyordu.
**Aynı strateji bir koşumda başarısız, bir koşumda başarılı görünüyordu** —
ölçülen şey aslında hangi işçinin önce başladığıydı. Çözüm: hedef başına ayrı
örnek yerine, bölümün bütün hedeflerini kapsayan TEK örnek
(`--ipset-ip=<ip_list>` virgüllü liste alıyor). Aynı aday zaten bütün hedeflere
aynı stratejiyi uyguluyor, dolayısıyla anlam değişmiyor.

**`winws.exe --help` bile yönetici yetkisi istiyor.** Seçenek listesini öğrenmek
için UAC harcamaya gerek yok: ikiliden ASCII dizgi çıkarmak yeterli ve daha
eksiksiz sonuç veriyor (yardımda görünmeyen karar satırları da çıkıyor).
Bu oturumdaki bütün teşhis oradan geldi.

**`--dpi-desync-fake-quic-mod` diye bir şey YOK (v72.13).** `--dpi-desync-fake-tls-mod`
SNI'yi runtime'da üretebiliyor ama QUIC'te karşılığı yok; tek eksen hazır yük
dosyasının kendisi. Bu yüzden `files/fake/` altındaki `quic_initial_*`
varyantları indiriliyor. Aramadan önce ikilide `strings` ile doğrula.

**`fetch-upstream.ps1` artık artımlı.** Eskiden "dosya sayısı yeterliyse hepsini
atla, değilse hepsini indir" idi; listeye yeni bir dosya eklemek bütün listeyi
yeniden indirmeyi zorunlu kılıyordu ve `WinDivert64.sys` bir test koşumundan
sonra hâlâ çekirdeğe yüklüyken (servis silinse bile sürücü görüntüsü
kaldırılana kadar kilitli kalır) "dosya başka bir süreç tarafından kullanılıyor"
ile patlıyordu. Artık her dosya manifest özetiyle karşılaştırılıyor. Bu duruma
düşersen çözüm yeniden başlatmak değil, `--cleanup`.

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
uygulanamaz; ikisi de aynı paketleri görür ve sonuç belirsizleşir. Ama paralel
olan yalnızca AĞ İSTEKLERİ: winws tek örnek olarak, bütün hedefleri kapsayan tek
ipset ile çalışıyor (sebebi tuzaklar bölümünde).

**"Başarı" QUIC'te el sıkışmasının tamamlanması**, HTTP isteğinin dönmesi değil.
DPI müdahalesi Initial paketinde oluyor; el sıkışması tamamlanıyorsa DPI aşılmış
demektir. Ölçümü dar tutmak yanlış sinyali azaltıyor — HTTP/3 isteği ayrıca
sunucu tarafı sebeplerle de başarısız olabilir ve bu DPI'a yazılırdı.

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

# winws paketleri görüyor mu VE gördüğünü değiştiriyor mu (ikisi ayrı soru).
# --section olmadan tcp80 varsayılır; QUIC teşhisi için şart.
# --out verilirse tam winws günlüğü oraya yazılır (ekran çıktısı kırpılıyor).
zapret-tr-test.exe --engage-check discord.com --section quic --doh --out quic.log
zapret-tr-test.exe --engage-check discord.com --section quic --doh --strategy "<args>"
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

Temiz: `--cleanup` koşuldu — WinDivert sürücüsü durduruldu ve kaldırıldı, servis
yok, DNS değiştirilmemiş, winws/dnscrypt süreci yok, internet normal.
`%ProgramData%\ZapretTR\config.json` duruyor (kullanıcı ayarı, kaldırma bunu
silmiyor).

Test hatları: Türk Telekom / AS9121 (sabit) **ve** Turkcell Mobil / AS16135
(telefon hotspot ile tethering). İkisi de ölçüldü. VPN yok. Superonline (AS34984)
erişimi yok.

**Son oturum sonunda makine Turkcell Mobil hotspot'una bağlıydı.** Sonraki
oturumda TTNET ölçümü yapılacaksa önce hattı doğrula:
`curl -s "http://ip-api.com/json/?fields=as,isp"` — AS9121 beklenir. Yanlış hatta
koşup sonucu yanlış profile yazmak, bu projedeki en pahalı sessiz hata sınıfı.

Testler: 116 geçiyor. Merdiven toplamı 221 aday (QUIC 15 → 56).
