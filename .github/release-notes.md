Önceki sürümlerin tamamı için [CHANGELOG.md](../blob/main/CHANGELOG.md).

## Kurulum

`ZapretTR-Setup-<sürüm>.exe` dosyasını indirip çalıştırın. Yönetici yetkisi ister;
gerekçesi aşağıda.

## Önce bilmeniz gerekenler

**Bu paket imzalı değil.** Kod imzalama sertifikamız yok. Windows SmartScreen
"bilinmeyen yayımcı" uyarısı verecek ve bazı antivirüsler paketi işaretleyebilir.
Bunun teknik sebebi var: uygulama, çekirdek modunda çalışan bir paket yakalama
sürücüsü (WinDivert) yükleyen `winws.exe`'yi çalıştırıyor. İmzasız derleme + paket
sürücüsü birleşimi neredeyse her zaman false-positive üretir.

İndirdiğiniz dosyanın bozulmadığını `SHA256SUMS.txt` ile doğrulayabilirsiniz:

```powershell
Get-FileHash .\ZapretTR-Setup-<sürüm>.exe -Algorithm SHA256
```

Çıkan özet `SHA256SUMS.txt` içindekiyle aynı olmalı. **Bu, dosyanın bu yayından
geldiğini gösterir; imza yerine geçmez.** Kaynak koddan kendiniz derlemek her zaman
en güvenli yol — depo bunun için yeterli.

**Yönetici yetkisi neden zorunlu:** WinDivert çekirdek modunda çalışıyor, sürücü
yüklemek yükseltilmiş yetki gerektiriyor. Şifreli DNS seçeneği ayrıca sistem DNS
ayarını değiştiriyor; uygulama her çıkış yolunda bunu geri alır ve `--cleanup` ile
elle de geri alınabilir.

**Ne yaptığı ve yapmadığı:** ZapretTR paketleri kendisi kurcalamaz; bunu
[bol-van/zapret](https://github.com/bol-van/zapret) projesinin `winws` motoru yapar.
ZapretTR o motoru yöneten arayüz ve çalışan parametreyi otomatik bulan test
katmanıdır. Hiçbir veri dışarı gönderilmez; test raporu yalnızca diske yazılır.

## Doğrulama durumu

Profillerin çoğu henüz gerçek bir hatta doğrulanmadı. Arayüz bunu açıkça gösterir:
"doğrulanmadı" rozeti taşıyan bir strateji, o bağlantıda denenmemiş bir tahmindir.
Parametre testini çalıştırmak, sizin hattınızda gerçekten çalışanı bulur ve bir
sonraki açılışta hazır hale getirir.

## Dosyalar

- `ZapretTR-Setup-<sürüm>.exe` — kurulum paketi (kendi kendine yeten).
- `zapret-tr-saha-testi.zip` — taşınabilir test aracı; hiçbir şey kurmaz,
  `--cleanup` ile kendini temizler.
- `SHA256SUMS.txt` — yukarıdaki dosyaların özetleri.
