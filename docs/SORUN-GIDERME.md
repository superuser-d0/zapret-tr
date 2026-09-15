# Sorun giderme

Uygulamanın en üstündeki büyük yazı (**durum bandı**) size ne olduğunu söyler. Aşağıda o
yazıyı bulun ve yanındaki adımları sırayla deneyin. Altındaki **günlük** penceresi de her
durumda ayrıntıyı ve çoğu zaman sebebini yazar; "Ayrıntılar" ile açabilirsiniz.

> **Her durumda ilk iş: "Raporu Kaydet".** Dosya masaüstüne yazılır ve hiçbir yere
> gönderilmez. Sorunu kendiniz çözemezseniz bize yardım edebilmemizi sağlayan tek şey o
> dosyadır. Ne işe yaradığı en altta: [Hiçbiri işe yaramadıysa](#hicbiri-ise-yaramadiysa).

## Hızlı bakış

| Ekranda yazan | Ne demek | İlk yapılacak |
|---|---|---|
| [ÇALIŞAN STRATEJİ YOK](#strateji-yok) | Test bitti, hiçbir aday açmadı | Günlükteki sebebe bakın, bilgisayarı yeniden başlatıp tekrar deneyin |
| [ÖLÇÜM YAPILAMADI](#olcum-yapilamadi) | Motor hiç başlamadı, hiçbir şey denenmedi | Başka DPI aracını kapatın, yeniden başlatın |
| [ENGEL BULUNAMADI](#engel-bulunamadi) | Test hedefleri zaten açılıyor | VPN'i kapatın ya da açılmayan siteyi yazın |
| [KISMEN ÇALIŞIYOR](#kismen-calisiyor) | Bir şey bulundu ama bazı adresler hâlâ kapalı | Önce Başlat'ı deneyin, takılan uygulama varsa testi tekrarlayın |
| [TEST BAŞARISIZ](#test-basarisiz) | Test beklenmedik bir hatayla durdu | Günlükteki mesaja bakın, raporu kaydedin |
| [BAŞLATILAMADI](#baslatilamadi) | Başlat'a basıldı, koruma açılamadı | Günlükteki mesaja göre aşağıya bakın |
| [ÇALIŞIYOR — AMA AÇMIYOR](#calisiyor-ama-acmiyor) | Kayıtlı ayar artık işe yaramıyor | Yeni parametre testi |
| [ÇALIŞIYOR — KISMEN AÇIYOR](#calisiyor-kismen-aciyor) | Bazı adresler açılmıyor | Yeni parametre testi |
| [BEKLENMEDİK DURUŞ](#beklenmedik-durus) | Koruma kendiliğinden kapandı | Tekrar Başlat, olmazsa yeniden başlatın |
| [SERVİS DURMUŞ](#servis-durmus) | Otomatik başlatma kurulu ama çalışmıyor | Başlat'a basın ya da servisi yeniden kurun |
| [DURAKLATILDI](#duraklatildi) | Korumayı siz duraklattınız | "DEVAM ET" ile açın |
| [TAM DURAKLATILAMADI](#duraklatildi) | Duraklat bir parçayı durduramadı | Günlükteki `hâlâ duranlar` satırına bakın, tekrar Duraklat |
| [SERVİS İŞLEMİ BAŞARISIZ](#servis-islemi-basarisiz) | Servis kurulamadı ya da kaldırılamadı | Günlükteki `[!]` satırına bakın |
| [KURULUM DOSYALARI EKSİK](#kurulum-dosyalari-eksik) | Antivirüs büyük ihtimalle dosya sildi | Karantinadan geri alın, yeniden kurun |
| [... antivirüs tarafından engellendi / Akıllı Uygulama Denetimi](#windows-engelliyor) | Windows koruması bir dosyayı çalıştırmadı | Hangi korumanın engellediğine göre aşağıya bakın |

Ekranda yazmayan ama sık yaşanan durumlar:
[Kurulum paketi açılmıyor ya da siliniyor](#windows-engelliyor) ·
[VPN bağlanmıyor](#vpn-baglanmiyor) ·
[İnternet tamamen gitti](#internet-gitti) ·
[Yeniden başlatınca koruma kapalı geliyor](#yeniden-baslatinca) ·
[Kalıntı temizliği penceresi](#kalinti-temizligi) ·
[Uygulama açılmıyor / çöktü](#uygulama-acilmiyor)

---

## Parametre testi sonuçları

<a id="strateji-yok"></a>

### ÇALIŞAN STRATEJİ YOK — parametre bulunamadı

Test bitti ve denenen adayların hiçbiri engeli aşamadı. Bu **iki çok farklı** anlama
gelebilir, ve hangisi olduğunu günlük söyler. Test bitince günlükte şöyle bir bölüm çıkar:

```
En sık görülen sebepler:
   48x  ...
```

**Günlükte "Bütün denemeler aynı sebeple düştü" yazıyorsa** sorun büyük ihtimalle
stratejilerde değil, makinenizde: motor paketleri hiç görememiş. Sırayla:

1. Açıksa **GoodbyeDPI, ByeDPI, SpoofDPI** gibi başka bir DPI aracını ya da **VPN**'i kapatın.
2. **Bilgisayarı yeniden başlatın.** Ağ sürücüsü önceki bir koşumdan çekirdekte asılı
   kalabiliyor; bu en sık sebep ve yeniden başlatmadan geçmiyor.
3. Testi tekrar çalıştırın.
4. Günlükte `UYARI: servis 15 saniyede durmadı` yazıyorsa "Otomatik Başlatmayı Kaldır"
   deyin, bilgisayarı yeniden başlatın ve testi öyle çalıştırın. (Otomatik başlatma
   kuruluysa uygulama onu test boyunca kendisi durdurur; bu uyarı durduramadığı anlamına gelir.)

**Sebepler farklı farklıysa** ölçüm düzgün yapılmış ve adaylar gerçekten tutmamış demektir.
O zaman:

1. **"Şifreli DNS kullan" işaretli mi?** Değilse işaretleyin ve testi tekrarlayın. Türkiye'de
   engel çoğu zaman önce DNS katmanında; o katman aşılmadan hiçbir strateji işe yaramaz.
2. **Servis sağlayıcıyı elle seçin.** "Bilmiyorum" seçiliyken otomatik tespit yanılmış ya da
   sağlayıcınız bulunamamış olabilir (günlükte "Bu sağlayıcı için profil yok" yazar). Doğru
   sağlayıcı seçilince önce o hatta bilinen adaylar denenir.
3. **Paylaşımlı bir ağdaysanız** (iş yeri, yurt, site ağı) trafiğiniz sandığınızdan farklı bir
   sağlayıcıdan çıkıyor olabilir. Mümkünse evden ya da telefonun mobil paylaşımından deneyin.
4. Testi **bir kez daha** çalıştırın. Arayüzdeki test bölüm başına en fazla 60 aday dener;
   sonuçlar ağın o anki durumuna göre de değişebiliyor.

**Önemli:** "strateji bulunamadı" her zaman "hiçbir şey açılmıyor" demek değildir. Günlüğün
başındaki `Mevcut durum` satırlarına bakın; engellenen bölüm ikincil olabilir (örneğin HTTPS
zaten açıkken yalnızca düz HTTP kapalı olabilir) ve kullandığınız uygulama belki zaten
çalışıyordur.

Hâlâ sonuç yoksa lütfen **raporu kaydedip bize gönderin**. Sizin hattınızda ne olduğunu
bilmeden yeni aday ekleyemiyoruz; bu rapor tam olarak bunun için:
[ölçüm bildirimi formu](https://github.com/superuser-d0/zapret-tr/issues/new?template=olcum-bildirimi.md).

<a id="olcum-yapilamadi"></a>

### ÖLÇÜM YAPILAMADI

Motor (`winws`) art arda birkaç kez **hiç başlamadı**, bu yüzden test durduruldu. Bu bir
strateji sorunu **değil**: hattınız hakkında hiçbir şey ölçülmedi.

1. **Bilgisayarı yeniden başlatın.** Takılı kalmış ağ sürücüsü en sık sebep.
2. GoodbyeDPI gibi başka bir DPI aracı açıksa kapatın. Kurulu bir servis olarak duruyorsa
   o aracın **kendi kaldırma yolunu** kullanın; "kapattım" sanılan araç açılışta geri gelir.
3. Antivirüs `WinDivert64.sys` dosyasını engelliyor olabilir. ZapretTR klasörünü
   (`C:\Program Files\ZapretTR`) istisna listesine ekleyin.
4. Testi yeniden çalıştırın.

<a id="engel-bulunamadi"></a>

### ENGEL BULUNAMADI

Testin denediği adreslerin **hepsi zaten açılıyor**, dolayısıyla aranacak bir şey yok.

- **VPN, proxy ya da başka bir DPI aracı açıksa** kapatın ve testi tekrarlayın. Açıkken
  engeli o aşıyor ve test "engel yok" görüyor.
- Sizde açılmayan belirli bir site varsa **"Açılmayan site"** kutusuna adresini yazın
  (örneğin `discord.com`) ve testi öyle çalıştırın.
- Gerçekten her şey açılıyorsa ZapretTR'ye ihtiyacınız yok; hattınızda şu an engel yok.

<a id="kismen-calisiyor"></a>

### KISMEN ÇALIŞIYOR

Test çalışan bir ayar buldu, ama bazı adresler hâlâ kapalı. Günlükte hangileri olduğu
yazar: `DİKKAT: şu hedefler hâlâ açılmıyor: ...`

1. Önce yine de **"ZAPRET'İ BAŞLAT"**'a basıp kullandığınız uygulamayı deneyin. Açılmayan
   adres sizin işinize yaramayan bir şey olabilir.
2. Uygulama bir ekranda takılıyorsa (örneğin Discord **güncelleme** ekranında kalıyorsa)
   testi **tekrar çalıştırın**.
3. Açılmayan adresi biliyorsanız **"Açılmayan site"** kutusuna yazıp testi öyle çalıştırın.

<a id="test-basarisiz"></a>

### TEST BAŞARISIZ

Test beklenmedik bir hatayla durdu. Bantın altında ve günlükte hata mesajı yazar.

- Mesajda **"yonetici yetkisiyle calismali"** geçiyorsa uygulamayı kapatıp sağ tık → "Yönetici olarak
  çalıştır" ile açın.
- Mesajda **"Kurulum dosyalari eksik"** geçiyorsa: [KURULUM DOSYALARI EKSİK](#kurulum-dosyalari-eksik).
- Diğer her durumda: **Raporu Kaydet**, sonra **Hata Bildir**. Bu durumu biz de görmek isteriz.

"Test iptal edildi" yazıyorsa bu hata değil; testi siz ya da uygulamayı kapatmanız durdurmuş.

<a id="kalinti-temizligi"></a>

### Kalıntı temizliği penceresi ve çakışma taraması

Test başlamadan önce uygulama, ölçümü bozabilecek şeyleri arar ve günlüğe
`ÇAKIŞMA TARAMASI: N bulgu` diye yazar. Bulgunun türüne göre:

| Günlükte yazan | Ne yapmalı |
|---|---|
| `... şu anda çalışıyor.` | O programı kapatın. İki araç aynı anda ağ sürücüsünü kullanamaz. |
| `... kaldırılmış ama "..." servis kaydı duruyor.` | Uygulama bunu **kendisi silebilir**: çıkan pencerede "Evet" deyin, sonra bilgisayarı yeniden başlatın. |
| `Sahipsiz ağ sürücüsü kaydı` | Aynı şekilde pencerede "Evet", ardından yeniden başlatma. |
| `... kurulu ("..." servisi).` | Çalışan bir kurulum; uygulama ona **dokunmaz**. O aracı kendi kaldırıcısıyla kaldırın ya da servisini durdurun. |
| `127.0.0.1:53 portunu "..." tutuyor.` | Başka bir DNS programı (reklam engelleyici, başka bir şifreli DNS aracı vb.) açık. Kapatın; kapatmazsanız şifreli DNS çalışamaz. |
| `hosts dosyası ... adresini ... yapıyor.` | `C:\Windows\System32\drivers\etc\hosts` dosyasında eski bir satır var. Not Defteri'ni yönetici olarak açıp o satırı silin ya da başına `#` koyun. |

Kalıntı temizliği penceresi **yalnızca Windows servis kayıtlarını** siler; başka bir programın
dosyalarına dokunmaz. "Hayır" derseniz test yine çalışır, ama sonucu güvenilir olmayabilir.

---

## Koruma açıkken

<a id="baslatilamadi"></a>

### BAŞLATILAMADI

"ZAPRET'İ BAŞLAT"'a bastınız ama koruma açılamadı. Bantın altındaki mesaja göre:

**`winws baslar baslamaz 1 koduyla kapandi`**: Motor zaten çalışıyor, ikincisi açılamıyor.
- Durum bandı daha önce **SERVİS MODU AKTİF** diyorsa korumanız zaten açık; Başlat'a
  basmanıza gerek yok.
- Değilse uygulamayı "Çıkış" ile tamamen kapatın, bilgisayarı yeniden başlatın ve tekrar
  deneyin.

**`dnscrypt-proxy baslatildi ama DNS sorgularina cevap vermedi`**: Şifreli DNS açılamadı.
Sistem DNS ayarınıza **dokunulmadı**, internetiniz eskisi gibi.
- Mesajda `53 dinlenmiyor` geçiyorsa başka bir DNS programı o portu tutuyor. O programı
  kapatın (hangisi olduğunu testteki çakışma taraması söyler).
- İlk açılışta şifreli DNS sunucu listesini indirmek zorunda; bağlantı yavaşsa zaman aşımına
  düşebilir. Bir kez daha deneyin.
- Olmuyorsa "Şifreli DNS kullan" işaretini kaldırıp başlatabilirsiniz, ama DNS katmanında
  engellenen sitelerin açılmayacağını bilin.

**`Varsayilan ag gecidi olan bir arayuz bulunamadi`**: Bilgisayar o an internete bağlı
değil. Bağlantıyı kontrol edip tekrar deneyin.

**`Kurulum dosyalari eksik`**: [KURULUM DOSYALARI EKSİK](#kurulum-dosyalari-eksik).

<a id="calisiyor-ama-acmiyor"></a>

### ÇALIŞIYOR — AMA AÇMIYOR

Koruma açık, ama başlattıktan birkaç saniye sonra yapılan kontrolde **hiçbir hedef
açılmadı**. Kayıtlı ayar büyük ihtimalle artık işe yaramıyor: servis sağlayıcılar engelleme
yöntemlerini zaman zaman değiştiriyor.

1. **"PARAMETRE TESTİ YAP"** ile yeni bir ayar arayın.
2. Otomatik başlatma kuruluysa yeni ayar bulunduktan sonra **"Otomatik Başlatmayı Kaldır"**,
   ardından **"Servis Olarak Yükle"** deyin. Servis kurulduğu andaki ayarla çalışır; test
   sırasında uygulama onu geçici olarak durdurup geri açar ama yeni ayarı ona kendiliğinden
   geçirmez.
3. Ağ değiştirdiyseniz (başka ev, telefon paylaşımı) servis sağlayıcı listesinden yeni hattı
   seçin.

<a id="calisiyor-kismen-aciyor"></a>

### ÇALIŞIYOR — KISMEN AÇIYOR

Koruma açık ve bazı adresler açılıyor, ama bantta yazan adresler açılmıyor. Kullandığınız
uygulama o adreslere ihtiyaç duyuyorsa takılabilir (örneğin Discord giriş ya da güncelleme
ekranında kalır). Adımlar [ÇALIŞIYOR — AMA AÇMIYOR](#calisiyor-ama-acmiyor) ile aynı.

Uygulamanız sorunsuz çalışıyorsa bir şey yapmanız gerekmez.

<a id="beklenmedik-durus"></a>

### BEKLENMEDİK DURUŞ

Koruma çalışırken motor kendiliğinden kapandı; o andan itibaren korumasızsınız.

1. **"ZAPRET'İ BAŞLAT"** ile tekrar açın.
2. Tekrar kapanıyorsa antivirüsün müdahale edip etmediğine bakın ve ZapretTR klasörünü
   istisna listesine ekleyin.
3. Olmuyorsa bilgisayarı yeniden başlatın. Hâlâ oluyorsa raporu kaydedip bildirin.

<a id="duraklatildi"></a>

### DURAKLATILDI

Korumayı **"Duraklat"** ile siz kapattınız. Bu durumda winws, şifreli DNS ve ağ sürücüsü
kapalıdır; sistem DNS ayarınız ZapretTR'den önceki hâlindedir. Otomatik başlatma kuruluysa
bilgisayarı yeniden başlatsanız da kapalı kalır. Ayarlarınız silinmez.

Korumayı geri açmak için **"DEVAM ET"** düğmesine basın. Kaldığı yerden, aynı ayarla sürer.

**"TAM DURAKLATILAMADI"** yazıyorsa bir parça durdurulamamıştır; hangisi olduğu günlükte
`UYARI: duraklatmadan sonra hâlâ duranlar` satırında yazar. Bir kez daha "Duraklat"a basın.
Olmazsa "Tüm Ayarları Sıfırla" her şeyi temizler, ama bulunan ayarı da siler.

<a id="vpn-baglanmiyor"></a>

### VPN bağlanmıyor

ZapretTR'nin motoru (winws) ya da ağ sürücüsü devredeyken VPN'ler bağlanamayabilir. Proton
VPN ile ölçtük: koruma açıkken bağlantı her denemede zaman aşımına düştü; motor durup sürücü
çekirdekten düştükten bir saniye sonra bağlandı. Sebep DNS değil. Pencereyi X ile kapatmak
yetmez, çünkü pencere kapanınca koruma arka planda sürer.

**"Duraklat"** arkada çalışan her şeyi kapatır: winws ve şifreli DNS süreçleri, DNS
yönlendirmesi, ağ sürücüsü ve (kuruluysa) otomatik başlatma servisleri. Sonra geride bir şey
kalıp kalmadığını ölçüp günlüğe yazar; kaldıysa bant **"TAM DURAKLATILAMADI"** der.

1. VPN'e bağlanmadan önce ZapretTR'de **"Duraklat"** düğmesine basın.
2. VPN'le işiniz bitince VPN'i kapatın ve **"DEVAM ET"** deyin.

Duraklattıktan sonra da bağlanmıyorsa bant "TAM DURAKLATILAMADI" diyor mu, günlükte hangi
parçanın durdurulamadığı yazıyor mu, ona bakın. Hiçbir şey kalmadığı yazıyorsa sorun ZapretTR'de
değil; "Raporu Kaydet" dosyasıyla bildirin.

"Tüm Ayarları Sıfırla" ya da "Otomatik Başlatmayı Kaldır" kullanmanıza **gerek yok**; ikisi
de bulunan ayarı siler.

<a id="yeniden-baslatinca"></a>

### Yeniden başlatınca koruma kapalı geliyor

Bu bir hata değil: **"ZAPRET'İ BAŞLAT" korumayı yalnızca o oturum için açar.** Kalıcı olması
için **"Servis Olarak Yükle (Otomatik Başlat)"** düğmesine basın. Kurduktan sonra yeniden
başlatmanıza gerek yok; servis hemen çalışır ve sonraki her açılışta kendiliğinden devreye
girer.

---

## Servis ve kurulum

<a id="servis-durmus"></a>

### SERVİS DURMUŞ

Otomatik başlatma kurulu ama çalışmıyor; **koruma şu an kapalı.** En sık sebebi bir
güncellemeden sonra servisin yeniden başlatılamamış olması. Üç yol var:

- **"ZAPRET'İ BAŞLAT"** ile korumayı elle açın, ya da
- **"Otomatik Başlatmayı Kaldır"**, ardından **"Servis Olarak Yükle"** ile yeniden kurun, ya da
- bilgisayarı yeniden başlatın.

<a id="servis-islemi-basarisiz"></a>

### SERVİS İŞLEMİ BAŞARISIZ

Otomatik başlatma kurulurken ya da kaldırılırken bir adım başarısız oldu. Günlükte `[!]` ile
başlayan satır hangisi olduğunu söyler.

- Şifreli DNS ile ilgiliyse DNS ayarınız **yarım bırakılmaz**: çözümleyici cevap vermezse
  yönlendirme yapılmaz ve servis geri sökülür. Bir kez daha deneyin; olmazsa "Şifreli DNS
  kullan" kutusunun ve [BAŞLATILAMADI](#baslatilamadi) altındaki DNS maddesinin açıklamasına bakın.
- Diğer durumlarda bilgisayarı yeniden başlatıp tekrar deneyin, yine olmazsa raporu kaydedip
  bildirin.

<a id="windows-engelliyor"></a>

### Windows engelliyor (Defender, Akıllı Uygulama Denetimi, SmartScreen)

Kurulum paketi indirilir indirilmez siliniyor, çift tıklayınca hiç açılmıyor ya da uygulama
"antivirüs tarafından engellendi" / "Akıllı Uygulama Denetimi tarafından engellendi"
diyorsa sebep Windows'un korumalarından biri. **ZapretTR'in kod imzalama sertifikası yok**
ve içinde çekirdek modunda çalışan bir ağ sürücüsü (WinDivert) var; bu ikisi bir arada
Windows'un makine öğrenmesine dayalı korumalarında yanlış alarm üretebiliyor.

Önce **dosyanın gerçekten bizden geldiğini doğrulayın**: yayın sayfasındaki
`SHA256SUMS.txt` ile karşılaştırın ([nasıl](../README-DETAYLI.md#kolay-kullanım)). Özet
tutmuyorsa dosyayı çalıştırmayın ve silin.

Sonra ekranda gördüğünüz pencereye göre:

**0. İndirme bitmiyor, dosya "Unconfirmed ….crdownload" olarak kalıyor (tarayıcı).** Edge
*"ZapretTR-Setup-…exe yaygın olarak indirilmiyor"* (*isn't commonly downloaded*) der ve dosyayı
bekletir. Bu da bir virüs tespiti değil, itibar uyarısı: dosya yeni ve imzasız.
İndirilenler panelinde (Ctrl+J) dosyanın üzerine gelin → **"…" → Sakla (Keep) → Daha fazla
göster (Show more) → Yine de sakla (Keep anyway)**. Chrome'da da benzer bir uyarı çıkabilir.

**1. "Windows bilgisayarınızı korudu" (SmartScreen).** Bu bir engel değil, uyarı:
**"Ek bilgi" → "Yine de çalıştır"**. Dosyanın imzasız olmasından kaynaklanır.

**2. "Tehdit bulundu" / dosya indirilir indirilmez kayboldu (Microsoft Defender).**
Bildirimdeki ad çoğu zaman sonu `!cl` ile biten bir addır (ör. `Trojan:Win32/Tecabans.STV!cl`);
`!cl`, kararın Microsoft'un **bulut tabanlı tahmininden** geldiğini gösterir; imzasız ve az
indirilmiş dosyalarda yanlış alarm olarak sık görülüyor.

1. **Windows Güvenliği → Virüs ve tehdit koruması → Koruma geçmişi**'ni açın.
2. ZapretTR dosyasının kaydını açın, ayrıntıda dosya yolunun `ZapretTR-Setup-...exe` ya da
   `C:\Program Files\ZapretTR\...` olduğunu kontrol edin.
3. **Eylemler → İzin ver** (ya da "Cihazda izin ver") deyin. Dosya karantinadaysa
   **Geri yükle**.
4. Kurulum paketini yeniden indirip çalıştırın.

**3. "Akıllı Uygulama Denetimi bu uygulamayı engelledi" (Smart App Control).** Bu özellik
imzası olmayan ya da Microsoft'un tanımadığı uygulamaları **hiç çalıştırmaz** ve "yine de
çalıştır" seçeneği sunmaz; tek bir uygulama için istisna da tanımlanamaz.

- Tek yol özelliği kapatmak: **Windows Güvenliği → Uygulama ve tarayıcı denetimi →
  Akıllı Uygulama Denetimi ayarları → Kapalı**. Bu, bütün imzasız uygulamalara izin verir;
  kararı siz verin.
- 2026 başındaki Windows güncellemelerinden önce bu özellik bir kez kapatılınca Windows
  yeniden kurulmadan geri açılamıyordu. Güncel bir Windows 11'de aynı ekrandan yeniden
  açılabiliyor; sizinkinde bu seçenek gri görünüyorsa kapatmadan önce düşünün.
- Özellik "Değerlendirme" modundaysa Windows onu **kendiliğinden açabilir**; bu yüzden
  dün çalışan ZapretTR bugün engellenebilir.
- Kurumsal bir bilgisayarda aynı engel BT yöneticisinin uygulama denetimi ilkesinden
  gelebilir; o durumda yöneticinize danışın.

**4. Başka bir antivirüs** (Kaspersky, ESET, Avast...). Karantinasından ZapretTR
dosyalarını geri yükleyin ve `C:\Program Files\ZapretTR` klasörünü istisna listesine
ekleyin. Kaspersky, WinDivert sürücüsünü "RiskTool" (risk taşıyan araç) olarak işaretliyor.

Hangi pencereyi gördüğünüzü bilmiyorsanız ekran görüntüsüyle birlikte
[bildirin](#hicbiri-ise-yaramadiysa); hangi korumanın engellediğini bilmeden doğru yolu
söyleyemeyiz.

<a id="kurulum-dosyalari-eksik"></a>

### KURULUM DOSYALARI EKSİK

Uygulamanın çalışması için gereken dosyalardan bazıları yok. Neredeyse her zaman sebebi
**antivirüsün dosyaları karantinaya alması**: ZapretTR çekirdek modunda çalışan bir ağ
sürücüsü taşıyor ve bu sürücü sık sık yanlış alarm veriyor.

1. Antivirüsün **karantina** bölümünü açın; ZapretTR dosyalarını geri yükleyin.
2. `C:\Program Files\ZapretTR` klasörünü **istisna listesine** ekleyin.
3. Kurulum paketini (`ZapretTR-Setup-<sürüm>.exe`) **yeniden çalıştırın**.

Microsoft Defender dahil hiçbir antivirüs için "işaretlemez" garantisi veremiyoruz: bulut
tabanlı kararlar gün gün değişiyor. Adım adım yol: [Windows engelliyor](#windows-engelliyor).
Dosyanın gerçekten bizden geldiğinden emin olmak için ayrıntılı belgedeki
[SHA256 doğrulamasını](../README-DETAYLI.md#kolay-kullanım) yapın.

<a id="uygulama-acilmiyor"></a>

### Uygulama açılmıyor ya da çöktü

- **"ZapretTR zaten çalışıyor"** penceresi çıkıyorsa uygulama zaten açık, sadece penceresi
  gizli. Saatin yanındaki bildirim alanında simgesine çift tıklayın. (Pencerenin X düğmesi
  uygulamayı kapatmaz, oraya indirir.)
- **"yonetici yetkisiyle calismali"** diyorsa sağ tık → "Yönetici olarak çalıştır".
- **"Beklenmedik bir hatayla karşılaştı"** penceresi çıktıysa ayrıntı
  `C:\ProgramData\ZapretTR\cokme.log` dosyasına yazıldı. Bu dosyayı hata bildirimine ekleyin.

---

## İnternet ve DNS

<a id="internet-gitti"></a>

### İnternet tamamen gitti

Siteler hiç açılmıyor, "DNS adresi bulunamadı" gibi hatalar alıyorsanız sistem DNS'i hâlâ
şifreli DNS'e yönlendirilmiş ama o çalışmıyor olabilir. Uygulama bunu açılışta kendisi
düzeltmeye çalışır; önce **ZapretTR'yi bir kez açıp kapatın**. Olmazsa:

1. Yönetici olarak açtığınız PowerShell'de şunu çalıştırın. Servisleri kaldırır ve DNS
   ayarınızı geri alır:
   ```powershell
   & "$env:ProgramFiles\ZapretTR\ZapretTR.exe" --uninstall-services
   ```
2. O da olmazsa elle geri alın: **Denetim Masası → Ağ Bağlantıları → bağlantınız →
   Özellikler → "İnternet Protokolü Sürüm 4 (TCP/IPv4)" → "DNS sunucu adresini otomatik al"**.
3. Programı **Ekle/Kaldır** üzerinden kaldırmak da aynı temizliği yapar.

### Güncelleme başarısız

"Güncelleme başarısız" yazıyorsa büyük ihtimalle GitHub'a o an ulaşılamadı ya da indirilen
dosyanın özeti tutmadı (bu durumda dosya **çalıştırılmaz**). Bir süre sonra tekrar deneyin ya da
yeni sürümü [Releases](https://github.com/superuser-d0/zapret-tr/releases/latest) sayfasından
elle indirip kurun; ayarlarınız korunur.

---

<a id="hicbiri-ise-yaramadiysa"></a>

## Hiçbiri işe yaramadıysa

**1. Her şeyi sıfırlayın.** **"Tüm Ayarları Sıfırla"** motoru ve şifreli DNS'i durdurur,
ZapretTR'nin servislerini ve ağ sürücüsünü kaldırır, kayıtlı ayarları ve öğrenilmiş
sonuçları siler. ZapretTR sistem DNS ayarınızı değiştirdiyse ayar eski hâline döner; kendi
yaptığınız DNS ayarına dokunulmaz. Sonra:

1. Bilgisayarı **yeniden başlatın**.
2. Uygulamayı açın, **"PARAMETRE TESTİ YAP"**.

**2. Bize bildirin.** Sırayla:

1. **"Raporu Kaydet"**: masaüstüne bir `.txt` dosyası yazar. İçinde uygulamanın günlüğü,
   yönetici yetkisi, dosyaların tamlığı, servislerin ve şifreli DNS'in durumu, sistem DNS'inin
   kimde olduğu ve çakışan araçlar var. **Genel IP adresiniz yazmaz**; göndermeden önce açıp
   okuyabilirsiniz.
2. **"Hata Bildir"**: GitHub'daki bildirim formunu sürüm, sağlayıcı ve günlüğün son
   satırlarıyla doldurulmuş olarak tarayıcıda açar. Kendiliğinden hiçbir şey göndermez.
3. Kaydettiğiniz rapor dosyasını forma sürükleyip bırakın ve gönderin (GitHub hesabı gerekir).

"Çalışmıyor" cümlesi tek başına teşhis edilemiyor; rapor dosyası ediliyor. Parametre
bulunamayan hatlardan gelen raporlar ayrıca en değerli katkı: yeni hatlar için aday
eklemenin tek yolu bu.
