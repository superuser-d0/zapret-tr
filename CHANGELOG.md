# Değişiklik günlüğü

Biçim [Keep a Changelog](https://keepachangelog.com/tr/1.1.0/) temelli,
sürümleme [Semantic Versioning](https://semver.org/lang/tr/).

Bu dosyada "doğrulandı" kelimesi dar bir anlam taşır: **gerçek bir hatta, ölçümle**.
Toplulukta bildirilmiş ya da mekanizmadan türetilmiş şeyler doğrulanmış sayılmaz.

## [Yayınlanmamış]

### Eklendi

- **Parametre testinden önce kalıntı taraması.** Bu araca gelenlerin çoğu başka bir
  araçtan geliyor — Türkiye'de en yaygını GoodbyeDPI. Eski araç "kaldırıldı" sanılıyor
  ama geride bir **servis kaydı** kalıyor; o servis açılışta ayağa kalkıp WinDivert
  sürücüsünü kapıyor, winws kendi sürücüsünü yükleyemiyor ve bütün adaylar aynı şekilde
  düşüyor. Dışarıdan görünen şey "hiçbir strateji çalışmadı" — bir kullanıcıda ölçüldü:
  176 aday, 1105 saniye, sonuç yok.

  Eskiden yalnızca **çalışan sürece** bakılıyordu ve o kontrol en sık karşılaşılan hâli
  hiç görmüyordu: kapalı ama kurulu kalıntı. Kullanıcı eski aracı kapatıyor, "kapattım"
  diyor, servis yine de açılışta geri geliyor. Tarama artık şunlara bakıyor:

  - bilinen araçların **çalışan süreçleri**;
  - bilinen araçların **kurulu servisleri** — ikilisi diskte var mı, açılışta başlıyor mu;
  - sahipsiz **WinDivert sürücü kayıtları** (hiçbir DPI aracı çalışmıyorken duranlar);
  - `127.0.0.1:53` portunu **kim tutuyor** — orayı başkası tutuyorsa şifreli DNS hiç
    açılamaz ve belirtisi sadece "şifreli DNS çalışmadı" olur;
  - `hosts` dosyasında **test hedeflerimizi yönlendiren satırlar** — o satır sistemdeki
    bütün DNS çözümlemesini atlar, şifreli DNS açık olsa bile.

  **Silme ayrı bir karar ve kullanıcının.** Ne silineceği tek tek yazılıp onay isteniyor.
  Silinebilir sayılan tek şey **kayıt**: ikilisi diskte olmayan öksüz servisler ve
  sahipsiz sürücü kayıtları. Başka bir ürünün **dosyalarına dokunulmuyor** — bizi
  engelleyen şey dosyalar değil kayıt, ve ikilisi yerinde duran bir araç çalışan bir
  kurulumdur; onu bozmanın geri dönüşü yok. Öyle bir araç için yapılan tek şey adını ve
  yolunu söylemek.

  Aynı tarama "Raporu Kaydet" çıktısına da giriyor.

- **DNS önbelleği, yönlendirmeden hemen sonra boşaltılıyor.** Yönlendirme tek başına
  yetmiyordu: Windows, DNS çevrilmeden **önce** alınmış cevapları tutmaya devam ediyor ve
  engel sunucusunun cevapları uzun TTL ile geliyor. Yani şifreli DNS açıldıktan sonra bile
  `discord.com` bir süre daha engel sunucusuna çözülüyor — önbellekteki zehirli kayıt.

  Dışarıdan görünen şey birebir "strateji tutmadı": trafik hâlâ engel sunucusuna gidiyor,
  winws ne yaparsa yapsın site açılmıyor, sonra birkaç dakika içinde kendiliğinden
  düzeliyor. Kafa karıştırıcılık sırasında en üst sıradaki belirti biçimi. Geri alırken de
  boşaltılıyor.

### Düzeltildi

Hepsi **tek bir gerçek kullanıcı raporundan** çıktı (2026-09-12, TTNET, 0.1.18).
Rapor "Raporu Kaydet" ile alınmıştı; yani kanal işe yaradı.

- **Arayüz "Doğrulandı: 1/4 hedef açılıyor" deyip yeşil "KORUMA AKTİF" gösteriyordu.**
  Başlatmadan sonraki doğrulama yalnızca **sıfır** hedef açıldığında uyarıyordu; 1/4'te
  "Doğrulandı" kelimesini kullanıp susuyordu. O dört tcp443 hedefi `discord.com`,
  `gateway.discord.gg`, `updates.discord.com` ve `www.youtube.com` — ve YouTube o hatta
  zaten engelli değil. Yani açılan tek hedef büyük olasılıkla hiçbir şey gerektirmeyendi
  ve Discord'un üçü de kapalıydı. Kullanıcı korunduğunu sanıyordu.

  İkinci kusur raporlamadaydı: yalnızca **sayı** yazılıyordu. Sayı teşhis vermiyor,
  **adlar** veriyor — bu rapordan hangi hedefin açılmadığını çıkarmak ancak hedef
  listesine bakarak mümkün oldu. Artık açılmayanlar adıyla yazılıyor ve kısmi başarı
  ayrı bir durum: **"ÇALIŞIYOR — KISMEN AÇIYOR"**.

- **dnscrypt-proxy'nin açılış dökümü günlükteki her şeyi dışarı itiyordu.** 523 satırlık
  raporun ~470 satırı çözümleyici listesiydi: sunucu başına üç satır, ardından 340
  satırlık gecikme tablosu. Arayüz günlüğü 500 satırla sınırlı olduğu için teşhis için
  gereken satırlar — başlatma komutu, winws'in söyledikleri, test sonuçları — ring
  buffer'dan düşüyordu. Rapor yine de okunabildi çünkü kullanıcı testten hemen sonra
  kaydetmişti; yani kurtaran şey tasarım değil şanstı.

  Sunucu başına tekrar eden satırlar ve gecikme tablosu artık günlüğe girmiyor.
  **Susturulan şey gürültü, bilgi değil:** hata/uyarı seviyeleri ve "en düşük gecikmeli
  sunucu" özeti geçiyor.

- **Arayüz "winws v72.13" yazıyordu; motor kendi ağzıyla "v72.12" diyor.** Kullanıcının
  günlüğündeki `github version v72.12` satırı ele verdi. Sebep tedarik zincirinde:
  `winws.exe` **`zapret-win-bundle`** deposundan bir *commit* ile sabitleniyor (o depoda
  tag yok), `v72.13` ise yalnızca sahte yük dosyalarının ve filtre parçalarının geldiği
  **`zapret`** deposunun tag'i. İki farklı kaynak tek bir numarayla etiketlenmişti.

  Bunun bedeli doğrudan teşhiste: "bu seçenek bu sürümde var mı" sorusu yanlış sürüme
  sorulursa cevap da yanlış olur — `docs/DEVAM.md`'deki iki çıkarım tam olarak buna
  dayanıyordu ve düzeltildi. Artık motorun kendi bildirdiği sürüm gösteriliyor; hiç
  çalışmadıysa uydurmak yerine ikilinin nereden geldiği yazılıyor.

### Bilinen — çözülmedi

- **Test 3 bölüm için çalışan strateji buluyor, 60 saniye sonra çalışma zamanında tcp443
  açılmıyor.** Aynı raporda, aynı oturumda: 23:56:31'de HTTPS için `fake + ttl4`
  doğrulanıyor ("açılan: discord-guncelleme, discord"), 23:57:37'de başlatma sonrası
  doğrulama 1/4 veriyor. Aynı sonuç bir önceki başlatmada da görüldü, yani tek seferlik
  değil.

  İki aday sebep var ve hangisi olduğu **bilinmiyor**: (a) test her bölümü ayrı bir winws
  örneğiyle ve `--ipset-ip` ile ölçüyor, çalışma zamanı üçünü tek örnekte ipset'siz
  çalıştırıyor; (b) çalışma zamanı komutunda QUIC bölümü yüzünden global filtreye
  `--wf-raw-part` giriyor, tcp443 ölçümünde ise o parça yok. Ayırt edici deney ve
  "ölçmeden dokunma" uyarısı `docs/DEVAM.md`'de.

## [0.1.19]

### Düzeltildi

Hepsinin çıkış noktası tek bir saha bildirimi: *"kurdum, bilgisayara restart attım,
olmadı."* Tek cümle, ekran görüntüsü yok, günlük yok. Bu cümleyi üretebilecek bütün
yollar tek tek tarandı; aşağıdakiler bulunanlar.

- **Yeni kurulmuş bir makinede arayüz "SİSTEM HAZIR" diyordu — koruma kapalıyken.**
  Kurulumdan hemen sonraki tablo şuydu: servis sağlayıcı "Bilmiyorum", strateji listesi
  boş, Başlat düğmesi kapalı, `winws` çalışmıyor, koruma yok — ve ekranın en üstünde, en
  büyük puntoyla **"SİSTEM HAZIR"**. Kullanıcının yapması gereken tek şey (parametre
  testi) hiçbir yerde yazmıyordu.

  Teknik olmayan bir kullanıcı için bu, yanlış bir cümleydi ve "kurdum, olmadı"nın en
  ucuz açıklaması. Başlık artık iki soruyu birden cevaplıyor: **"KORUMA KAPALI —
  KURULUM YARIM"** ve altında sıradaki adım. Durum değeri `Ready` olarak kaldı;
  değiştirmek düğmelerin etkinliğini bozardı — değişen yalnızca kullanıcıya söylenen şey.

- **Sıradaki adım, servis sağlayıcı "Bilmiyorum"a dönünce hemen siliniyordu.** Yukarıdaki
  düzeltmenin kendi içindeki bir açık: strateji listesi boşaltılırken durum bandının ayrıntı
  satırı `servis sağlayıcısı seçilmedi · strateji yok` ile eziliyor, yani kullanıcının tam da
  o anda görmesi gereken tek cümle kayboluyordu. Başlık "KURULUM YARIM" derken altında ne
  yapılacağı yazmıyordu. Bu hatayı, düzeltmeyi kilitlemek için yazılan testin **koşulsuz**
  hâle getirilmesi yakaladı — koşulun içindeyken sessizce hiç koşmuyordu.

- **"Başlat"ın yeniden başlatmayı atlatmadığı hiçbir yerde yazmıyordu.** "ZAPRET'İ
  BAŞLAT" yalnızca o oturum için bir `winws` süreci açıyor; bilgisayar kapanıp
  açıldığında geriye hiçbir şey kalmıyor. Karşılığı olan düğme ("Servis Olarak Yükle")
  ekranda duruyordu ama **hiçbir zaman önerilmiyordu**. Artık hem başlatmadan sonra hem
  de test bittiğinde açıkça söyleniyor, README'de de ayrı bir adım oldu.

- **Servis kurulumu, çözümleyici cevap vermese bile sistem DNS'ini 127.0.0.1'e
  çeviriyordu.** Bu kontrol uygulamanın kendi yolunda (`DnsCryptRunner`) baştan beri
  vardı, **servis yolunda yoktu**: `sc start` düşse bile yönlendirme yapılıyordu. Sonuç
  projedeki en kötü tablo — 127.0.0.1'i dinleyen kimse yok, makine hiçbir adı çözemiyor,
  yani kullanıcıya göre internet tamamen gitti. Üstelik açılıştan açılışa **kalıcı**:
  kurtarma yalnızca uygulama açıldığında koşuyor, dolayısıyla yeniden başlatmak durumu
  düzeltmiyor, pekiştiriyordu.

  Artık DNS, `127.0.0.1:53` gerçekten cevap verene kadar (en çok 30 sn) beklendikten
  sonra çevriliyor. Cevap gelmezse sistem DNS ayarına **dokunulmuyor** ve ölü servis geri
  sökülüyor.

- **DNS geri alma, `netsh` çıkış kodlarına hiç bakmıyor ve yedeği her hâlükârda
  siliyordu.** Geri alma başarısız olsa bile metot "geri aldım" deyip
  `dns-backup.json`'ı siliyordu: DNS 127.0.0.1'de kalıyor, geri dönülecek kayıt da
  kalmıyordu. İkinci bir sessiz hata da aynı yerdeydi — arayüz **adıyla** aranıyordu,
  yani kullanıcı bağlantıyı yeniden adlandırmışsa ("Ethernet" → "Ev") geri alma hiçbir
  şey yapmıyordu. Artık önce yedekteki GUID'den güncel ad bulunuyor, her `netsh`
  çağrısının sonucu okunuyor ve yedek **yalnızca hepsi başarılıysa** siliniyor.

- **Şifreli DNS yalnızca IPv4'ü kapsıyordu.** Arayüzde İSS'in verdiği bir IPv6
  çözümleyicisi duruyorsa (yönlendirici duyurusu ya da DHCPv6 ile gelir) Windows sorguyu
  pekâlâ oraya yolluyor ve DNS kaçırma katmanı ayakta kalıyor. Dışarıdan görünen şey
  "şifreli DNS açık ama site yine açılmıyor" — yani belirtisi stratejinin tutmamasıyla
  birebir aynı. Yönlendirilen arayüzlerin IPv6 DNS sunucuları artık boşaltılıyor;
  boşaltılan değer yedekte taşınıyor ve geri alma simetrik.

  Yönlendirmek yerine boşaltmak bilinçli: `dnscrypt-proxy`'yi `[::1]`'i de dinlemeye
  zorlamak, IPv6'nın kapalı olduğu makinelerde bağlanamayıp sürecin hiç açılmamasına yol
  açardı.

- **İkinci bir uygulama örneği her şeyi bozuyordu.** Pencereyi X ile kapatmak uygulamayı
  bildirim alanına indiriyor, yani "kapattım" sanan kullanıcı masaüstü kısayoluna tekrar
  tıklayabiliyor. O anda `winws` aynı filtreyle ikinci kez açılamadığı için "1 koduyla
  kapandı" veriyor (kullanıcıya göre "Başlat çalışmıyor"), ve daha kötüsü ikinci örnek
  kapanırken DNS yedeğini geri alıp **siliyor** — birinci örneğin şifreli DNS'i sessizce
  devre dışı kalıyordu. Artık tek örnek kilidi var ve ikinci başlatma nereye bakılacağını
  söyleyip çıkıyor.

- **Servisler öldüklerinde bir daha hiç başlamıyordu.** Açılışta servisler ağdan önce
  ayağa kalkabiliyor; `winws` sürücüyü açamayıp ya da `dnscrypt` ağ bulamayıp hemen
  ölürse kurtarma tanımı olmadan bir daha başlamıyor. Kullanıcının gördüğü şey tam olarak
  "kurdum, yeniden başlattım, çalışmıyor" oluyor — servis listede duruyor ama durmuş. Her
  iki servise de üç kademeli yeniden başlatma tanımı ekleniyor.

- **Şifreli DNS servisinin ölü olup olmadığı hiç sorulmuyordu.** Servis durumu bugüne
  kadar yalnızca `winws` için soruluyordu; arayüz "SERVİS MODU AKTİF" diyordu çünkü
  `winws` gerçekten ayaktaydı. Oysa `dnscrypt` düşerse sonuç `winws`inkinden ağır:
  sistem DNS'i hâlâ 127.0.0.1'i gösterirken orada dinleyen kimse kalmıyor. Artık ayrıca
  soruluyor ve uyarı yazılıyor.

- **Açılıştaki DNS kurtarması, servisin ayağa kalkmasını beklemiyordu.** Kullanıcıların
  çoğu uygulamayı açılıştan hemen sonra açıyor ve o an `ZapretTR-DNS` henüz hazır
  olmayabiliyor. Tek denemeye bakıp "ölmüş" saymak, çalışır durumdaki bir kurulumun
  şifreli DNS'ini sessizce sökmek demekti — yedek de silindiği için geri dönüşü yoktu.
  Yönlendirmeyi **servis** yaptıysa artık 20 saniye bekleniyor.

- **"Servis Olarak Yükle" seçili parametre yokken sessizce hiçbir şey yapmıyordu.**
  Kurulum sonrası ilk açılışta strateji listesi zaten boş olduğu için bu en olası yol:
  kullanıcı basıyor, ekranda hiçbir şey değişmiyor, düğmeyi bozuk sanıyor. Artık ne
  yapması gerektiğini söylüyor. Aynı düzeltmede, servis kuruluyken parametre seçili
  olmasa da **kaldırma** yolu açık kaldı.

- **Beklenmedik bir hata uygulamayı sessizce kaybettiriyordu.** Arayüz kurulurken çıkan
  bir hata Windows'un kendi çökme penceresiyle sonuçlanıyor ve kullanıcının elinde
  "açılmıyor"dan başka bir şey kalmıyordu. Artık hata `%ProgramData%\ZapretTR\cokme.log`
  dosyasına yazılıyor, kullanıcıya gösteriliyor ve pencere ayakta kalıyor — yarım çalışan
  bir pencere, kaybolan bir pencereden iyidir.

### Eklendi

- **"Raporu Kaydet" artık makinenin ölçülen durumunu da yazıyor.** Eski rapor yalnızca
  görünüm modelinin *bildiği* şeyleri taşıyordu — seçili profil, seçili strateji, günlük.
  Oysa "olmadı" bildirimlerinin çoğunda bunların hiçbiri yanlış değil; yanlış olan şey
  görünüm modelinin **bakmadığı** yerde duruyor. Rapora giren yeni bölüm o soruları
  cevaplıyor: yönetici yetkisi var mı, upstream dosyaları tam mı, `winws`/`dnscrypt`/
  `ZapretTR` süreçlerinden kaç tane ayakta, iki servis kurulu ve çalışıyor mu, sistem
  DNS'i kimde ve `127.0.0.1:53` cevap veriyor mu, hangi arayüzde hangi DNS yazılı,
  GoodbyeDPI gibi çakışan bir araç açık mı, `learned.json`'da kaç kayıt var.

  Asıl kazanç, kullanıcı bilgisayarı yeniden başlatıp uygulamayı **yeni** açtığında
  görünüyor: o durumda günlük neredeyse boş ve eski rapor biçimi "çalışmadı" cümlesine
  hiçbir şey ekleyemiyordu.

### Değişti

- **README'ye "Bilgisayarı yavaşlatır mı" bölümü.** Gerçek bir makinede ölçüldü, dönüşümlü
  (kapalı → açık → tekrar kapalı) — sondaki ikinci "kapalı", farkın ZapretTR'den mi yoksa
  hattın kendi dalgalanmasından mı geldiğini ayırmak için.

  Özet: `winws` 10 MB bellek ve sürekli ~95 MB/s indirme altında makinenin %0,2'sinden
  azı. Çekirdek sürücüsünün DPC/kesme süresinde ölçülebilir artış yok — oyun takılması
  olacaksa oradan çıkar. Gecikme değişmiyor. Tek gerçek maliyet şifreli DNS: bir adresi
  ilk kez çözmek 45–606 ms, tekrarında 0,4 ms (ISS'nin çözücüsünden bile hızlı).

  İki şey bilerek eksik bırakıldı ve README'de öyle yazıyor: **FPS doğrudan ölçülmedi**
  (ölçüm sırasında oyun çalıştırılmadı), ve **indirme hızı için kesin bir şey
  söylenmiyor** — hattı doyurabilen, hız sınırı koymayan bir test sunucusu bulunamadı.

- **Ölçüm bildirimi için konu şablonu.** Projenin asıl eksiği saha verisi: on profilin
  sekizinde tek bir doğrulanmış ölçüm yok. Testçinin ne yazacağını tek tek düşünmesi
  gerekmesin diye alanlar hazır geliyor — hangi hat, paylaşımlı ağ mı, ne oldu, günlük.
  README'deki çağrı da somutlaştı: üç adım ve forma doğrudan bağlantı.

- **`docs/DEVAM.md` güncellendi.** 19 commit geride kalmıştı. Eklenen dersler: kapalı
  `Expander` içeriğinin görsel ağaca hiç girmemesi, motor hatasını "strateji yok" diye
  raporlamanın yanlış teşhis olması, hatayı kullanıcının GEÇTİĞİ yola göre düzeltmek,
  sürüm karşılaştırmasının metin olarak sessizce yanlış olması, öğrenilmiş kayıt
  anahtarının çakışması, "kurulu" ile "çalışıyor"un ayrı sorular olması, ve arayüzü
  otomasyonla sürerken çıkan tuzaklar (UIPI, DPI farkındalığı, MessageBox'ın ağaçtaki
  yeri, düğme yazısının duraklatınca değişmesi).

## [0.1.18]

### Eklendi

- **"Hata Bildir" düğmesi.** GitHub'daki bildirim formunu **doldurulmuş halde** açıyor:
  sürüm, motor sürümü, Windows, servis sağlayıcı, seçili strateji ve parametre, şifreli
  DNS durumu, otomatik başlatma durumu ve günlüğün son satırları formda hazır geliyor.

  Neden: eldeki en öğretici saha bildirimi ("çıkış kodu 34") tek başına işe yaramadı,
  ikinci bir koşum gerekti — çünkü hangi sürümde, hangi hatta, hangi parametreyle
  olduğunu bilmiyorduk. Bunları kullanıcıdan tek tek istemek yerine forma önceden
  koyuyoruz.

  **Hiçbir şey gönderilmiyor.** Düğme tarayıcıda formu açmaktan ibaret; okuyup
  düzenlemek ve göndermek kullanıcının. Uygulamanın içinden doğrudan issue açmak
  bilerek yapılmadı: bunun için gereken GitHub belirteci exe'ye gömülseydi herkes
  çıkartıp bizim adımıza konu açabilirdi, ve anonim gelen bir bildirime geri soru
  sorulamıyor.

  Kullanıcının "Açılmayan site" kutusuna **kendi yazdığı** adres forma **bilerek
  konmuyor**: denenen parametreler ve varsayılan hedefler bizim listemiz, ama o adres
  kullanıcıya ait ve genel bir forma kendiliğinden düşmemeli.

- **Konu şablonu** (`.github/ISSUE_TEMPLATE`). GitHub'a elle gelenler de aynı alanları
  dolduruyor.

  Gerçek bir makinede uçtan uca ölçüldü: düğmeye basıldı, onay kutusu çıktı,
  **"Hayır" denince tarayıcı açılmadı**, "Evet" denince GitHub'ın formu **dolu**
  geldi. "Açılmayan site" kutusuna yazılan adres formda **yoktu**. Bu koşumda
  başlığın yapı karmasını (`0.1.18+a8a66ca…`) taşıdığı görülüp düzeltildi:
  karma artık yalnızca ortam tablosunda, başlıkta değil.

## [0.1.17]

### Düzeltildi

- **Servis kurulu ama durmuşsa kullanıcı kilitleniyordu.** Uygulama servisin yalnızca
  **var olup olmadığına** bakıyordu, **çalışıp çalışmadığına** değil. Bir yükseltmeden
  sonra `sc start` herhangi bir sebeple başarısız olursa servis VAR ama DURMUŞ kalıyor;
  arayüz bunu "SERVİS MODU AKTİF" diye okuyup **Başlat düğmesini kapatıyordu**. Sonuç:
  koruma yok, ve kullanıcının yapabileceği hiçbir şey yok.

  Bir kullanıcıda 0.1.9'dan 0.1.15'e yükseltmeden sonra tam olarak bu yaşandı —
  "güncelledim, bağlanamadım" bildiriminin sebebi büyük olasılıkla buydu.

  Artık üç durum ayrı: **kurulu değil** (Başlat açık), **kurulu ve çalışıyor**
  (Başlat kapalı, gerek yok), **kurulu ama durmuş** → durum **"SERVİS DURMUŞ"** oluyor,
  ne yapılacağı yazılıyor ve **Başlat açık kalıyor** ki kullanıcı korumasını elle
  başlatabilsin.

### Değişti

- README güncellendi: yeni yetenekler (tek tıkla güncelleme, bildirim alanı, rapor),
  servis kurulumu/kaldırması sonrası ne yapılacağı, "SERVİS DURMUŞ" durumu ve güncel
  test sayısı.

## [0.1.16]

### Düzeltildi

- **Eski doğrulama, yenisiyle birlikte listede kalıyordu.** Öğrenilmiş sonuçlar
  kaydedilirken anahtar (sağlayıcı, bölüm, **parametre**) idi. Yani bir testte
  `fake+ttl4` doğrulanıp kaydediliyor, engelleme değişip yeni testte
  `multisplit pos=2` kazanıyor ve **ikisi birden** "bu bağlantıda doğrulandı"
  etiketiyle listede duruyordu. Çalışma zamanında bölüm başına tek kazanan
  kullanıldığı için ikinci kayıt hiçbir şey eklemiyor; yalnızca hangisinin güncel
  olduğunu belirsizleştiriyor ve kayıtlı seçim eskisini gösterebiliyordu.

  Anahtar artık (sağlayıcı, bölüm). "Eskiden çalışıyordu" bir kanıt değil: o ölçüm
  artık geçerli olmayan bir ağ durumuna aitti.

- **"Tüm Ayarları Sıfırla" ekranı sıfırlamıyordu.** Disk temizleniyordu ama
  profiller, seçili sağlayıcı ve strateji **bellekte** duruyordu: kullanıcı
  sıfırladıktan sonra ekranda hâlâ eski sağlayıcıyı ve "✓ doğrulanmış" stratejiyi
  görüyordu — silinmiş bir şeyin adı ekranda kalıyordu. Daha kötüsü, o hâliyle
  Başlat'a basmak diskte karşılığı olmayan seçimi yeniden kaydediyordu. Artık
  profiller yeniden yükleniyor, seçimler ve tercihler ilk açılış hâline dönüyor.

### Değişti

- **"Güncellemeleri Denetle" sonucu artık pencerede söyleniyor.** Güncelleme yoksa
  "Yeni güncelleme bulunamadı" penceresi çıkıyor. Eskiden sonuç yalnızca günlüğe
  yazılıyordu ve Ayrıntılar paneli varsayılan olarak kapalı; kullanıcı düğmeye
  basıyor ve hiçbir şey olmamış gibi görünüyordu.

- **Otomatik başlatma kurulunca ne yapılacağı net söyleniyor:** uygulamayı
  kapatabilirsiniz, koruma servis olarak çalışır, yeniden başlatmaya **gerek yok**.

- **Otomatik başlatma kaldırılınca yeniden başlatma öneriliyor.** Kurulumda değil,
  yalnızca kaldırmada: WinDivert sürücüsü çekirdekten hemen düşmüyor ve kalıntı bir
  sürücü, sonraki parametre testinde bütün adayların aynı şekilde başarısız olmasına
  yol açabiliyor (bir kullanıcıda 176 aday, 1105 saniye, sonuç yok).

- **Sürüm değiştiğinde bildiriliyor.** Güncellemeden sonra ilk açılışta "sürüm
  değişti, kayıtlı stratejiniz korundu" deniyor. Strateji **silinmiyor**: bir sürüm
  değişikliği İSS'in DPI yapılandırmasını değiştirmediği için ölçüm hâlâ geçerli.
  "Ya artık çalışmıyorsa" endişesi zaten karşılanmış durumda — başlattıktan sonra
  hedefler gerçekten ölçülüyor ve açılmıyorsa yeni test öneriliyor.

## [0.1.15]

### Eklendi

- **"Güncellemeleri Denetle" düğmesi ve tek tıkla kurulum.** Düğme yeni sürüm var
  mı diye bakar; varsa paketi indirir, **SHA256 özetini doğrular** ve kurulumu
  başlatır. Artık tarayıcı açıp dosya aramak, indirip SmartScreen uyarısını geçmek
  gerekmiyor.

  Kurulum **onaysız başlamaz**: ne indirileceği, özetin doğrulanacağı ve ZapretTR'nin
  kapanacağı önce sorulur. Özet tutmazsa dosya silinir ve kurulum yapılmaz —
  indirilen paket çalıştırılacağı için bu doğrulama atlanamaz.

- **Açılışta güncelleme bildirimi.** Yeni sürüm varsa arayüzde bir satır ve günlükte
  indirme adresi görünüyor. Sebebi somut: bazı günler birkaç sürüm çıkıyor ve her
  seferinde test kullanıcılarına tek tek "şunu kur" demek gerekiyordu — kullanıcıya
  ulaşmayan bir düzeltme işe yaramıyor.

  **Bu, uygulamanın dışarı istek yapan tek yeri** ve saklanacak bir şey değil:
  GitHub'a yalnızca "en son sürüm ne" sorusu gidiyor. Hattınız, stratejiniz ya da
  ölçüm sonuçlarınız **gönderilmiyor**. `config.json` içindeki `updateCheckEnabled`
  değeri `false` yapılırsa uygulama hiçbir ağ isteği yapmaz.

  Sürüm karşılaştırması metin değil sayı olarak yapılıyor. Metin karşılaştırması
  sessizce yanlış sonuç verirdi: "0.1.9" ile "0.1.14" karşılaştırıldığında metin
  sırası 0.1.9'u sonraya koyar ve bildirim hiç görünmezdi — hata da vermezdi.

### Değişti

- README ve yayın notundaki "hiçbir veri dışarı gönderilmez" ifadesi güncellendi.
  Güncelleme kontrolü eklendiği için o cümle artık tam doğru değildi; ne
  gönderildiği ve nasıl kapatılacağı açıkça yazıldı.

## [0.1.14]

### Düzeltildi

- **Saha testi, engel bulamadığında rapor yazmadan çıkıyordu.** 0.1.13'te hata
  yolundaki eksik rapor düzeltilmişti ama asıl yol bu değildi: test sorunsuz
  çalışıp "DPI ile engellenen hedef yok" dediğinde de hiçbir dosya
  bırakılmıyordu. Kullanıcı ekranda dolu dolu çıktı görüyor, sonra klasörde JSON
  bulamıyordu.

  **"Engel yok" da bir sonuçtur.** O koşum "bu hatta şu an engel yok" bilgisini
  veriyor — saha paketinin toplamak istediği şeyin ta kendisi. Artık bu yolda da
  (ve `--baseline` modunda da) rapor yazılıyor: hangi hedef açıldı, hangisi
  engellendi, DNS yönlendirilmiş mi.

- **Koruma açıkken yapılan ölçüm sessizce yanıltıyordu.** `winws` çalışırken saha
  testi koşturulursa hedefler zaten açılır ve test "engel yok" der. Ölçüm doğrudur,
  ama ölçtüğü şey engelleme değil **kendi korumamızdır**. Gerçek bir kullanıcıda
  tam olarak bu yaşandı: 11 hedefin 11'i "açılıyor" çıktı. Test artık başlarken
  `winws`in çalıştığını fark edip uyarıyor ve önce nasıl durdurulacağını söylüyor.

## [0.1.13]

### Eklendi

- **Pencereyi kapatmak artık korumayı kapatmıyor.** X düğmesi uygulamayı saat
  yanındaki bildirim alanına indiriyor; `winws` ve şifreli DNS çalışmaya devam
  ediyor. Pencereyi geri getirmek için simgeye çift tıklayın. Uygulamayı gerçekten
  sonlandırmak için **"Çıkış"** düğmesi ya da simgenin sağ tık menüsündeki "Çıkış".

  İlk gizlemede bir bildirim baloncuğu çıkıyor — kullanıcı uygulamanın kapandığını
  sanıp simgeyi aramazsa "kapatamıyorum" durumuna düşer.

### Düzeltildi

- **Saha testi paketi hata durumunda hiçbir dosya bırakmıyordu.** Test bir yerde
  patlarsa hata ekrana yazılıp çıkılıyor, `--out` ile istenen JSON hiç
  oluşturulmuyordu. Kullanıcının elinde gönderecek bir şey kalmıyor, neyin
  patladığını da kimse öğrenemiyordu — paketin tek işi veri toplamak olduğu halde.
  Artık hata durumunda da bir rapor yazılıyor (ne olduğu, hangi tür hata, ne zaman).

- **Rapor yolu göreliydi.** "zapret-tr-rapor.json yazıldı" mesajı, dosyanın nerede
  oluştuğunu bilmeyen birine hiçbir şey anlatmıyordu. Artık tam yol yazılıyor.

### Doğrulama

- Türk Telekom (AS9121) profili **ikinci bağımsız bir kullanıcıda daha** çalıştı.
  Aynı sağlayıcıda üç ayrı hatta üç farklı davranış ölçmüştük; bu, profilin farklı
  hatlarda tekrarlandığına dair ilk saha teyidi. Ölçüm raporu alınmadığı için
  profildeki adaylar `verified` sayısını değiştirmiyor.

## [0.1.12]

Bu sürümün tamamı tek bir saha raporundan çıktı. Bir kullanıcının makinesinde test
**320 adayı 23 saniyede** "denedi" ve arayüz "ÇALIŞAN STRATEJİ YOK" dedi. Gerçek şu ki
hiçbir aday denenmemişti: `winws` her seferinde başlar başlamaz 34 koduyla ölüyordu.

### Düzeltildi

- **Motor hiç başlamadığında arayüz "strateji bulunamadı" diyordu.** İki bambaşka
  durum aynı ekranı gösteriyordu: "bu hatta hiçbir strateji işe yaramıyor" ile
  "makinede bir şey bozuk, ölçüm yapılamadı". Birincisi "başka çözüm ara" demek,
  ikincisi "düzelt ve tekrar dene" demek. Artık ayrı: motor art arda 25 denemede hiç
  başlamazsa arama **duruyor** ve durum **"ÖLÇÜM YAPILAMADI"** oluyor, ne yapılacağı
  sırayla yazılıyor (yeniden başlatma, çakışan aracı kapatma).

- **`winws`'in kendi hata mesajı atılıyordu.** Erken ölümde yalnızca çıkış kodu
  gösteriliyor, "ayrıntılar için günlüğe bakın" deniyordu — ama günlükte ayrıntı
  yoktu, çünkü motorun çıktısı okunmadan hata fırlatılıyordu. 304 aday bu mesajla
  düştü ve sebebini kimse öğrenemedi. Artık motorun ilk satırları hata mesajında.

- **Geçersiz parametre üretiyorduk.** Genel aramadaki
  `--dpi-desync-fakedsplit-mod=altorder` değeri `winws` tarafından reddediliyordu
  (`Invalid fakedsplit mod : altorder`); motorun kendi yardım metni
  `altorder=0|1|2|3` bekliyor. Her koşumda 12-16 aday buna harcanıyordu.

## [0.1.11]

### Düzeltildi

- **"Raporu Kaydet" düğmesi görünmüyordu.** 0.1.10'da düğmeyi "Ayrıntılar" panelinin
  içine koymuştuk. WPF kapalı bir panelin içeriğini hiç oluşturmuyor, dolayısıyla
  paneli açmayan kullanıcı için düğme **yoktu** — gerçek bir kurulumda tam olarak bu
  yaşandı. Düğme artık ana düğme öbeğinde, "Servis Olarak Yükle" ile "Tüm Ayarları
  Sıfırla" arasında ve açılışta görünür.

  Gerekçemiz ("günlüğün yanında dursun, bağlamı orası") yanlıştı: bulunamayan bir
  düğmenin bağlamı da olmaz.

- **Test bu hatayı yakalamıyordu, artık yakalıyor.** Düğmenin pencerede bulunması
  yetmiyor; kapalı bir panelin altında olmadığı da sınanıyor. Düzeltme geri alınarak
  doğrulandı: 0.1.10 yapısında test `"Raporu Kaydet" bir Expander icinde` diyerek
  düşüyor.

## [0.1.10]

### Eklendi

- **"Raporu Kaydet" düğmesi.** Ayrıntılar panelinde, günlüğün hemen altında. Günlüğü
  ve ortam özetini (sürüm, motor, servis sağlayıcı, strateji, parametre, DNS ve servis
  durumu) tek bir metin dosyasına yazıyor.

  Bu bir kolaylık değil, eksik bir kanaldı: Türksat Kablonet kullanıcısı 0.1.6'nın
  çalıştığını bildirdi ama profil hâlâ doğrulanmamış durumda, çünkü **hangi adayın**
  kazandığını bilmiyoruz — günlüğü iletmenin tek yolu pencereden metni elle seçip
  kopyalamaktı. Ortam özeti de rapora bu yüzden giriyor: "şu aday çalıştı" satırını
  okuyup hangi profil ve hangi sürümle olduğunu bilmeden profile işleyemiyoruz.

  Dosya **hiçbir yere gönderilmiyor**, yalnızca diske yazılıyor; neyi paylaşacağınıza
  siz karar veriyorsunuz. Başında ne içerdiği yazıyor, genel IP adresiniz yazılmıyor.

### Değişti

- **CI artık kurulumu gerçekten çalıştırıyor.** Yeni `installer-test.yml` işi paketi
  kuruyor, servisi kurduruyor, üzerine yükseltme yapıp servisin sağ kalıp kalmadığına
  bakıyor, sonra söküp kaldırıyor ve makinede kalıntı bırakmadığını doğruluyor.
  Gerekçesi ölçülebilir: 2026-09-08'de bulunan dört kullanıcı-etkileyen hatanın
  **hiçbirini** birim testleri yakalamadı; ikisi tam da bu katmanda yaşıyordu.
  Yükseltmenin servisi silmesi (0.1.8) artık regresyon testi olarak sınanıyor —
  düzeltme geri alınarak testin gerçekten yakaladığı kanıtlandı.

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
