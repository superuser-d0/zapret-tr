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

**Uygulama düzeyinde de doğrulandı:** WPF arayüzü ile şifreli DNS + bu strateji
açıkken Discord'un kendi istemcisi çalıştı. Ölçüm ile gerçek kullanım örtüştü —
projede ilk kez bir profil bu düzeyde teyit edildi. `--apply --doh` aynı anda
`3 hedef düzeldi, 0 hedef bozuldu` verdi; "0 bozuldu" ayrıca önemli, YouTube ve
kontrol hedefleri etkilenmedi.

**Ölçülmüş sonuç — TTNET kapsamlı tarama (2026-09-07, AS9121, yeni makine):**

Aynı komut (`--isp turk-telekom --doh --exhaustive --max-candidates 20`) üç kez
bağımsız koşuldu. Her koşum 60 deneme / ~5.5 dakika. Sonuç şaşırtıcı derecede
kararlı: **32 ortak adayın 14'ü üçünde de çalıştı**, biri (`vf-443-fake-multisplit-badseq`)
yalnızca 1/3 — tekrarın gerekçesi tam olarak o tek aday.

3/3 geçenler `profiles/isp/turk-telekom.json`'a `verified` olarak işlendi:
tcp443'te 8, quic'te 7 doğrulanmış aday. Yabancı profillerden gelenler (md5sig
ailesi Superonline'dan, ttl1+autottl3 Turkcell Mobil'den) `tt-` kimliğiyle
kopyalandı — **kaynak profillerinde durumları değişmedi**, çünkü orada değil
burada doğrulandılar.

**`--exhaustive` ilk koşumunda hemen bir şey buldu: `tt-quic-fake-plain`.**
`--dpi-desync=fake --dpi-desync-repeats=11` — sahte yük yok, `any-protocol` yok —
TTNET'te Discord QUIC'ini açıyor, 3/3. Bu aday profilde en baştan beri duruyordu
ama **TTNET'te bir kez bile denenmemişti**: arama ilk başarıda duruyor ve
profilin ilk QUIC adayı (`tt-quic-anyproto-cutoff`) hep hemen tutuyordu. Yani
bulunamamış olmasının sebebi hattın davranışı değil, arama stratejisiydi.

Bu, `tt-quic-anyproto-cutoff` notundaki "any-protocol'süz aynı aday 2/2 zaman
aşımı" ölçümüyle ÇELİŞMİYOR — o ölçüm google yüklü + cutoff'lu adaydan yalnızca
`any-protocol`'ü çıkarmıştı. İkisi farklı adaylar.

**Yük eksenine dair aynı hatta üçüncü kez kanıt:** `tt-quic-fake-google`
(google yükü, `any-protocol` YOK) bu hatta **3/3 zaman aşımı** verdi. Aynı yük
`any-protocol` ile birlikte verildiğinde çalışıyor. Yani yük tek başına zarar
veriyor, `any-protocol` ile birlikte fayda sağlıyor. Ağırlığı doğrulanmışların
altına (60) çekildi. Mobil hattaki "yüklü aday 0/2, yüksüz 3/3" gözlemiyle aynı
yöne işaret ediyor.

Ayrıca `any-protocol` ailesinde **cutoff değeri belirleyici değil**: n2, n3 ve d2
üçü de, yüklü ve yüksüz halleriyle, 3/3 geçti. Belirleyici olan `any-protocol`'ün
kendisi.

**Mobil, sabit hattan gerçekten farklı — türetilemez.** İki somut fark:

1. Discord TCP: TTNET'te **RST**, Turkcell Mobil'de **zaman aşımı**.
2. QUIC: TTNET'te `any-protocol`+`cutoff` şart; mobilde düz
   `fake --repeats=11` yetiyor. Dahası mobilde **sahte yük eklemek zarar
   veriyor** — yüklü aday 0/2, yüksüz aday 3/3. `tcm-quic-fake-google`'ın
   ağırlığı bu yüzden yüksüzün altına çekildi.

Yani sahte yük seçimi hatta göre değişen bir eksen; "google yükü hep iyidir"
varsayımı yanlış.

**Turkcell Mobil'de DNS kaçırma VAR — şifreli DNS bu hatta isteğe bağlı değil.**
`--apply --isp turkcell-mobil` (DoH'suz) koşulduğunda discord.com **engel sayfası**
(`erisime_engellenmis`) döndürüyor ve winws hiçbir şey değiştiremiyor:
`0 hedef düzeldi`. Aynı komut `--doh` ile: `3 hedef düzeldi, 0 bozuldu` —
Discord'un metin, bağlantı ve QUIC bölümlerinin üçü de açılıyor.

İki katmanlı engellemenin en temiz kanıtı bu: üstteki DNS katmanı durdukça
alttaki DPI stratejisi ne kadar doğru olursa olsun sonuç değişmiyor.
`tcp443` bölümünde bu fark belirti olarak da görünüyor — DoH varken
**zaman aşımı** (gerçek DPI), DoH yokken **engel sayfası** (DNS kaçırma).

---

## Yapılacaklar

### 1. Doğrulama kapsamı — 17 aday doğrulanmış, 8 profil hâlâ boş

Kod eksiği değil saha verisi eksiği. Gerçek hatta doğrulanan adaylar:

| Profil | Bölüm | Doğrulanmış aday |
|---|---|---|
| turk-telekom (AS9121) | tcp443 | 8 (`tt-443-fake-ttl4` başta) |
| turk-telekom (AS9121) | quic | 7 (`tt-quic-anyproto-cutoff` başta) |
| turkcell-mobil (AS16135) | tcp443 | 1 (`tcm-443-fake-autottl`) |
| turkcell-mobil (AS16135) | quic | 1 (`tcm-quic-fake-plain`) |

Kalan 8 profilde sıfır. Hiçbir profilde `tcp80` ve `discord-voice` doğrulanmadı —
`profiles/probe-targets.json` içinde o bölümlerde engelli bir hedef yok (tcp80'de
yalnızca kontrol hedefi var), dolayısıyla bu hatlardan ölçülemezler. Yeni bir
tcp80 hedefi eklenmeden bu bölüm hiçbir hatta doğrulanamaz.

Kullanıcılar test çalıştırdıkça `%ProgramData%\ZapretTR\learned.json` doluyor ve
profillerin üzerine bindiriliyor. **CLI bunu 2026-09-07'ye kadar hiç yazmıyordu**
— yalnızca WPF yazıyordu, yani `zapret-tr-test.exe` ile bulunan her doğrulama
rapor dosyasında kalıp arayüze/servise hiç geçmiyordu. Artık `--save-learned` var
(bayrakla, çünkü bu araç başkasının makinesinde de koşuyor ve oradaki söz
"sonuçlar yalnızca rapor dosyasına yazılır").

Not: paralel sınama hatası düzeltilene kadar QUIC sonuçları kararsızdı (tuzaklar
bölümüne bak). Bu düzeltmeden ÖNCE toplanmış `learned.json` verisi varsa QUIC
bölümü için güvenilmez.

Kapsamı genişletmenin yolu artık bir bayrak: `--exhaustive` ilk başarıda durmaz,
bütçe bitene kadar dener. Doğrulama verisi toplamak için olan tek şey bu;
normal kullanımda gereksiz ve yavaş.

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

### 3. ~~Paket boyutu~~ — ÇÖZÜLDÜ (34.3 → 12.5 MB)

`PublishTrimmed` artık açık ve **güvenli**. Bütün JSON yolları kaynak üretimine
taşındı: `CoreJsonContext` (yapılandırma, DNS yedeği, profil, merdiven),
`ProberJsonContext` (hedef listesi, DoH yanıtı, ASN tespiti),
`ReportJsonContext` (rapor).

Güvenliği "dikkatli olduk"a değil **derleyiciye** dayanıyor:

```
<TrimmerSingleWarn>false</TrimmerSingleWarn>
<EnableTrimAnalyzer>true</EnableTrimAnalyzer>
<WarningsAsErrors>...;IL2026;IL2104;IL3050</WarningsAsErrors>
```

Yeni bir yansıma tabanlı JSON yolu eklenirse yayın **uyarı değil hata** verir.
Bu üç satırı kaldırma.

Not: eski notta "profil yükleyicisi, hedef listesi, DoH yanıtları ve rapor DTO'su"
sayılıyordu ama **`IspDetector`'ın iki ASN sorgusu listede yoktu**. Çözümleyici
onları buldu; elle sayım eksik kalmıştı.

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

**`PublishSingleFile`, QUIC'i sessizce öldürüyor.** msquic.dll tek dosya paketine
gömüldüğünde çalışma anında bulunamıyor, `QuicConnection.IsSupported` false
dönüyor ve belirti `QUIC bu makinede desteklenmiyor (msquic yok)` oluyor.
Ölçülen dört yayın biçimi:

| Biçim | Boyut | QUIC |
|---|---|---|
| tek dosya, kırpılmamış | 33.6 MB, 1 dosya | **bozuk** |
| tek dosya, kırpılmış | 11.3 MB, 1 dosya | **bozuk** |
| çok dosya, kırpılmış | 21.0 MB, 58 dosya | çalışıyor |
| tek dosya + yanda msquic | 11.8 MB, 2 dosya | çalışıyor ← seçilen |

**Kırpmayla ilgisi yok** — kırpılmamış tek dosya da bozuk. Ve bu, kırpma
çalışmasından **önce de vardı**: dağıtılan 33.6 MB'lik saha paketi QUIC bölümünü
hiç ölçemiyordu. Fark edilmemesinin sebebi kontrol hedefi koruması: QUIC kontrolü
de açılmadığı için bölüm "güvenilmez" sayılıp sessizce atlanıyordu. Koruma doğru
çalıştı ama kök nedeni gizledi.

Çözüm iki yerde birden: CLI projesindeki `MsQuicTekDosyaDisindaKalsin` hedefi
msquic'i yayın çıktısına kopyalıyor (bulamazsa `<Error>` ile patlıyor), ve
`build-field-package.ps1` onu pakete alıyor (yoksa `throw`). İkisi de bilerek
gürültülü — bu dosyanın sessizce kaybolması tam olarak bir kez oldu.

**Bölüme göre ölçüm seçimi DÖRT ayrı yerde tekrarlanıyordu ve hepsi tek tek
ısırdı.** `quic` ham `QuicConnection`, `discord-voice` STUN, gerisi
`HttpProbeClient` ister. Bu dallanma baseline'da, aday denemesinde, doğrulama
tekrarında, `--engage-check`'te, `--diagnose`'da ve `--apply`'da ayrı ayrı
yazılmıştı; her biri sırayla yanlış çıktı. Belirtisi hep aynı ve sessiz:
ölçüm çalışıyor gibi görünüyor ama başka bir şey ölçüyor — örneğin `--apply`
QUIC hedefleri için `HTTP 200` yazıyordu, oysa QUIC ölçümü hiçbir zaman
"HTTP 200" dönmez.

Artık tek yer var: `StrategyProber.ProbeAsync`. **Yeni bir ölçüm yolu
eklerken kendi dallanmanı yazma, bunu çağır.** Çıktıda bir bölüm kendi
protokolüne ait olmayan bir detay yazıyorsa (QUIC'te "HTTP ...", ses
bölümünde "HTTP ...") o yol bu fonksiyonu atlıyordur.

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

**Aday tekilleştirmesi tier'ların İÇİNDE vardı, ARASINDA yoktu.** Profiller
birbirinden türediği ve genel merdiven de aynı kombinasyonları ürettiği için aynı
komut farklı adla ikinci kez deneniyordu. Ölçüldü: TTNET kapsamlı taramasında
40 denemenin 8'i (%20) birebir aynı argümanın tekrarıydı — örneğin
`tt-quic-fake-plain` ile `superonline/sol-quic-fake-plain` aynı komut. Bütçe
sınırlı olduğu için bedeli doğrudan: denenmeyen 8 gerçek aday. Artık
`SearchSectionAsync` bölüm boyunca görülen argümanları tutuyor.

**Merdiven adaylarının kimliği aile adıydı, yani kimlik değildi.** Bir aile
eksenlerin kartezyen çarpımı kadar aday üretiyor; hepsi `ladder/fake-quic-anyproto`
adını taşıyordu. İlerleme satırlarında aynı ad peş peşe tekrarlıyordu, ama asıl
zarar `--save-learned` ile görüldü: doğrulanan altı farklı varyant `learned.json`'a
aynı adla yazıldı ve hangisinin çalıştığı okunamaz hale geldi. Artık aile içinde
sıra numarası var (`ladder/fake-quic-anyproto#3`). Asıl kimlik yine argümanlar —
`ConfigStore.AddLearned` de onu anahtar alıyor — ad yalnızca okunabilirlik için.

**`winws.exe --help` bile yönetici yetkisi istiyor.** Seçenek listesini öğrenmek
için UAC harcamaya gerek yok: ikiliden ASCII dizgi çıkarmak yeterli ve daha
eksiksiz sonuç veriyor (yardımda görünmeyen karar satırları da çıkıyor).
Bu oturumdaki bütün teşhis oradan geldi.

**`--dpi-desync-fake-quic-mod` diye bir şey YOK (v72.13).** `--dpi-desync-fake-tls-mod`
SNI'yi runtime'da üretebiliyor ama QUIC'te karşılığı yok; tek eksen hazır yük
dosyasının kendisi. Bu yüzden `files/fake/` altındaki `quic_initial_*`
varyantları indiriliyor. Aramadan önce ikilide `strings` ile doğrula.

**`fetch-upstream.ps1`'in SHA256 doğrulaması bir süre HİÇ koşmadı.** Artımlı
indirmeye geçiş `$skipWinws` değişkenini kaldırmış ama onu kullanan
`if ($skipWinws) { ... return }` dalı dosyada kalmıştı. `Set-StrictMode -Version
Latest` altında tanımsız değişkene erişmek hata; dolayısıyla betik **her koşumda**
tam o noktada, yani dosyalar indikten sonra ama doğrulama döngüsünden önce
patlıyordu. Belirtisi yanıltıcı: bütün dosyalar iniyor, `vendor/` dolu ve çalışır
görünüyor, sadece en sondaki kırmızı satır okunmazsa hiçbir şey ters görünmüyor.
Kaybedilen şey projenin tedarik zinciri güvencesinin tamamıydı.

Temiz bir makinede yeniden kurulurken yakalandı; düzeltmeden sonra 16 dosyanın
16'sı manifest özetiyle eşleşti, yani inen ikililer doğruydu — ama bu ancak
doğrulama koştuktan sonra bilinebilirdi.

Ders: `return` ile biten bir "atla" dalı, atlanan şey bir güvenlik kontrolüyse
sessizce her şeyi atlayabilir. Bu betikte artık öyle bir dal yok.

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
zapret-tr-test.exe --detect-isp               # hangi hattayım + eşleşen profiller
zapret-tr-test.exe --diagnose discord.com     # tek adres, dört protokol

# Doğrulama verisi toplamak için: ilk başarıda durma, çalışan HER adayı bul ve
# bulunanları learned.json'a yaz (--save-learned, --isp gerektirir).
# Üç kez koşup kesişimini almak tek koşumun gürültüsünü ayıklıyor: 2026-09-07'de
# 32 ortak adayın 14'ü 3/3, biri 1/3 çıktı.
zapret-tr-test.exe --isp turk-telekom --doh --exhaustive --max-candidates 20 \
                   --save-learned --out rapor.json -y

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

**DİKKAT: bu oturum YENİ bir makinede koşuldu.** Önceki oturumların makinesi
değil. Belirtileri: .NET SDK yoktu (yalnızca 8.0.30 runtime),
`%ProgramData%\ZapretTR` yoktu, `vendor/` boştu. Aşağıdakiler bu yeni makine için
geçerli.

Temiz ve **bağımsız doğrulandı** (`--cleanup` çıktısına güvenilmedi, ayrıca
kontrol edildi): winws/dnscrypt/ZapretTR süreci yok, WinDivert ve ZapretTR
servisi yok, DNS yönlendiriciye dönmüş (`192.168.8.1`, dnscrypt'in `127.0.0.1`'i
değil), `example.com` çözülüyor.

`%ProgramData%\ZapretTR\learned.json` duruyor: 16 kayıt (tcp443 9, quic 7), bu
oturumun `--save-learned` koşumlarından. `config.json` yok — WPF hiç açılmadı.

**Ortam kurulumu (yeniden gerekirse):** .NET SDK 8.0.424 `dotnet-install.ps1` ile
kullanıcıya özel kuruldu, PATH'e EKLENMEDİ. Tam yol:
`%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe`. `C:\Program Files\dotnet\dotnet.exe`
hâlâ SDK'sız, onunla `dotnet build` çalışmaz.

Test hatları: Türk Telekom / AS9121 (sabit). VPN yok. Bu makinede Turkcell Mobil
hotspot ve Superonline (AS34984) erişimi YOK — turkcell-mobil profilinin verisi
önceki makineden geliyor, bu oturumda tekrar ölçülmedi.

Ölçüm yapmadan önce hattı doğrula:

```
zapret-tr-test.exe --detect-isp
```

Yanlış hatta koşup sonucu yanlış profile yazmak, bu projedeki en pahalı sessiz
hata sınıfı.

**Kapanan konu — `--dns test`: bu makinede YENİDEN ÜRETİLEMEDİ.** Önceki makinede
dnscrypt açılıyor, çözümleyicilere bağlanıyor ama doğrulama sorgusu cevapsız
kalıyordu. Burada hem sıcak hem **soğuk** başlangıçta (çözümleyici listesi
önbelleği `public-resolvers.md` silinerek) saniyeler içinde geçti; üç hedef de
gerçek adrese çözüldü ve DNS temiz geri alındı. Yani soğuk başlangıç
hipotezi — "15 sn'lik doğrulama penceresi liste indirmeye yetmiyor" — bu hatta
**doğrulanmadı**. Sorun o makineye özgü (yavaş bağlantı, 53/udp'yi tutan başka
bir servis, ya da loopback UDP'yi engelleyen bir güvenlik duvarı olabilir).

Üretilemeyen bir hata düzeltilemediği için bunun yerine **kendini anlatır** hale
getirildi: doğrulama başarısız olduğunda `DnsCryptRunner` artık ne kadar
beklendiğini, sürecin yaşayıp yaşamadığını (yaşamıyorsa çıkış kodunu) ve
`127.0.0.1:53`'ü dinleyen biri olup olmadığını mesaja koyuyor. Bu üçlü, üç ayrı
durumu ayırıyor: süreç ölmüş / süreç yaşıyor ama portu bağlayamamış / port bağlı
ama sorgu cevapsız. O makinede tekrar görülürse mesaj hangisi olduğunu söyler.

Testler: 116 geçiyor. Merdiven toplamı 221 aday (QUIC 15 → 56).
Kırpılmış Release yayını doğrulandı: 11.3 MB tek dosya + yanında `msquic.dll`,
kırpma analizöründen tek uyarı yok.
