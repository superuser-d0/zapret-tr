; ZapretTR kurulum paketi (Inno Setup 6)
;
; Bu paketin varlik sebebi teknik bir zorunluluk. Otomatik baslatma eklendikten
; sonra Windows servisleri ikilileri MUTLAK YOLLA isaret etmeye basladi. Ikililer
; derleme agacinda dururken depo tasinir ya da temizlenirse servisler acilista
; baslayamiyor; winws calismayinca koruma gidiyor, ama asil kotusu dnscrypt-proxy
; calismayinca sistem DNS'i 127.0.0.1'i gostermeye devam ediyor ve hicbir adres
; cozulemiyor. Yani kullanicinin internetinin gitmesi.
;
; Bu yuzden kurulum, ikilileri kullanicinin dokunmayacagi sabit bir dizine koyar.

#define AppName "ZapretTR"

; Surum disaridan verilebilir: ISCC /DAppVersion=1.2.3
; Yayin akisi bunu git tag'inden geciyor, boylece kurulum paketi, exe'nin
; surum kaynagi (Directory.Build.props) ve tag birbirinden ayrilamiyor.
#ifndef AppVersion
  #define AppVersion "0.1.6"
#endif
#define AppPublisher "ZapretTR contributors"
#define AppUrl "https://github.com/superuser-d0/zapret-tr"
#define AppExe "ZapretTR.exe"

[Setup]
AppId={{8F3A1C42-5D7E-4B19-9A24-6E8C0F5D3B71}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppSupportURL={#AppUrl}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=Output
OutputBaseFilename=ZapretTR-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; Kurulum yonetici olarak calismali: servis kurar ve cekirdek surucusu tasiyan
; dosyalari Program Files altina yazar.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Uygulamanin kendisi de yonetici gerektirdigi icin kurulum sonunda
; "simdi calistir" secenegi yukseltilmis baslatir.
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Uygulama ve kutuphaneleri
; Hata ayiklama sembolleri (.pdb) disarida. Yayin zaten DebugType=none ile
; uretiliyor ama bu filtre ikinci bir guvence: elle yapilmis bir yayin ciktisi
; pakete sembol sizdirmasin.
Source: "..\publish\app\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs

; winws ve bagimliliklari. VendorPaths bu klasoru uygulamanin YANINDA ariyor,
; klasor adi bu yuzden birebir "zapret-winws" olmali.
Source: "..\vendor\zapret-winws\*"; DestDir: "{app}\zapret-winws"; Flags: ignoreversion recursesubdirs

; dnscrypt-proxy zapret-winws'in KARDESI olmali; VendorPaths onu boyle ariyor.
Source: "..\vendor\dnscrypt-proxy\*"; DestDir: "{app}\dnscrypt-proxy"; Flags: ignoreversion recursesubdirs

; ISS profilleri ve hedef listesi
Source: "..\profiles\*"; DestDir: "{app}\profiles"; Flags: ignoreversion recursesubdirs

; Lisans ve bildirimler
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{#AppName} kaldir"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Masaüstüne kısayol ekle"; GroupDescription: "Ek görevler:"

[Run]
; DNS bekcisi: SYSTEM olarak calisan zamanlanmis gorev. Servis modunda arkada DNS'i
; izleyen baska hicbir sey yok -- sonradan takilan ag karti yonlendirilmiyor,
; dnscrypt kalici olarak olurse makine hicbir adi cozemiyordu. Gerekcesi
; DnsGuard.cs'de. Her kurulumda yeniden yaziliyor ki gorev yeni exe'yi gostersin.
Filename: "{app}\{#AppExe}"; Parameters: "--register-dns-guard"; Flags: runhidden waituntilterminated; StatusMsg: "DNS bekcisi kuruluyor..."

; Kurulum biterken bir bekci turu. Yukseltmenin onceki surumu sokerken DNS geri
; alinamamis ve servis geri kurulmamissa sistem DNS'i 127.0.0.1'de, cozumleyicisiz
; kalmis olabilir; bir sonraki tetikleyiciyi (en gec 10 dk) beklemeye gerek yok.
; nowait: bekci cozumleyiciyi 90 sn'ye kadar bekleyebiliyor, kurulum beklememeli.
Filename: "{app}\{#AppExe}"; Parameters: "--dns-guard"; Flags: runhidden nowait

; shellexec ZORUNLU. Uygulamanin manifesti requireAdministrator ve Inno, kurulum
; sonu "simdi baslat" girdisini yukseltilmemis baglamda CreateProcess ile
; calistiriyor -- CreateProcess UAC yukseltmesi yapamaz, yalnizca ShellExecute
; yapar. Bayrak olmadan kurulum sonunda su hata cikiyordu:
;   "CreateProcess tamamlanamadi; kod 740. The requested operation requires elevation."
; Kullanici icin belirtisi kotu: kurulum bitiyor ama uygulama acilmiyor; yalnizca
; masaustu kisayolundan aciliyor. Gercek makinede goruldu.
Filename: "{app}\{#AppExe}"; Description: "{#AppName} uygulamasını şimdi başlat"; Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
; Bekci ONCE siliniyor: servisler sokulurken bir bekci turu araya girerse
; yarim kalmis bir durumu gorup DNS'i yeniden yonlendirmeye kalkabilir.
Filename: "{app}\{#AppExe}"; Parameters: "--unregister-dns-guard"; Flags: runhidden waituntilterminated; RunOnceId: "ZapretTrDnsGuard"

; Kaldirmadan ONCE servisleri sokup DNS'i geri al. Bu adim atlanirsa kullanicinin
; sistem DNS'i 127.0.0.1'de kalir, dnscrypt-proxy de silinmis olur ve makine
; hicbir adi cozemez. Kaldirma sirasinda yapilabilecek en kotu sey bu.
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall-services"; Flags: runhidden waituntilterminated; RunOnceId: "ZapretTrServices"

[UninstallDelete]
; Uygulamanin urettigi calisma dosyalari (dnscrypt yapilandirmasi, cozumleyici
; listesi) [Files] altinda listelenmedigi icin elle siliniyor.
Type: filesandordirs; Name: "{app}\dnscrypt-proxy"
Type: filesandordirs; Name: "{app}\zapret-winws"

[Code]
// Yukseltmeden ONCE otomatik baslatma servisi kurulu muydu. Kurulum, dosyalari
// degistirebilmek icin servisleri sokmek ZORUNDA (calisan winws surucuyu, dolayisiyla
// WinDivert64.sys'i kilitliyor) -- ama soktugunu geri kurmazsa kullanicinin otomatik
// baslatma tercihi yukseltmede SESSIZCE kaybolur.
//
// 0.1.7 yukseltmesinde gercek bir makinede goruldu: kurulum bitti, uygulama "SISTEM
// HAZIR" dedi, servisler yoktu ve kullanici korumasiz kaldi. Belirtisi yok: uygulama
// dogru davraniyor, kaybolan sey kullanicinin bir daha basmadigi bir dugmenin sonucu.
var
  ServisGeriKurulacak: Boolean;

// sc query cikis kodu: 0 = servis var, 1060 = yok.
function ServisKurulu(const Ad: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\sc.exe'), 'query ' + Ad, '',
                 SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

// Bir komutun ciktisinda metin geciyor mu (find bulunca 0 doner).
function CiktidaVar(const Komut, Metin: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'), '/c ' + Komut + ' | find /I "' + Metin + '" >nul',
                 '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

// taskkill /F surecin OLDUGUNU beklemeden donuyor: sonlandirma istegi gonderiliyor,
// tutamaclar birkac yuz milisaniye sonra kapaniyor. O arada surucuye "dur"
// denirse surucu kimse kullanmiyor olsa bile "durduruluyor" durumunda takili
// kalabiliyor ve yeni winws onu acamiyordu -- sahadan gelen "yeni surumu kurdum,
// motor calismiyor, yeniden baslatinca aciliyor" bildiriminin en olasi yolu.
procedure SurecBitsin(const Ad: String);
var
  i: Integer;
begin
  for i := 1 to 40 do
  begin
    if not CiktidaVar('tasklist /FI "IMAGENAME eq ' + Ad + '" /NH', Ad) then
      Exit;
    Sleep(250);
  end;
end;

// Surucu servisi STOP_PENDING'den cikana kadar bekle (en cok 10 sn).
procedure SurucuDussun(const Ad: String);
var
  i: Integer;
begin
  for i := 1 to 40 do
  begin
    if not CiktidaVar('sc query ' + Ad, 'PENDING') then
      Exit;
    Sleep(250);
  end;
end;

// Kurulum baslamadan once calisan bir surum varsa kapat: acik bir uygulama
// dosyalari kilitler ve kurulum yarim kalir.
//
// ONCE NAZIKCE. Eskiden dogrudan "taskkill /F" vardi ve bu, uygulamanin kendi
// temizlik yolunu tamamen atliyordu: koruma acikken yukseltme yapan bir
// kullanicida winws ve dnscrypt-proxy oksuz kaliyor, sistem DNS'i 127.0.0.1'de
// kaliyordu. dnscrypt sonradan olurse makine hicbir adi cozemez.
//
// /F'siz taskkill WM_CLOSE gonderiyor; uygulamanin pencere kapanma yolu winws'i
// durdurup DNS'i geri aliyor. Zorla oldurme yalnizca kapanmayan bir surec icin,
// son care olarak kaliyor -- kurulumun dosya kilidi yuzunden yarim kalmasi da
// kabul edilebilir degil.
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/c taskkill /IM ZapretTR.exe',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Temizligin bitmesi icin sure taniniyor: winws'in durmasi ve DNS'in geri
  // alinmasi anlik degil.
  Sleep(4000);

  Exec(ExpandConstant('{cmd}'), '/c taskkill /IM ZapretTR.exe /F',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

// Dosyalar degistirilmeden ONCE onceki kurulumun surucusunu cekirdekten kaldir.
//
// Gercek bir kullanicida goruldu: bir test kosumundan sonra WinDivert surucusu
// cekirdekte asili kaliyor ve WinDivert64.sys kilitleniyor. Yukseltme o dosyayi
// degistiremiyor ve kurulum su hatayi veriyor:
//
//   "Var olan dosya degistirilirken sorun cikti:
//    DeleteFile tamamlanamadi; kod 5. Erisim engellendi."
//
// Kullaniciya kalan tek secenek "bu dosya atlansin" oluyor -- yani eski surucu
// dosyasiyla devam etmek. Ayni kilit, kaldirmadan sonra da klasorde kalinti
// birakiyor ve bir sonraki kurulum ayni duvara tosluyor.
//
// PrepareToInstall dogru kanca: kurulum yeri artik belli ({app} cozulebiliyor)
// ama dosya kopyalama henuz baslamadi.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  OncekiExe: String;
  Sc: String;
begin
  Result := '';
  OncekiExe := ExpandConstant('{app}\{#AppExe}');

  // Durum, sokme ISLEMINDEN ONCE okunmali; sonra bakmanin anlami olmaz.
  ServisGeriKurulacak := ServisKurulu('ZapretTR');

  // 1) Onceki surumun kendi temizligi: servisleri soker ve DNS'i geri alir.
  //    Kullanici ayarlari ve ogrenilmis dogrulamalar KORUNUR.
  if FileExists(OncekiExe) then
  begin
    Exec(OncekiExe, '--uninstall-services', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;

  // 2) Surucuyu KURULUM KENDISI kaldirir; onceki exe'ye guvenmez.
  //
  //    Sebep: bu adim eski surume DELEGE EDILEMEZ. Surucu kaldirma davranisi
  //    0.1.4'te eklendi, dolayisiyla 0.1.3 ve oncesinden yukseltirken cagrilan
  //    exe onu YAPMIYOR. Tam da duzeltmeye calistigimiz kullanicilar eski surumde
  //    olacagi icin, kurulumun kendi ayaklari uzerinde durmasi sart.
  //
  //    sc.exe cagrilari WinDivertCleanup'in yaptiginin aynisi.
  Sc := ExpandConstant('{sys}\sc.exe');

  // winws surucuyu acik tutuyor olabilir; once o gitmeli.
  Exec(ExpandConstant('{cmd}'), '/c taskkill /IM winws.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{cmd}'), '/c taskkill /IM dnscrypt-proxy.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Surucuye dokunmadan once surecin GERCEKTEN bitmesini bekle; gerekcesi SurecBitsin'de.
  SurecBitsin('winws.exe');

  // Uc ad birden: WinDivert'i baska araclar da kuruyor. WinDivert14 GoodbyeDPI'in
  // ve WinDivert 1.4'un adi, monkey bazi dagitimlarinki. Biri geride kalip surucusu
  // cekirdege yuklu duruyorsa dosya yine kilitli kalir ve ayni kod 5 hatasi doner.
  Exec(Sc, 'stop windivert', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  SurucuDussun('windivert');
  Exec(Sc, 'delete windivert', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(Sc, 'stop WinDivert14', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  SurucuDussun('WinDivert14');
  Exec(Sc, 'delete WinDivert14', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(Sc, 'stop monkey', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  SurucuDussun('monkey');
  Exec(Sc, 'delete monkey', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Surucu goruntusunun cekirdekten dusmesi anlik degil.
  Sleep(2000);
end;

// Sokulen servisi geri kur. Dosyalar yerine gectikten SONRA, cunku servis yeni
// ikiliyi gostermeli.
//
// Geri kurma sessizce basarisiz olmamali: olursa kullanici korundugunu sanarak
// korumasiz kalir, ki duzeltmeye calistigimiz sey tam olarak bu.
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if (CurStep <> ssPostInstall) or (not ServisGeriKurulacak) then
    Exit;

  if not Exec(ExpandConstant('{app}\{#AppExe}'), '--install-services', '',
              SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;

  if (ResultCode <> 0) and (not WizardSilent()) then
    MsgBox('Otomatik baslatma servisi geri kurulamadi (kod ' + IntToStr(ResultCode) + ').' #13#10
           'Uygulamayi acip "Servis Olarak Yukle" dugmesiyle yeniden kurabilirsiniz.',
           mbInformation, MB_OK);
end;
