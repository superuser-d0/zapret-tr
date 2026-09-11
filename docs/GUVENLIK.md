# Güvenlik ve antivirüs

ZapretTR imzasız dağıtılıyor ve çekirdek modunda çalışan bir paket yakalama sürücüsü
taşıyor. Bu ikisi bir arada olduğunda antivirüs uyarısı **istisna değil, beklenen
durum**. Bu belge o uyarının ne anlama geldiğini, neyin gerçekten dağıtıldığını,
bunun nereden geldiğini ve uyarı aldığınızda **ne yapmanız gerektiğini** anlatıyor.

Kısa cevabı baştan verelim, çünkü en çok sorulan soru bu:

> **Antivirüsünüzü kapatmayın.** Gerekli değil, ve kapatmak sizi ZapretTR'in
> yaratmadığı risklere açar. Doğru çözüm, kurulum klasörü için **istisna** tanımlamak.
> Nasıl yapılacağı [aşağıda ürün ürün yazılı](#7-ürün-ürün-istisna-adımları).

---

## 1. Kutunun içinde ne var

Sürüm **0.1.18** için dağıtılan her çalıştırılabilir dosya. Özetler indirilen
gerçek yayın dosyalarından hesaplandı.

| Dosya | Ne yapıyor | SHA-256 | İmza |
|---|---|---|---|
| `ZapretTR-Setup-0.1.18.exe` | Kurulum paketi (Inno Setup 6) | `810389820c0795bdf7fd2c930ae7df295e351ecc0d29e0b6dccd1e826d1d85fb` | **yok** |
| `zapret-tr-saha-testi.zip` | Ölçüm paketi (son kullanıcı için değil) | `63bd7c980c346544beb896c7f01f064c871e51d28fdceca8388d263513a633bc` | — |
| `winws.exe` | DPI atlatma motoru (zapret) | `2da71e80878dc270ac83f5893ecbb841f9752a57f1da8ff9325636b4346bc632` | **yok** |
| `WinDivert64.sys` | Çekirdek modu paket yakalama sürücüsü | `8da085332782708d8767bcace5327a6ec7283c17cfb85e40b03cd2323a90ddc2` | **var** (çift) |
| `WinDivert.dll` | Sürücünün kullanıcı modu arayüzü | `16abd6a029e65557c6a309bea7b13bf81fff4e193567582e1cddbf6719f323e0` | **yok** |
| `cygwin1.dll` | `winws.exe`'nin çalışma zamanı | `103104a52e5293ce418944725df19e2bf81ad9269b9a120d71d39028e821499b` | **yok** |
| `dnscrypt-proxy.exe` | Şifreli DNS çözümleyici | `d847f834aef02f8705a649dc1060f520cdb7931d7361035728770dce2c16eeb6` | **yok** |
| `zapret-tr-test.exe` | Ölçüm aracı (saha paketi içinde) | `e2b970fce99b9bcde185106239370acf5868aeae092255e69d69ed27f7405503` | **yok** |
| `msquic.dll` | QUIC ölçümü için | `24da0bf4d7b7e0adb598297d170fa547f94f398bd6f2b515ae87f0a207c999c7` | **var** (Microsoft) |

**Listede eksik olan bir dosya var ve bunu söylemek zorundayız:** kurulum paketinin
içindeki `ZapretTR.exe` (uygulamanın kendisi). Kurulum paketi bir bütün olarak
`SHA256SUMS.txt` ile doğrulanabiliyor, ama içinden çıkan dosyaların ayrı özetleri
yayınlanmıyor. Antivirüsünüz `ZapretTR.exe`'yi işaretlerse elinizde onu bizim
yayınımızla eşleştirecek bir referans yok. Bu bir eksik ve
[yol haritasında](../README.md#yol-haritası) açık duruyor.

---

## 2. Bunlar gerçekten geldiklerini söyledikleri yerden mi geliyor

ZapretTR, `winws` motorunu ve DNS çözümleyicisini kendisi yazmıyor; dışarıdan
alıyor. Dolayısıyla "ZapretTR'e güvenmek" tek başına yetmez — **o dosyaların
yolda değiştirilmediğini** de bilmek gerekir.

`tools/upstream-manifest.json` her satıcı dosyasının özetini sabitliyor ve
`tools/fetch-upstream.ps1` indirme sonrası doğruluyor. Ama bu kendi kendini
doğrulamaktan ibaret: manifest de aynı betikle üretiliyor. Gerçek soru, o
dosyaların **kaynağındaki resmî dağıtımla** aynı olup olmadığı.

**2026-09-11'de bağımsız olarak karşılaştırıldı:**

| Karşılaştırma | Sonuç |
|---|---|
| Yayın dosyaları ↔ `SHA256SUMS.txt` ↔ GitHub'ın kendi özeti | **üçü de aynı** |
| `winws.exe`, `cygwin1.dll`, `WinDivert.dll`, `WinDivert64.sys` ↔ `bol-van/zapret-win-bundle` @ `32fbbeb` | **birebir aynı** |
| `WinDivert64.sys` ↔ resmî `basil00/WinDivert` v2.2.2-A | **birebir aynı** |
| `WinDivert.dll` ↔ resmî `basil00/WinDivert` v2.2.2-A | **2 bayt fark** — aşağıda |
| `dnscrypt-proxy.exe` ↔ resmî `DNSCrypt/dnscrypt-proxy` 2.1.18 win64 | **birebir aynı** |

Yani zincirin tamamı kaynağına kadar takip edildi ve tek bir yerde fark çıktı.

### `WinDivert.dll`'deki iki bayt

Dağıttığımız `WinDivert.dll`, resmî WinDivert 2.2.2-A sürümünden **tam olarak iki
baytta** ayrılıyor. İkisi de PE başlığında, kod bölümlerinde **tek bayt fark yok**:

| Alan | Resmî | Bizdeki | Anlamı |
|---|---|---|---|
| `DllCharacteristics` | `0x0000` | `0x0040` | `DYNAMIC_BASE` — **ASLR açık** |
| `CheckSum` | `0x0000fc9c` | `0x0000fcdc` | Yukarıdaki değişiklik için yeniden hesaplanmış |

ASLR (adres alanı düzeni rastgeleleştirme) bir **sertleştirme** özelliği: kütüphanenin
belleğe her seferinde farklı adrese yüklenmesini sağlar ve sömürü yazmayı zorlaştırır.
Yani bu fark güvenliği azaltan değil, artıran bir değişiklik. Sağlaması da içeriden
tutarlı: sağlama toplamındaki fark (`0x40`) bayrak alanındaki farkla birebir aynı,
yani başka hiçbir bayt değişmemiş.

Bu değişikliği **biz yapmadık**; dosya `bol-van/zapret-win-bundle` deposundan bu
haliyle geliyor. Kaydetmemizin sebebi basit: "resmî sürümle aynı değil" cümlesi tek
başına bırakıldığında korkutucu, açıldığında değil.

---

## 3. İmzalar: sürücü kimin adına imzalı

`WinDivert64.sys` **çift imzalı**, ve bu teknik bir zorunluluk: Windows 10/11
imzasız bir çekirdek sürücüsünü yüklemez.

- **Sectigo EV kod imzalama** — sahibi: `成都密思听科技有限公司`
  (Chengdu Misiting Technology Co., Ltd.), Sichuan, Çin.
  Kurumsal seri no `91510107MA7E8Y2876`.
- **Microsoft Windows Hardware Compatibility Publisher** — Microsoft'un attestation
  imzası. Sürücünün Windows'ta yüklenebilmesini sağlayan şey bu.

Sürücünün, WinDivert'in yazarı değil de Çin merkezli bir şirket adına imzalı olması
ilk bakışta rahatsız edici ve öyle görünmesi doğru. Kaydettiğimiz nokta şu:
**bu bizim ya da bol-van'ın eklediği bir şey değil.** Dosya, `basil00/WinDivert`
projesinin resmî v2.2.2-A yayınındaki dosyayla **bit bit aynı** — imza da oradan
geliyor. WinDivert'i hangi yoldan kurarsanız kurun (GoodbyeDPI, zapret, doğrudan
resmî yayın) aynı imzalı sürücüyü almış oluyorsunuz.

`msquic.dll` Microsoft imzalı. **Geri kalan her şey imzasız**, kurulum paketi ve
uygulamanın kendisi dahil — kod imzalama sertifikamız yok. SmartScreen'in
"bilinmeyen yayımcı" uyarısı buradan geliyor ve doğru bir uyarı.

---

## 4. VirusTotal durumu

**Henüz yayınlanmış bir VirusTotal ölçümümüz yok.** Bu belgeyi hazırlarken tarama
yapılmak istendi ama çalıştığımız ortamın ağ politikası `virustotal.com`'a çıkışı
engelledi. Sonuç uydurmak yerine boş bırakıyoruz: bu depoda ölçülmemiş bir şeye
"ölçüldü" demiyoruz.

Taramayı **herkes çalıştırabilir**, iki yoldan:

**a) Depo sahibiyseniz — otomatik.** `.github/workflows/virustotal.yml` iş akışı
yayındaki bütün ikilileri (kurulum paketi + içindeki satıcı dosyaları) tarayıp
sonucu iş özetine markdown tablo olarak yazıyor. Tek gereken
[ücretsiz bir VirusTotal anahtarı](https://www.virustotal.com/gui/my-apikey):

```
Settings → Secrets and variables → Actions → New repository secret
  Ad:    VT_API_KEY
  Değer: <anahtarınız>
```

Sonra Actions sekmesinden `virustotal` iş akışını elle çalıştırın. Her yeni yayında
da kendiliğinden koşuyor. Elle çalıştırılabilir olması kasıtlı: bir motorun yeni
imzası, **ikili hiç değişmeden** aylar sonra tespit üretebiliyor.

**b) Kullanıcıysanız — tek tıkla.** VirusTotal'da bir dosyayı özetinden sorgulamak
için hesap gerekmiyor. Aşağıdaki bağlantılar yukarıdaki tablodaki özetlere gidiyor;
"Not found" çıkması dosyanın temiz olduğu anlamına **gelmez**, sadece o dosyayı
henüz kimsenin yüklemediğini gösterir.

- [`ZapretTR-Setup-0.1.18.exe`](https://www.virustotal.com/gui/file/810389820c0795bdf7fd2c930ae7df295e351ecc0d29e0b6dccd1e826d1d85fb)
- [`winws.exe`](https://www.virustotal.com/gui/file/2da71e80878dc270ac83f5893ecbb841f9752a57f1da8ff9325636b4346bc632)
- [`WinDivert64.sys`](https://www.virustotal.com/gui/file/8da085332782708d8767bcace5327a6ec7283c17cfb85e40b03cd2323a90ddc2)
- [`WinDivert.dll`](https://www.virustotal.com/gui/file/16abd6a029e65557c6a309bea7b13bf81fff4e193567582e1cddbf6719f323e0)
- [`dnscrypt-proxy.exe`](https://www.virustotal.com/gui/file/d847f834aef02f8705a649dc1060f520cdb7931d7361035728770dce2c16eeb6)
- [`cygwin1.dll`](https://www.virustotal.com/gui/file/103104a52e5293ce418944725df19e2bf81ad9269b9a120d71d39028e821499b)
- [`zapret-tr-test.exe`](https://www.virustotal.com/gui/file/e2b970fce99b9bcde185106239370acf5868aeae092255e69d69ed27f7405503)

İndirdiğiniz dosyanın özetini kendiniz hesaplayın ve yukarıdakiyle karşılaştırın —
eşleşmiyorsa dosya bizim yayınımızdan gelmiyor demektir:

```powershell
Get-FileHash .\ZapretTR-Setup-0.1.18.exe -Algorithm SHA256
```

### Ölçülmüş tek antivirüs sonucu

**Windows Defender: sıfır tespit.** (2026-09-07, motor `1.1.26080.3`, imza
`1.459.93.0`; kurulum paketi, saha paketi ve kurulu dizin tarandı, gerçek zamanlı
koruma açıktı.) Diğer ürünler için elimizde ölçüm yok.

---

## 5. Antivirüsler bu paketi neden işaretler

Bir tespit çıkarsa bunun neredeyse kesin sebebi aşağıdakilerden biri. Hiçbiri
"yanlış alarm" diye geçiştirilecek şey değil — hepsi ZapretTR'in **gerçekten
yaptığı** işler:

1. **WinDivert bir paket yakalama sürücüsüdür.** Çekirdek modunda çalışır ve ağ
   trafiğini araya girip değiştirir. Bu tam olarak bir rootkit'in de yaptığı iş.
   Antivirüsler bu sürücüyü genellikle *zararlı* değil, **"hacking tool" / "riskware"**
   ailesine koyar — yani "bu kötü niyetli değil ama kötüye kullanılabilir" kutusu.

2. **İmzasız ve az görülen dosya.** Norton ve McAfee gibi ürünlerin itibar motorları
   "imzasız + dünyada az sayıda kurulum" bileşimini **tek başına** tespit sebebi
   sayar. Dosyanın içeriğine bakmadan verilen bir karardır ve yeni sürüm çıktıkça
   her seferinde tekrarlanır.

3. **Yönetici yetkisi + Windows servisi kurma.** Uygulamanın manifesti
   `requireAdministrator`, ve "Servis Olarak Yükle" gerçekten bir sistem servisi
   kuruyor. Davranış motorları için bu güçlü bir sinyal.

4. **Sistem DNS'ini `127.0.0.1`'e çeviriyor.** Şifreli DNS için gerekli ve her
   çıkışta geri alınıyor — ama davranış olarak bir **DNS hijacker** ile birebir
   aynı. Niyetin iyi olması yaptığı şeyi değiştirmiyor, motorlar da niyet okumuyor.

5. **.NET tek dosya paketi.** `ZapretTR.exe` ve `zapret-tr-test.exe` kendi
   çalışma zamanını içinde taşıyan, sıkıştırılmış tek dosya paketleri. Sezgisel
   motorlar için "çalışırken kendini açan paketleyici" görünümü veriyor.

6. **`cygwin1.dll`.** `winws.exe` Cygwin ile derlendiği için zorunlu. Eski ve
   geçmişte kötü amaçlı yazılımlarda da kullanılmış yaygın bir çalışma zamanı.

Beklenen tespit adları — **bunlar ölçüm değil, sektörde bilinen örüntü**, ilk
VirusTotal koşumu yapıldığında bu bölüm gerçek sonuçlarla değiştirilecek:
`HackTool:Win32/WinDivert`, `RiskTool.Win64.WinDivert`, `not-a-virus:NetTool.*`,
`Riskware/WinDivert`, `PUA.WinDivert`, `WS.Reputation.1`, `Artemis!<hash>`.
`HackTool` / `RiskTool` / `PUA` / `not-a-virus` önekleri "zararlı yazılım bulundu"
demek **değildir**; "bu bir araç ve kötüye kullanılabilir" demektir.

---

## 6. Antivirüsünüzü kapatmayın

En çok verilen tavsiye bu ve **yanlış**. Üç sebeple:

**Gerekli değil.** ZapretTR'in ihtiyacı olan şey antivirüsün kapalı olması değil,
kendi dosyalarına dokunulmaması. Bunun aracı istisna listesi.

**Sizi asıl riske o açar.** Antivirüsü kapattığınız süre boyunca sistem
ZapretTR'le hiç ilgisi olmayan her şeye karşı da savunmasız. İnternette gezinirken
bir engelleme atlatma aracını kurmak için korumayı tamamen indirmek, çözdüğünden
büyük bir sorun yaratır.

**Zaten işe yaramıyor.** Antivirüsü kapatıp kurulumu tamamlarsanız, tekrar
açtığınızda ürün diski tarar ve dosyaları **kurulumdan sonra** karantinaya alır.
Elinizde kalan şey kapalı bir antivirüs değil, yarım kalmış bir kurulum olur —
üstelik en kötü anda: [aşağıya bakın](#8-antivirüs-dosyayı-karantinaya-aldıysa).

İstisna tanımlamak da bedava değil, bunu da açıkça söyleyelim: o klasörün içindeki
her şey artık taranmıyor. Bu yüzden istisnayı **sadece kurulum klasörüne** verin,
`C:\` ya da indirilenler klasörüne değil; ve ZapretTR'i kaldırdığınızda
[istisnayı da kaldırın](#10-kaldırdıktan-sonra).

---

## 7. Ürün ürün istisna adımları

Önce hangi yolları ekleyeceğinizi bilin. **Dördü de gerekli:**

| Ne | Yol |
|---|---|
| Kurulum klasörü | `C:\Program Files\ZapretTR` |
| Veri klasörü | `C:\ProgramData\ZapretTR` |
| Süreçler | `ZapretTR.exe`, `winws.exe`, `dnscrypt-proxy.exe` |
| Sürücü | `C:\Program Files\ZapretTR\zapret-winws\WinDivert64.sys` |

**İstisnayı kurulumdan ÖNCE ekleyin.** Kurulum sırasında dosya karantinaya
alınırsa kurulum yarım kalır ve arkasında toparlanması gereken bir sistem bırakır.

### Windows Defender

Ayarlar → **Gizlilik ve güvenlik** → **Windows Güvenliği** → **Virüs ve tehdit
koruması** → *Virüs ve tehdit koruması ayarları* altında **Ayarları yönet** →
aşağıda **Dışlamalar** → **Dışlama ekle veya kaldır** → **Dışlama ekle** →
**Klasör**.

Defender bu paketi işaretlemiyor (ölçüldü), yani muhtemelen hiçbir şey yapmanıza
gerek yok. Yine de sorun çıkarsa bakılacak iki ayar var:

- **Denetimli klasör erişimi** (fidye yazılımı koruması) açıksa uygulamanın
  `%ProgramData%` altına yazmasını engelleyebilir. Aynı sayfada.
- **İstenmeyebilecek uygulama engelleme** (PUA koruması) `winws.exe`'yi *HackTool*
  ailesinden yakalayabilir. Windows Güvenliği → **Uygulama ve tarayıcı denetimi** →
  *İtibara dayalı koruma* altında **İtibara dayalı koruma ayarları** →
  **İstenmeyebilecek uygulama engelleme**.

### Avast / AVG

(İkisi aynı motoru kullanıyor, adımlar da aynı.)

**Menü** → **Ayarlar** → **Genel** → **İstisnalar** → **İstisna ekle** → klasör
yolunu yazın.

Bu ürünlerde dikkat edilecek ayrı bir şey var: **CyberCapture** ve **Sertleştirilmiş
mod**. İkisi de "imzasız ve nadir" dosyaları içeriğine bakmadan bulut analizine
gönderip bu arada çalışmasını engelliyor — tipik sonucu `FileRepMalware` ya da
`Win64:Evo-gen [Susp]`. Genel istisna bunu çoğu zaman çözüyor; çözmezse
**Koruma → Çekirdek Kalkanları** altında ayrıca istisna gerekiyor.

### ESET

**Gelişmiş kurulum** (F5) → **Algılama motoru** → **Dışlamalar** →
**Performans dışlamaları** → yol ekleyin.

ESET'te asıl anahtar ayrı bir yerde: aynı **Algılama motoru** sayfasında
**"Potansiyel olarak güvenli olmayan uygulamaların algılanmasını etkinleştir"**
seçeneği. WinDivert tam olarak bu kategoride (`Win64/HackTool.WinDivert` tarzı bir
adla) yakalanır. Bu seçenek açıksa istisna yazmadan sürücü her açılışta silinebilir.

### Kaspersky

**Ayarlar** → **Güvenlik Ayarları** → **Tehditler ve Dışlamalar** →
**Dışlamaları yönet** → **Ekle**.

Kaspersky'de ikinci bir adım var ve atlanırsa istisna işe yaramaz: aynı sayfada
**"Algılanacak nesne türleri"** listesinde **"Diğer yazılımlar"** (kötü amaçlı
olmayan ama saldırganlarca kullanılabilen programlar) kutusu işaretliyse WinDivert
`not-a-virus:` önekiyle yakalanmaya devam eder. Ayrıca **Güvenilir uygulamalar**
listesine `winws.exe` ve `dnscrypt-proxy.exe`'yi eklemek gerekebilir.

### Bitdefender

**Koruma** → **Antivirüs** → **Aç** → **Ayarlar** → **İstisnaları yönet** →
**İstisna ekle**.

Bitdefender'da tek istisna yetmiyor: **Advanced Threat Defense** (davranış
izleyici) ayrı çalışıyor ve `winws.exe`'yi çalışma anında yakalayabiliyor.
İstisna eklerken hem **Antivirüs** hem **Advanced Threat Defense** kutularını
işaretleyin. Online Threat Prevention da DNS değişikliğine karışabiliyor.

### Malwarebytes

**Ayarlar** → **İzin Verilenler Listesi** → **Ekle** → **Dosya veya klasör**.

Malwarebytes WinDivert'i genellikle `RiskWare.WinDivert` olarak yakalar.
**Ayarlar → Koruma → Potansiyel istenmeyen değişiklikler (PUM)** ayarı, DNS
değişikliğini ayrıca engelleyebiliyor.

### Norton

**Ayarlar** → **Antivirüs** → **Taramalar ve Riskler** → **Taramalardan Hariç
Tutulacak Öğeler** *ve ayrıca* **Otomatik Koruma, SONAR ve Download Intelligence
tarafından Algılanmayacak Öğeler** — **ikisine de** eklemek gerekiyor.

Norton'un `WS.Reputation.1` tespiti dosyanın içeriğiyle değil, **dünyadaki kurulum
sayısıyla** ilgili. Yeni bir sürüm her çıktığında tekrar edecek; dosya değiştiği
için istisna da yenilenmesi gerekebilir. Bunu bitiren tek şey kod imzalama
sertifikasıdır, bizde henüz yok.

### McAfee

**Ayarlar** → **Gerçek Zamanlı Tarama** → **Hariç tutulan dosyalar** → **Dosya ekle**.

McAfee'nin `Artemis!<hash>` tespiti de Norton'unki gibi bulut itibarına dayanıyor
ve aynı sebeple her yeni sürümde tekrarlayabiliyor.

---

## 8. Antivirüs dosyayı karantinaya aldıysa

En kötü senaryo, bir dosyanın **kurulumdan sonra** ve **koruma açıkken**
alınmasıdır. Özellikle `dnscrypt-proxy.exe`: şifreli DNS açıkken sistem DNS'i
`127.0.0.1`'i gösteriyor, ve o adresi dinleyen süreç karantinaya giderse
**makine hiçbir adı çözemez.** Belirtisi "internet gitti" olur; oysa bağlantı
duruyor, ad çözümlemesi ölüyor.

Toparlama, sırayla:

**1. Önce DNS'i geri alın.** Yönetici olarak PowerShell:

```powershell
& "$env:ProgramFiles\ZapretTR\ZapretTR.exe" --uninstall-services
```

Bu komut servisleri söker ve sistem DNS ayarını diskteki yedekten geri yükler.
`ZapretTR.exe`'nin kendisi de karantinadaysa Program Ekle/Kaldır üzerinden kaldırma
aynı temizliği yapıyor. Saha paketini kullanıyorsanız karşılığı
`zapret-tr-test.exe --cleanup`.

**2. Antivirüs karantinasından dosyayı geri alın** ve aynı işlemde istisna
tanımlayın. Bütün ürünlerde karantina ekranında "geri yükle ve izin ver" benzeri
bir seçenek var — sadece "geri yükle" derseniz dosya birkaç dakika içinde tekrar
alınır.

**3. Kurulumu üzerine tekrar çalıştırın.** Kurulum, eksik dosyaları yerine koyar
ve önceki ayarlarınızı korur.

**4. Sürücü çekirdekte asılı kaldıysa** (belirtisi: parametre testinde **bütün**
adayların aynı şekilde başarısız olması) bilgisayarı bir kez yeniden başlatın.
Kalıntı bir WinDivert servisi, ölçümün tamamını sessizce geçersiz kılar.

---

## 9. Bize güvenmek zorunda değilsiniz

Bu belgedeki her şeyi kendiniz doğrulayabilirsiniz ve bunu yapmanızı tavsiye
ediyoruz:

- **Özetleri karşılaştırın.** Yukarıdaki tablo ile `Get-FileHash` çıktısı.
- **Kaynaktan derleyin.** Depo bunun için yeterli: `dotnet build`, ardından
  `tools/fetch-upstream.ps1` satıcı ikililerini sabitlenmiş sürümden indirip
  özetlerini doğruluyor. Bu yol imzasız ikiliye güvenme sorununu tamamen ortadan
  kaldırır.
- **Satıcı ikililerini kaynağında doğrulayın.** `WinDivert64.sys` için referans
  `basil00/WinDivert` v2.2.2-A yayını; `dnscrypt-proxy.exe` için
  `DNSCrypt/dnscrypt-proxy` 2.1.18 win64 yayını. İkisi de bizdekiyle birebir aynı
  olmalı — 2026-09-11'de öyleydi.
- **Ne yaptığını izleyin.** Uygulamanın dışarıya yaptığı tek kendiliğinden istek
  açılıştaki güncelleme kontrolü (GitHub'a "en son sürüm ne"), ve ayarlardan
  kapatılabiliyor. Ölçüm sonuçları ve raporlar yalnızca diske yazılır. Bunu
  Process Monitor ya da Wireshark ile doğrulayabilirsiniz.

---

## 10. Kaldırdıktan sonra

ZapretTR'i kaldırdığınızda **istisnaları da kaldırın.** Geride kalan bir klasör
istisnası, o yola başka bir şey yazan herhangi bir programın taranmamasına yol
açar. Kaldırma işlemi klasörü siler ama antivirüsünüzün istisna listesine
dokunamaz — o sizde kalır.

Aynı şekilde, kapattığınız bir koruma bileşenini (PUA koruması, ESET'in
"güvenli olmayan uygulamalar" algılaması, Bitdefender'ın Advanced Threat Defense'i)
**geri açın**.

---

## 11. Antivirüsünüz işaretlediyse bize bildirin

Bu belgedeki tespit adları henüz büyük ölçüde tahmin. Gerçek bir bildirim, onları
ölçüme çeviren tek şey. Uygulamadaki **"Hata Bildir"** düğmesi formu doldurulmuş
açıyor, ya da doğrudan
[konu açabilirsiniz](https://github.com/superuser-d0/zapret-tr/issues/new/choose).

Yazarken şunlar işimize yarıyor:

- Antivirüs **ürünü ve sürümü**
- Tespitin **tam adı** (`HackTool:Win32/...` gibi)
- **Hangi dosya** işaretlendi (kurulum paketi mi, `winws.exe` mi, sürücü mü)
- Ne zaman oldu: indirirken, kurulum sırasında, yoksa kurulduktan sonra mı
- İndirdiğiniz dosyanın SHA-256 özeti

Tespit gerçekse ve bir false-positive ise, ürünün üreticisine örnek göndermek
genellikle birkaç gün içinde düzeltiliyor — ve bunu **kullanıcı olarak siz**
yaptığınızda daha hızlı işliyor.
