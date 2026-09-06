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

---

## Parametre verisi hakkında not

`profiles/isp/*.json` içindeki başlangıç parametre adayları, Türkiye'deki kullanıcı
topluluğunda kamuya açık olarak paylaşılan yapılandırma dizgilerinden derlenmiştir
(forum başlıkları ve açık kaynak araçların belgeleri). Bunlar **kod değil, yapılandırma
verisidir** ve hiçbir üçüncü taraf projenin kaynak kodu bu depoya kopyalanmamıştır.

Tüm adaylar `source: "community-unverified"` etiketiyle başlar ve ancak ZapretTR'nin
kendi test motoruyla doğrulandıktan sonra `verified` durumuna geçer.
