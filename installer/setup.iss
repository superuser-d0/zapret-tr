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

; Kurulum dosyasinin ve sihirbazin simgesi. Onceden Inno Setup'in varsayilan
; simgesi ve resmi gorunuyordu. Uclu de tools/make-icon.ps1 ile uretiliyor.
SetupIconFile=..\src\ZapretTr.App\Assets\ZapretTR.ico
WizardSmallImageFile=wizard-small-55.bmp,wizard-small-110.bmp

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

[InstallDelete]
; 0.2.1'den once yayin cok dosyaliydi: ZapretTR.dll, ZapretTr.Core.dll ve
; ZapretTr.Prober.dll exe'nin yaninda IMZASIZ duruyordu ve Akilli Uygulama
; Denetimi "Bu uygulamanin bir kismi engellendi ... ZapretTR.dll" diyerek
; uygulamayi calistirmadi. Artik tek dosya (gerekcesi ZapretTr.App.csproj'da).
; Inno yukseltmede eski dosyalari kendiliginden silmiyor; silinmezse o imzasiz
; DLL'ler ve ~150 MB .NET kalintisi Program Files'ta kalir. [InstallDelete]
; dosya kopyalamadan ONCE calisiyor: guncel yerel DLL'ler (msquic, WPF'in
; *_cor3 dosyalari) [Files] ile hemen geri yaziliyor. Yalnizca kok dizin; alt
; klasorler (zapret-winws, dnscrypt-proxy, profiles) bu desenlere girmiyor.
; Kullanici ayarlari ProgramData'da, burada degil.
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\ZapretTR.deps.json"
Type: files; Name: "{app}\ZapretTR.runtimeconfig.json"
Type: files; Name: "{app}\createdump.exe"
; WPF/WinForms yerellestirme derlemeleri; tek dosyada exe'nin icindeler.
Type: filesandordirs; Name: "{app}\cs"
Type: filesandordirs; Name: "{app}\de"
Type: filesandordirs; Name: "{app}\es"
Type: filesandordirs; Name: "{app}\fr"
Type: filesandordirs; Name: "{app}\it"
Type: filesandordirs; Name: "{app}\ja"
Type: filesandordirs; Name: "{app}\ko"
Type: filesandordirs; Name: "{app}\pl"
Type: filesandordirs; Name: "{app}\pt-BR"
Type: filesandordirs; Name: "{app}\ru"
Type: filesandordirs; Name: "{app}\tr"
Type: filesandordirs; Name: "{app}\zh-Hans"
Type: filesandordirs; Name: "{app}\zh-Hant"

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
; DNS bekcisi kaydi ve bekci turu ARTIK BURADA DEGIL, [Code] icinde (CurStepChanged).
;
; Sebep olculdu (2026-09-16, gercek makine, 0.2.3): Akilli Uygulama Denetimi acikken
; Windows imzasiz ZapretTR.exe'yi calistirmiyor ve Inno'nun [Run] girdisi bunu ham
; haliyle kullanicinin yuzune veriyordu:
;   "Su dosya yurutulmedi: C:\Program Files\ZapretTR\ZapretTR.exe
;    CreateProcess tamamlanamadi; kod 4551. Uygulama Denetimi ilkesi bu dosyayi engelledi."
; Kurulumun ortasinda, ne yapacagini soylemeyen bir kutu. [Code] icinde sonucu
; kendimiz denetliyor ve sonunda TEK ve anlasilir bir aciklama veriyoruz.

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
// Akilli Uygulama Denetimi ZORLAMA modunda mi.
//
// Deger: 0 kapali, 1 acik (zorlama), 2 degerlendirme. OLCULDU (2026-09-16):
// degerlendirme modundaki bir makinede 0.2.4 hatasiz kuruldu ve calisti;
// SAC ACIK bir makinede 0.2.5 taslagi kuruldu ama ZapretTR.exe hic calismadi
// (hata 4551). "1 = acik" eslemesi Microsoft'un belgeledigi deger; acik bir
// makinede okunarak DOGRULANMADI.
function SacAcik(): Boolean;
var
  Durum: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SYSTEM\CurrentControlSet\Control\CI\Policy',
                               'VerifiedAndReputablePolicyState', Durum)
            and (Durum = 1);
end;

function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
  Mesaj: String;
begin
  // SAC aciksa kurulum BASTAN soylemeli. Eskiden kullanici once kuruluyor, sonra
  // "exe calistirilamadi" kutusunu goruyor ve elinde acilmayan bir uygulama
  // kaliyordu. Kod imzasi olmadan bu engeli asmanin yolu yok; yapabildigimiz tek
  // sey kullaniciyi kurmadan once bilgilendirmek. Sessiz kurulumda (CI) soru
  // sorulmuyor ve kurulum suruyor.
  if SacAcik() then
  begin
    Mesaj := 'Bu bilgisayarda Akilli Uygulama Denetimi ACIK.' + #13#10 + #13#10;
    Mesaj := Mesaj + 'ZapretTR''in kod imzalama sertifikasi yok. Bu ayar acikken Windows'
             + ' ZapretTR''i calistirmiyor (hata 4551): kurulum biter ama uygulama acilmaz.' + #13#10 + #13#10;
    Mesaj := Mesaj + 'Kurmadan once, SIRASIYLA:' + #13#10;
    Mesaj := Mesaj + '1) Indirdiginiz kurulum dosyasina sag tiklayin -> Ozellikler ->' + #13#10;
    Mesaj := Mesaj + '   alttaki "Engellemeyi Kaldir" kutusunu isaretleyip Tamam deyin.' + #13#10;
    Mesaj := Mesaj + '2) Windows Guvenligi -> Uygulama ve tarayici denetimi ->' + #13#10;
    Mesaj := Mesaj + '   Akilli Uygulama Denetimi ayarlari -> Kapali.' + #13#10;
    Mesaj := Mesaj + '3) Kurulumu yeniden calistirin.' + #13#10 + #13#10;
    Mesaj := Mesaj + 'Yine de simdi kurulsun mu?';

    if SuppressibleMsgBox(Mesaj, mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDYES) <> IDYES then
    begin
      Result := False;
      Exit;
    end;
  end;

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
  TemizlikYapildi: Boolean;
begin
  Result := '';
  OncekiExe := ExpandConstant('{app}\{#AppExe}');
  Sc := ExpandConstant('{sys}\sc.exe');

  // Durum, sokme ISLEMINDEN ONCE okunmali; sonra bakmanin anlami olmaz.
  ServisGeriKurulacak := ServisKurulu('ZapretTR');

  // 1) Onceki surumun kendi temizligi: servisleri soker ve DNS'i geri alir.
  //    Kullanici ayarlari ve ogrenilmis dogrulamalar KORUNUR.
  //
  //    SONUCU DENETLENIYOR ve denetlenmesi ZORUNLU. Eskiden burada Exec'in donus
  //    degeri de ResultCode da yok sayiliyordu. Gercek bir makinede olculdu
  //    (2026-09-16, 0.2.3 kurulumu): Akilli Uygulama Denetimi acikken Windows
  //    ZapretTR.exe'yi calistirmiyor (hata 4551, "An Application Control policy
  //    has blocked this file") -- yani bu cagri basarisiz oluyor, servisler
  //    SOKULMUYOR ve kimse fark etmiyor. Kullanicinin bildirimi: "guncelleme
  //    sirasinda servisleri kapamiyor".
  TemizlikYapildi := False;
  if FileExists(OncekiExe) then
  begin
    TemizlikYapildi :=
      Exec(OncekiExe, '--uninstall-services', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
      and (ResultCode = 0);
  end;

  // 1b) Exe calismadiysa servisleri KURULUM kendisi soker.
  //
  //     sc.exe Microsoft imzali; SAC onu engellemiyor, dolayisiyla bu yol imzasiz
  //     exe'mize hic bagli degil. Tek basina yetmiyor -- exe'nin yaptigi DNS geri
  //     almayi sc.exe yapamaz; ama servisin ayakta kalip dosyalari kilitlemesi ve
  //     yukseltmenin yarim kalmasi bundan daha kotu.
  //
  //     DNS geri alma bu durumda DNS bekcisine kaliyor: gorev SYSTEM olarak kosuyor
  //     ve cozumleyici yoksa yonlendirmeyi zaten geri aliyor.
  if not TemizlikYapildi then
  begin
    Exec(Sc, 'stop ZapretTR', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(Sc, 'stop ZapretTR-DNS', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    SurucuDussun('ZapretTR');
    SurucuDussun('ZapretTR-DNS');
    Exec(Sc, 'delete ZapretTR', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(Sc, 'delete ZapretTR-DNS', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;

  // 2) Surucuyu KURULUM KENDISI kaldirir; onceki exe'ye guvenmez.
  //
  //    Sebep: bu adim eski surume DELEGE EDILEMEZ. Surucu kaldirma davranisi
  //    0.1.4'te eklendi, dolayisiyla 0.1.3 ve oncesinden yukseltirken cagrilan
  //    exe onu YAPMIYOR. Tam da duzeltmeye calistigimiz kullanicilar eski surumde
  //    olacagi icin, kurulumun kendi ayaklari uzerinde durmasi sart.
  //
  //    sc.exe cagrilari WinDivertCleanup'in yaptiginin aynisi. (Sc yukarida atandi.)

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
  Exe: String;
  Mesaj: String;
  Engellendi: Boolean;
begin
  // ServisGeriKurulacak kosulu ARTIK BURADA DEGIL: DNS bekcisi, servis kurulu
  // olmasa da her kurulumda yazilmali (gerekcesi DnsGuard.cs). Eskiden bu satir
  // "not ServisGeriKurulacak" ile de cikiyordu ama bekci [Run] icindeydi, yani
  // ayri kosuyordu; ikisi burada birlestigi icin kosul daraltildi.
  if CurStep <> ssPostInstall then
    Exit;

  Exe := ExpandConstant('{app}\{#AppExe}');
  Engellendi := False;

  // 1) DNS bekcisi gorevi. Her kurulumda yeniden yaziliyor ki gorev YENI exe'yi
  //    gostersin. Gerekcesi DnsGuard.cs'de: servis modunda arkada DNS'i izleyen
  //    baska hicbir sey yok.
  if not Exec(Exe, '--register-dns-guard', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Engellendi := True;

  // 2) Sokulen otomatik baslatma servisini geri kur. Sessizce basarisiz olmamali:
  //    olursa kullanici korundugunu sanarak korumasiz kalir.
  if ServisGeriKurulacak then
  begin
    if not Exec(Exe, '--install-services', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      Engellendi := True;
      ResultCode := -1;
    end;

    if (ResultCode <> 0) and (not Engellendi) and (not WizardSilent()) then
      MsgBox('Otomatik baslatma servisi geri kurulamadi (kod ' + IntToStr(ResultCode) + ').' #13#10
             'Uygulamayi acip "Servis Olarak Yukle" dugmesiyle yeniden kurabilirsiniz.',
             mbInformation, MB_OK);
  end;

  // 3) Kurulum biterken bir bekci turu: yukseltme sirasinda DNS geri alinamamissa
  //    sistem 127.0.0.1'de cozumleyicisiz kalmis olabilir ve bir sonraki
  //    tetikleyiciyi (en gec 10 dk) beklemeye gerek yok. nowait: bekci
  //    cozumleyiciyi 90 sn'ye kadar bekleyebiliyor, kurulum beklememeli.
  Exec(Exe, '--dns-guard', '', SW_HIDE, ewNoWait, ResultCode);

  // Exe HIC calismadiysa sebebi neredeyse her zaman Akilli Uygulama Denetimi
  // (hata 4551). Kullanici bunu ham CreateProcess hatasi olarak gormesin: ne
  // oldugunu ve ne yapacagini TEK bir yerde, sirasiyla soyluyoruz.
  //
  // Iki adim da gerekli ve ikincisini gercek kullanici bulup bildirdi
  // (2026-09-16): yalnizca SAC'i kapatmak YETMEDI, indirilen dosyanin
  // "Engellemeyi Kaldir" isaretinin de temizlenmesi gerekti.
  if Engellendi and (not WizardSilent()) then
  begin
    // Metin degiskene yaziliyor ve HICBIR SATIR '#' ile BASLAMIYOR. Sebep
    // olculdu: ISCC'nin onislemcisi satir basindaki '#' karakterini yonerge
    // sayiyor, dolayisiyla '#13#10' ile baslayan bir devam satiri derlemeyi
    // "Unknown preprocessor directive" ile durduruyor. Bu, metin testlerinin
    // goremedigi bir hata sinifi; yalnizca gercek derleme yakaliyor.
    Mesaj := 'Windows, ZapretTR.exe dosyasini calistirmadi'
             + ' (Akilli Uygulama Denetimi, hata 4551).' + #13#10 + #13#10;
    // "ZapretTR kurulu" yaziyordu; dogru ama yaniltici. Bu durumda uygulamanin
    // kendisi de acilmiyor (gercek makinede goruldu, 2026-09-16).
    Mesaj := Mesaj + 'Dosyalar kopyalandi, ama bu ayar acikken ZapretTR acilmaz;'
             + ' otomatik baslatma servisi ve DNS bekcisi de kurulamadi.' + #13#10 + #13#10;
    Mesaj := Mesaj + 'Cozum icin SIRASIYLA:' + #13#10;
    Mesaj := Mesaj + '1) Indirdiginiz kurulum dosyasina sag tiklayin -> Ozellikler ->' + #13#10;
    Mesaj := Mesaj + '   alttaki "Engellemeyi Kaldir" kutusunu isaretleyip Tamam deyin.' + #13#10;
    Mesaj := Mesaj + '2) Windows Guvenligi -> Uygulama ve tarayici denetimi ->' + #13#10;
    Mesaj := Mesaj + '   Akilli Uygulama Denetimi ayarlari -> Kapali.' + #13#10;
    Mesaj := Mesaj + '3) Kurulumu yeniden calistirin.' + #13#10 + #13#10;
    Mesaj := Mesaj + 'ZapretTR''in kod imzalama sertifikasi yok; bu ozellik'
             + ' imzasiz uygulamalara tek tek istisna tanimlamaya izin vermiyor.';

    MsgBox(Mesaj, mbError, MB_OK);
  end;
end;
