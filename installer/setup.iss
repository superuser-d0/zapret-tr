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
; shellexec ZORUNLU. Uygulamanin manifesti requireAdministrator ve Inno, kurulum
; sonu "simdi baslat" girdisini yukseltilmemis baglamda CreateProcess ile
; calistiriyor -- CreateProcess UAC yukseltmesi yapamaz, yalnizca ShellExecute
; yapar. Bayrak olmadan kurulum sonunda su hata cikiyordu:
;   "CreateProcess tamamlanamadi; kod 740. The requested operation requires elevation."
; Kullanici icin belirtisi kotu: kurulum bitiyor ama uygulama acilmiyor; yalnizca
; masaustu kisayolundan aciliyor. Gercek makinede goruldu.
Filename: "{app}\{#AppExe}"; Description: "{#AppName} uygulamasını şimdi başlat"; Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
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

  // Uc ad birden: WinDivert'i baska araclar da kuruyor. WinDivert14 GoodbyeDPI'in
  // ve WinDivert 1.4'un adi, monkey bazi dagitimlarinki. Biri geride kalip surucusu
  // cekirdege yuklu duruyorsa dosya yine kilitli kalir ve ayni kod 5 hatasi doner.
  Exec(Sc, 'stop windivert', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(Sc, 'delete windivert', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(Sc, 'stop WinDivert14', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(Sc, 'delete WinDivert14', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(Sc, 'stop monkey', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(Sc, 'delete monkey', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Surucu goruntusunun cekirdekten dusmesi anlik degil.
  Sleep(2000);
end;
