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
  #define AppVersion "0.1.3"
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
