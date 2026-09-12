# Üçüncü Taraf Bileşenler / Third-Party Notices

ZapretTR bir **türev üründür**. DPI atlatma işini kendisi yapmaz; bu işi
[bol-van/zapret](https://github.com/bol-van/zapret) projesinin `winws` motoru yapar.
ZapretTR yalnızca o motoru yöneten bir arayüz ve otomatik parametre bulma katmanıdır.

---

## zapret / winws

- **Kaynak:** https://github.com/bol-van/zapret
- **İkili dağıtım:** https://github.com/bol-van/zapret-win-bundle
- **Yazar:** bol-van
- **Lisans:** MIT

`vendor/zapret-winws/` altındaki `winws.exe` ve yardımcı dosyalar bu projeye aittir ve
değiştirilmeden dağıtılır. Bu dosyalar depoya işlenmez; `tools/fetch-upstream.ps1`
tarafından sabitlenmiş bir commit'ten indirilir ve SHA256 ile doğrulanır.

MIT lisansının tam metni indirme sırasında `vendor/zapret-winws/LICENSE.upstream.txt`
olarak yanına konur.

## WinDivert

- **Kaynak:** https://github.com/basil00/Divert
- **Yazar:** Basil Fierz / basil00
- **Lisans:** LGPL v3 (ayrıca ticari lisans seçeneği mevcuttur)

`WinDivert.dll` ve `WinDivert64.sys` çekirdek modunda çalışan bir paket yakalama
sürücüsüdür. zapret-win-bundle içinde dağıtıldığı haliyle, **değiştirilmeden** kullanılır.
LGPL şartı gereği: bu bileşen değiştirilmemiştir ve kullanıcı isterse kendi derlediği
sürümüyle değiştirebilir — `vendor/zapret-winws/` altındaki dosyaları elle değiştirmek
yeterlidir, ZapretTR bu dosyaları yeniden imzalamaz veya kilitlemez.

## Cygwin

- **Kaynak:** https://cygwin.com (kaynak kodu: https://cygwin.com/git/newlib-cygwin.git)
- **Yazar:** Cygwin geliştiricileri
- **Lisans:** LGPL v3

`winws.exe` Cygwin ile derlendiği için `cygwin1.dll` olmadan başlamaz. Bu DLL
zapret-win-bundle içinde dağıtıldığı haliyle, **değiştirilmeden** `vendor/zapret-winws/`
altına konur. WinDivert'te olduğu gibi kullanıcı onu kendi derlediği uyumlu bir sürümle
değiştirebilir.

## dnscrypt-proxy

- **Kaynak:** https://github.com/DNSCrypt/dnscrypt-proxy
- **Yazar:** Frank Denis ([@jedisct1](https://github.com/jedisct1))
- **Lisans:** ISC

İsteğe bağlı şifreli DNS için kullanılır. `tools/fetch-upstream.ps1` resmi yayın
arşivini sabitlenmiş bir sürümden indirir, SHA256 ile doğrular ve yalnızca
`dnscrypt-proxy.exe` ile `LICENSE` dosyasını çıkarır; ikisi de **değiştirilmeden**
kurulumla birlikte `dnscrypt-proxy/` klasörüne konur. ISC lisansının istediği telif
bildirimi o `LICENSE` dosyasındadır.

## dnscrypt-resolvers

- **Kaynak:** https://github.com/DNSCrypt/dnscrypt-resolvers
- **Yazar:** DNSCrypt projesi

`dnscrypt-proxy`'nin bağlandığı genel çözümleyici listesi (`public-resolvers.md`).
Bu liste **bizimle dağıtılmaz**; `dnscrypt-proxy` çalışırken doğrudan kaynağından
indirir ve projenin minisign imzasıyla doğrular.

---

## Zapret Win TR

- **Kaynak:** https://github.com/alimali54/zapret-win-turkey
- **Yazar:** Ali Mali ([@alimali54](https://github.com/alimali54))
- **Ne aldık:** doğrudan kod değil — iki şey öğrendik ve kendi kodumuzda uyguladık.

Projenin geliştiricisi, aracındaki hazır ISS stratejilerini kullanmamıza izin verdi
ve `.au3` kaynağını paylaştı. Karşılaştırdık: klasik winws motoru için tanımladığı
sekiz stratejinin **sekizi de bizim profillerimizde zaten vardı** ve altısında onun
tercihi bizim ilk adayımızla aynıydı. Yani buradan kopyalanan bir strateji yok;
birbirinden bağımsız iki proje aynı değerlere varmış. Bu örtüşmeyi `docs/DEVAM.md`
içinde kayıt altına aldık, çünkü o profillerin tohumlarına duyulacak güveni
etkiliyor.

Kaynağından **öğrendiğimiz** ve uyguladığımız iki şey:

1. WinDivert sürücüsünün yalnızca `windivert` adıyla değil, `WinDivert14` ve
   `monkey` adlarıyla da kalabildiği. Temizliğimiz eskiden yalnızca ilkini
   söküyordu.
2. Aynı anda çalışan başka bir DPI atlatma aracının (özellikle GoodbyeDPI)
   ölçümü tamamen geçersiz kıldığı ve bunun kullanıcıya söylenmesi gerektiği.

Bunlar fikir düzeyinde katkılar; uygulama bize ait. Yine de kaynağı belirtmek
doğru olur.

## Parametre verisi hakkında not

`profiles/isp/*.json` içindeki başlangıç parametre adayları, Türkiye'deki kullanıcı
topluluğunda kamuya açık olarak paylaşılan yapılandırma dizgilerinden derlenmiştir
(forum başlıkları ve açık kaynak araçların belgeleri). Bunlar **kod değil, yapılandırma
verisidir** ve hiçbir üçüncü taraf projenin kaynak kodu bu depoya kopyalanmamıştır.

Tüm adaylar `source: "community-unverified"` etiketiyle başlar ve ancak ZapretTR'nin
kendi test motoruyla doğrulandıktan sonra `verified` durumuna geçer.
