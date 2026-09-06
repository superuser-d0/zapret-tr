ZapretTR - Superonline test paketi
==================================

Merhaba, ve yardimin icin tesekkurler.

Bu, Superonline hattinda hangi internet engellemelerinin nasil asilabildigini
olcen bir test araci. Kurulum yok, kayit yok, hicbir sey gondermiyor. Calisiyor,
olcuyor, sonucu bir dosyaya yaziyor. O kadar.


NASIL CALISTIRILIR
------------------

1. Bu klasordeki  TESTI-BASLAT.bat  dosyasina cift tikla.

2. Windows "bu uygulamanin degisiklik yapmasina izin veriyor musunuz?" diye
   soracak. Evet de. (Sebebi asagida yaziyor.)

3. Program ne yapacagini ekranda anlatip onay isteyecek. Okuduktan sonra
   E yazip Enter'a bas.

4. Test 2-5 dakika surer. Bu sirada internetin kisa sureli kesilip acilabilir,
   normal. Sayfalar acilmazsa panige gerek yok, test bitince duzelir.

5. Bitince klasorde  superonline-rapor.json  diye bir dosya olusacak.
   Onu bana geri gonder.

6. Son olarak  TEMIZLIK.bat  dosyasina cift tikla. Bu, testin yukledigi
   ag surucusunu kaldirir ve makineni eski haline dondurur.

   ONEMLI: Bu klasoru silmek istersen ONCE TEMIZLIK.bat calistir. Surucu
   yukluyken klasordeki dosyalardan biri Windows tarafindan kilitli tutulur
   ve "erisim reddedildi" hatasi alirsin. Temizlikten sonra klasor normal
   sekilde silinir.


NEDEN YONETICI YETKISI ISTIYOR
------------------------------

Test, ag paketlerini incelemek zorunda. Windows bunun icin cekirdek seviyesinde
calisan bir surucu (WinDivert) yuklenmesini sart kosuyor ve surucu yuklemek
yonetici yetkisi gerektiriyor.

Bu surucu gecici. TEMIZLIK.bat ile kaldiriliyor. Istersen once TEMIZLIK.bat'in
ne yaptigina bakabilirsin, iki satirlik bir dosya.


ANTIVIRUS UYARI VEREBILIR
-------------------------

Muhtemelen verecek. Iki sebebi var: program bir ag paketi surucusu tasiyor, ve
imzali degil (kod imzalama sertifikasi pahali). Bu tur araclar antivirusler
tarafindan rutin olarak isaretlenir.

Guvenmiyorsan calistirma - tamamen anlarim. Kaynak kodun tamami acik, istersen
gonderirim.


RAPORDA NE VAR, NE YOK
----------------------

VAR:
  - Servis saglayici adi (Superonline)
  - Test edilen adresler (discord.com, youtube.com gibi bilinen siteler)
  - Denenen teknik parametreler ve her birinin sonucu
  - Sureler

YOK:
  - IP adresin
  - Bilgisayar adin, kullanici adin
  - Gezdigin siteler, tarayici gecmisin
  - Baska hicbir kisisel bilgi

Rapor duz metin. Gondermeden once acip kendi gozunle okuyabilirsin, hicbir sey
sifreli ya da gizli degil.

Program raporu HICBIR YERE gondermiyor. Sadece diske yaziyor. Bana ulasmasinin
tek yolu senin gondermen.


BIR SEY TERS GIDERSE
--------------------

Internet tamamen kesilirse:
  - TEMIZLIK.bat calistir.
  - Duzelmezse bilgisayari yeniden baslat. Surucu kalici degil, acilista gitmis
    olur.

Program acilmiyorsa ya da hata veriyorsa:
  - Ekrandaki hatanin fotografini cek, bana gonder.

Testi yarida kesmek istersen:
  - Pencereyi kapat, sonra TEMIZLIK.bat calistir.
