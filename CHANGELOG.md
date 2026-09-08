# Değişiklik günlüğü

Biçim [Keep a Changelog](https://keepachangelog.com/tr/1.1.0/) temelli,
sürümleme [Semantic Versioning](https://semver.org/lang/tr/).

Bu dosyada "doğrulandı" kelimesi dar bir anlam taşır: **gerçek bir hatta, ölçümle**.
Toplulukta bildirilmiş ya da mekanizmadan türetilmiş şeyler doğrulanmış sayılmaz.

## [Yayınlanmamış]

## [0.1.10]

### Değişti

- **Strateji listesinde kendi ölçümünüz ayırt ediliyor.** Parametre testinizden çıkan
  adaylar `✓ … · sizin testiniz`, başka bir hatta doğrulanmış olanlar `✓ … · doğrulanmış`
  etiketiyle görünüyor. İkisi de "doğrulandı" ama aynı şey değil: biri sizin hattınızda
  ölçüldü, diğeri başkasınınkinde. Ağ değiştirip listeye dönen kullanıcının görmesi
  gereken ilk şey hangisinin kendi ölçümü olduğu.
- **README'ye bağlantı değiştirme bölümü eklendi.** Aynı modeme bağlı WiFi ile Ethernet
  arasında geçiş yeni test gerektirmiyor (ölçüldü: aynı strateji iki bağlantıda da
  çalıştı, iki arayüzün ağ geçidi aynı IP ve aynı MAC adresi). Yeni test gerektiren şey
  bağlantı türü değil ağın kendisi: mobil paylaşım, başka bir ev, iş yeri.

## [0.1.9]

### Düzeltildi

- **Şifreli DNS yalnızca o an bağlı olan arayüze uygulanıyordu.** Kablo takılıyken
  kurulum yapan bir kullanıcıda WiFi `Disconnected` olduğu için atlanıyordu; sonra
  kabloyu çıkarıp WiFi'ye geçince o arayüzün DNS'i İSS'in sunucusunda kalıyor ve
  **DNS engellemesi geri geliyordu**. Belirtisi de yok: `winws` çalışmaya devam
  ettiği için arayüz "KORUMA AKTİF" gösteriyor, devrede olmayan şey şifreli DNS.
  Gerçek bir makinede ölçüldü — `Ethernet → 127.0.0.1`, `WiFi → 192.168.8.10`.
  Artık şu an bağlı olmayan Ethernet/WiFi kartlarına da yazılıyor: ayar kalıcı,
  arayüz bağlanınca geçerli oluyor.
- **Yedek, sonradan eklenen arayüzü kapsamıyordu.** Yönlendirme genişleyince eski
  yedekte olmayan bir karta yazılabiliyordu; geri alma onu atlar, DNS'i
  `127.0.0.1`'de kalır ve kaldırmadan sonra o bağlantıda **hiçbir ad çözülmezdi**.
  Yedek artık yeni arayüzleri kapsayacak şekilde genişletiliyor; mevcut kayıtların
  üzerine yazılmıyor, çünkü bizim koyduğumuz `127.0.0.1` "orijinal" diye
  kaydedilirse geri dönüş yolu tamamen kaybolur.
- **Saha testi paketi tek bir servis sağlayıcıya sabitlenmişti.** Paket her yayında
  herkese açık duruyordu ama içinde `--isp superonline` yazıyordu: başka bir hattan
  indiren kişi **yanlış profille** test ediyor, aday bütçesi alakasız parametrelere
  harcanıyor ve rapor `superonline-rapor.json` adıyla çıkıyordu. Paket artık
  `--isp auto` kullanıyor — hat tespit ediliyor, tanınmazsa genel aramaya düşüyor.
  (CLI'ye `--isp auto` desteği bunun için eklendi.)
- **Saha paketinin ne olduğu yanlış anlaşılıyordu.** Yayın sayfasında kurulum
  paketinin hafif bir alternatifi gibi duruyordu; değil — internet açmaz, koruma
  sağlamaz, yalnızca ölçüm yapıp rapor yazar. Hem yayın notunda hem paketin içindeki
  açıklamada bu artık en başta söyleniyor.

**Not:** Kurulumdan sonra takılan yeni bir adaptör (ör. USB WiFi) hâlâ kapsam
dışında. Onun için uygulamayı açıp servisi bir kez yeniden kurmak gerekiyor.

## [0.1.8]

### Düzeltildi

- **Yükseltme, otomatik başlatma servisini sessizce siliyordu.** Kurulum, dosyaları
  değiştirebilmek için servisleri sökmek zorunda: çalışan `winws`, WinDivert sürücüsünü
  ve dolayısıyla `WinDivert64.sys` dosyasını kilitliyor. Ama söktüğünü geri kurmuyordu.
  Sonuç, 0.1.7'ye yükselten gerçek bir makinede görüldü: kurulum sorunsuz bitiyor,
  uygulama "SİSTEM HAZIR" diyor, servisler yok ve kullanıcı **korumasız** kalıyor.
  Belirtisi de yok — uygulama doğru davranıyor, kaybolan şey kullanıcının bir daha
  basmadığı bir düğmenin sonucu. Artık kurulum, yükseltmeden önce servisin kurulu olup
  olmadığına bakıyor ve dosyalar yerine geçtikten sonra geri kuruyor; geri kurma
  başarısız olursa sessiz geçmiyor, kurulum bunu söylüyor.

**0.1.7'den yükselttiyseniz:** servisiniz silinmiş olabilir. `sc.exe query ZapretTR`
komutu `RUNNING` demiyorsa uygulamayı açıp "Servis Olarak Yükle" düğmesine basın.

## [0.1.7]

### Düzeltildi

- **Otomatik başlatma servisi açıkken "Başlat" hata veriyordu.** Servis kuruluysa
  `winws` zaten çalışıyor; ikinci bir kopya aynı filtreyle açılamadığı için 1 koduyla
  kapanıyor ve arayüz "BAŞLATILAMADI" gösteriyordu. Kullanıcı, koruma **çalışırken**
  bozuk sandığı bir uygulamaya bakıyordu. Artık servis kuruluyken Başlat düğmesi
  kapalı ve durum "SERVİS MODU AKTİF" diyor — elle başlatmaya gerek yok.
- **`winws` 1 koduyla kapandığında sebep söylenmiyordu.** Günlüğe, en olası neden
  (zaten çalışan bir kopya) ipucu olarak yazılıyor.

### Eklendi

- **Başlatmadan sonra kayıtlı strateji doğrulanıyor.** Bir kez test yapıp stratejiyi
  kaydeden kullanıcı, sonraki açılışlarda doğrudan Başlat'a basıyor ve test bir daha
  koşmuyor. Ama engelleme değişebilir: İSS'in DPI yapılandırması güncellendiğinde dün
  çalışan parametre bugün çalışmaz ve arayüz yine "ÇALIŞIYOR" gösterirdi. Artık
  başlatmadan birkaç saniye sonra hedefler ölçülüyor; **hiçbiri açılmıyorsa** durum
  "ÇALIŞIYOR — AMA AÇMIYOR" olur ve yeni bir parametre testi önerilir.

## [0.1.6]

Bu sürümün tamamı, [Zapret Win TR](https://github.com/superuser-d0/zapret-tr/blob/main/THIRD-PARTY-NOTICES.md)
geliştiricisi Ali Mali'nin paylaştığı kaynaktan öğrenilenlere dayanıyor.

### Düzeltildi

- **Sürücü temizliği tek servis adına bakıyordu.** WinDivert yalnızca `windivert`
  adıyla değil, `WinDivert14` (WinDivert 1.4 ve **GoodbyeDPI**'ın kullandığı ad) ve
  bazı dağıtımlarda `monkey` adıyla da kurulu kalabiliyor. Kalıntı bir servis
  sürücüyü çekirdekte tutuyorsa winws kendi sürücüsünü yükleyemiyor ve **bütün
  adaylar aynı şekilde düşüyor** — dışarıdan "hiçbir strateji çalışmadı" gibi
  görünüyor. Bir kullanıcıda tam bu tablo vardı: 176 aday, 1105 saniye, sonuç yok.
  Hem `--cleanup`/`--uninstall-services` hem de kurulum artık üç adı da söküyor.

### Eklendi

- **Çakışan araç tespiti.** WinDivert'i aynı anda iki araç kullanamıyor; GoodbyeDPI
  açıkken ölçüm hiç yapılamıyor ama kullanıcı bunu göremiyordu. Parametre testi
  başlamadan önce çalışan bilinen araçlar (goodbyedpi, ciadpi, spoofdpi, winws2)
  tespit edilip uyarı veriliyor. **Süreç öldürülmüyor** — başka bir aracı kapatmak
  kullanıcının kararı; amaç 15 dakikayı körlemesine harcamasını engellemek.

### Not — bağımsız doğrulama

Ali Mali'nin aracındaki klasik winws stratejileri bizim profillerimizle
karşılaştırıldı: **sekizinin sekizi de zaten bizde vardı** ve altısında onun tercihi
bizim ilk adayımızdı. İçe aktarılacak yeni strateji çıkmadı; ama bu, o profillerin
tohumlarının bağımsız bir araçla örtüştüğünü gösteriyor. Etiketler `verified`
**yapılmadı** — dayanak "olumsuz dönüş olmadı" ve sessizlik ölçüm değildir.

## [0.1.5]

### Düzeltildi

- **Test "başarılı" derken Discord güncellemede takılı kalabiliyordu.** Gerçek bir
  kullanıcıda görüldü: parametre testi çalışan strateji buluyor, Zapret
  başlatılıyor, ama Discord istemcisi güncelleme ekranından geçemiyor.

  Sebep: Discord istemcisi güncelleme için **ayrı bir sunucuya** gidiyor
  (`updates.discord.com`) ve o adres de ayrıca engelli — ama hedef listemizde yoktu.
  Test yalnızca `discord.com` ve `gateway.discord.gg`'ye bakıyordu; ikisini açan bir
  strateji "başarılı" ilan ediliyor ve kullanıcının eline çalışmayan bir Discord
  geçiyordu. Ölçüldü (TTNET): koruma kapalıyken `updates.discord.com` RST veriyor,
  açıkken HTTP 200.

  `updates.discord.com` hedef listesine **kendi kategorisiyle** eklendi
  (`discord-guncelleme`). Aynı kategoriye konsaydı `discord.com`'un açılması bu
  adresi de açılmış gösterirdi ve eksik olan şey raporda görünmezdi. Hedefler
  paralel sınandığı için (aynı anda 3) süreye pratikte bir şey katmıyor.

- **Kısmi başarı "başarılı" gibi gösteriliyordu.** Bir bölümde birden fazla hedef
  sınıfı olabiliyor ve kazanan aday hepsini açmak zorunda değil — arama ilk
  başarıda duruyor, "başarı" ise en az bir sınıfın açılması. Ekranda yalnızca
  "STRATEJİ BULUNDU" yazıyordu. Artık açılmayan hedefler ismen listeleniyor ve
  durum **"KISMEN ÇALIŞIYOR"** olarak gösteriliyor.

## [0.1.4]

### Düzeltildi

- **Yükseltme, kilitli `WinDivert64.sys` yüzünden başarısız oluyordu.** Gerçek bir
  kullanıcıda görüldü: bir test koşumundan sonra WinDivert sürücüsü çekirdekte
  asılı kalıyor, dosya kilitleniyor ve kurulum **"DeleteFile tamamlanamadı; kod 5.
  Erişim engellendi."** veriyordu. Kullanıcıya kalan tek seçenek "bu dosya
  atlansın" oluyordu. Aynı kilit kaldırmadan sonra da klasörde kalıntı bırakıyor
  ve bir sonraki kurulum aynı duvara toslıyordu.

  Kök sebep: `ServiceManager.UninstallAsync` yalnızca ZapretTR servislerini
  söküyordu; `windivert` sürücüsüne hiç dokunmuyordu. Sürücüyü kaldıran kod
  (`WinDivertCleanup`) yalnızca CLI'nin `--cleanup` komutundan erişilebiliyordu.

  İki yerden düzeltildi: `--uninstall-services` artık sürücüyü de indiriyor
  (kullanıcı ayarları korunarak), **ve kurulum bunu eski sürüme delege etmiyor** —
  `PrepareToInstall` içinde doğrudan `sc stop/delete windivert` çağırıyor. Bu şart,
  çünkü yükseltme sırasında çalışan exe henüz ESKİ sürüm ve o davranışa sahip değil.

  Gerçek makinede yeniden üretilip doğrulandı: sürücü RUNNING durumdayken eski
  sürümün üzerine kurulum → hata yok, dosya değiştirildi, sürücü kaldırıldı.

- **Pencereyi kapatırken çökme.** 0.1.1'de eklenen kapanma işleyicisi, kapanma
  işlemi sürerken `Close()` çağırıyordu; WPF bunu reddediyor ("Cannot ... call
  Close ... while a Window is closing"). Duman testi test barındırıcısını
  çökerterek yakaladı. Artık Dispatcher'a bırakılıyor.

- **Şifreli DNS çözümlemesi başarısız olduğunda ölçüm sessizce sistem DNS'ine
  düşüyordu.** DNS kaçırması olan bir hatta bu, DPI yerine DNS katmanını ölçmek
  demek — ve hiçbir yerde söylenmiyordu. Ölçülen bir örnek: bir kullanıcının
  sistem DNS'i `discord.com`'u `195.175.254.2`'ye (sağlayıcının engel sunucusu)
  çözüyor. Artık böyle bir hedef **belirsiz** işaretleniyor; belirsiz hedefte
  strateji aranmaz, kontrol hedefi belirsizse bölüm tümüyle atlanır.

- **"Çalışan strateji yok" mesajı nedenini söylemiyordu.** İki çok farklı durum
  aynı görünüyordu: stratejiler gerçekten tutmadı, ya da winws hiç çalışmadı.
  Artık en sık görülen başarısızlık sebepleri listeleniyor ve bütün denemeler aynı
  sebeple düştüyse bunun ortamla ilgili olduğu açıkça yazılıyor.

## [0.1.3]

### Değişti

- **Genel arama artık aileleri sırayla dolaşıyor: önce her aileden birer aday,
  sonra derinleşiyor.** Önceden aileler peş peşe, her aile sonuna kadar
  deneniyordu. Bütçe sınırsız olsa sıra önemsizdi; ama arayüz bölüm başına 60
  aday deniyor ve sonuç ölçüldü — başka bir TTNET hattında **176 aday denendi,
  1105 saniye sürdü, hiçbiri tutmadı.** Sebep: `tcp443` genel aramasında ilk aile
  (`fake-fooling`) tek başına 45 varyant ve genel aramaya kalan ~33 bütçenin
  tamamını yiyordu. Aynı bütçeyle denenen aile sayısı:

  | Bölüm | Önce | Sonra |
  |---|---|---|
  | tcp443 | **1** aile | **7** aile |
  | quic | 5 aile | 6 aile |

  `multisplit`, `multidisorder`, `fakedsplit`, `fake-tls-mod` ve `syndata`
  aileleri hiç denenmiyordu — aralarında sahte paket üretmeyenler bile var, yani
  mekanizma olarak tamamen farklı şeyler. Aile **içindeki** sıra değişmedi;
  o `generic-ladder.json`'da profil yazarının kararı.

### Düzeltildi

- **DoH çözümlemesi önbelleklenmiyordu.** Hedef listesinde 10 girdi ama 7
  benzersiz adres var (`discord.com` üç bölümde, `www.youtube.com` iki bölümde);
  aynı ad tekrar tekrar soruluyordu. Baseline taraması sıralı olduğu için bu
  doğrudan gecikmeye biniyordu: yavaş bir hatta 3-6 saniye boşa gidiyordu.

## [0.1.2]

### Düzeltildi

- **Kurulum sonundaki "şimdi başlat" çalışmıyordu.** Uygulamanın manifesti
  `requireAdministrator`; Inno kurulum sonu girdisini yükseltilmemiş bağlamda
  `CreateProcess` ile çalıştırdığı için **"CreateProcess tamamlanamadı; kod 740.
  The requested operation requires elevation."** hatası veriyordu. Kullanıcı için
  belirtisi kötü: kurulum başarıyla bitiyor ama uygulama açılmıyor, yalnızca
  masaüstü kısayolundan açılabiliyordu. `shellexec` bayrağı eklendi — UAC
  yükseltmesini yalnızca ShellExecute yapabiliyor.

## [0.1.1]

0.1.0 hiç yayımlanmadı: kurulum paketi üretildi, gerçek bir makineye kuruldu ve
**normal bir kullanıcı gibi çalıştırıldığında iki ciddi hata ortaya çıktı.** İkisi
de bu sürümde düzeltildi ve aynı hatta ölçümle doğrulandı.

### Düzeltildi

- **HTTPS bölümüne yanlış strateji uygulanıyordu.** Parametre testi bitince her
  bölümün kazananı HTTPS strateji listesine ekleniyor ve seçili strateji her turda
  üzerine yazılıyordu; bölümler `tcp80 → tcp443 → quic` sırasında geldiği için
  **sonuncusu, yani QUIC komutu, HTTPS stratejisi olarak** kalıyordu. Ölçülen sonuç:
  winws `--filter-tcp=443 --dpi-desync=fake --dpi-desync-any-protocol=1
  --dpi-desync-cutoff=n2 --dpi-desync-fake-quic=...` ile çalışıyordu — TCP bölümüne
  QUIC komutu. Kullanıcı "3 bölüm için çalışan parametre bulundu" görüyor, Başlat'a
  basıyor, düz HTTP açılıyor ama `discord.com` HTTPS'te RST almaya devam ediyordu.
  Düzeltmeden sonra aynı hatta `curl https://discord.com` → **HTTP 200**.
- **Pencereyi X ile kapatmak hiçbir şeyi temizlemiyordu.** Temizlik yalnızca "Çıkış"
  düğmesinin içindeydi; pencere kapanma işleyicisi yoktu. Gerçek makinede ölçüldü:
  koruma açıkken pencere kapatıldığında `winws` ve `dnscrypt-proxy` öksüz kaldı,
  sistem DNS'i `127.0.0.1`'de kaldı ve DNS yedeği diskte "geri alınmamış" olarak
  durdu. dnscrypt sonradan ölürse makine hiçbir adı çözemez. Artık iki çıkış yolu
  da aynı yerden geçiyor: "Çıkış" düğmesi de pencereyi kapatıyor, temizlik kapanma
  yolunda çalışıyor.
- **Kurulum, çalışan uygulamayı zorla öldürüyordu.** `taskkill /F`, uygulamanın
  kendi temizlik yolunu tümüyle atlıyordu; koruma açıkken yükseltme yapan bir
  kullanıcıda yukarıdakiyle aynı sonuç doğuyordu. Artık önce nazikçe kapatılıyor
  (uygulamanın kapanma yolu çalışsın diye), zorla öldürme yalnızca son çare.
- **Arayüzün parametre testi şifreli DNS'i kullanmıyordu.** "Şifreli DNS kullan"
  işaretli olsa bile ölçüm hedefleri sistem DNS'iyle çözüyordu. Türkiye'de o katman
  çoğu zaman kaçırılmış durumda olduğundan `discord.com` engel sunucusuna çözülüyor,
  ölçüm "engel sayfası" görüyor ve bölüm hiç aranmıyordu. Gerçek makinede ölçüldü:
  arayüz **"ENGEL BULUNAMADI"** diyordu, aynı hatta aynı anda CLI `--doh` ile 22
  çalışan strateji buluyordu. Yani ürünün ana yüzeyi, DNS kaçırması olan her hatta
  kullanılamaz haldeydi.

## [0.1.0] — ilk yayın (yayımlanmadı)

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

[Yayınlanmamış]: https://github.com/superuser-d0/zapret-tr/compare/v0.1.6...HEAD
[0.1.6]: https://github.com/superuser-d0/zapret-tr/releases/tag/v0.1.6
[0.1.5]: https://github.com/superuser-d0/zapret-tr/releases/tag/v0.1.5
[0.1.4]: https://github.com/superuser-d0/zapret-tr/releases/tag/v0.1.4
[0.1.3]: https://github.com/superuser-d0/zapret-tr/releases/tag/v0.1.3
[0.1.2]: https://github.com/superuser-d0/zapret-tr/releases/tag/v0.1.2
[0.1.1]: https://github.com/superuser-d0/zapret-tr/releases/tag/v0.1.1
[0.1.0]: https://github.com/superuser-d0/zapret-tr/releases/tag/v0.1.0
