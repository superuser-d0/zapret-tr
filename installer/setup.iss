; ZapretTR kurulum paketi (Inno Setup 6)
;
; Bu paketin var olma sebebi teknik bir zorunluluk. Otomatik başlatma eklendikten
; sonra Windows servisleri ikilileri MUTLAK YOLLA işaret etmeye başladı. İkililer
; derleme ağacında dururken depo taşınır ya da temizlenirse servisler açılışta
; başlayamıyor; winws çalışmayınca koruma gidiyor, ama asıl kötüsü dnscrypt-proxy
; çalışmayınca sistem DNS'i 127.0.0.1'i göstermeye devam ediyor ve hiçbir adres
; çözülemiyor. Yani kullanıcının internetinin gitmesi.
;
; Bu yüzden kurulum, ikilileri kullanıcının dokunmayacağı sabit bir dizine koyar.

#define AppName "ZapretTR"

; Sürüm dışarıdan verilebilir: ISCC /DAppVersion=1.2.3
; Yayın akışı bunu git etiketinden geçiriyor; böylece kurulum paketi, exe'nin
; sürüm kaynağı (Directory.Build.props) ve etiket birbirinden ayrışamıyor.
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

; Kurulum dosyasının ve sihirbazın simgesi. Önceden Inno Setup'ın varsayılan
; simgesi ve resmi görünüyordu. Üçü de tools/make-icon.ps1 ile üretiliyor.
SetupIconFile=..\src\ZapretTr.App\Assets\ZapretTR.ico
WizardSmallImageFile=wizard-small-55.bmp,wizard-small-110.bmp

; Kurulum yönetici olarak çalışmalı: servis kurar ve çekirdek sürücüsü taşıyan
; dosyaları Program Files altına yazar.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Uygulamanın kendisi de yönetici gerektirdiği için kurulum sonunda
; "şimdi çalıştır" seçeneği yükseltilmiş başlatır.
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[InstallDelete]
; 0.2.1'den önce yayın çok dosyalıydı: ZapretTR.dll, ZapretTr.Core.dll ve
; ZapretTr.Prober.dll exe'nin yanında İMZASIZ duruyordu ve Akıllı Uygulama
; Denetimi "Bu uygulamanın bir kısmı engellendi ... ZapretTR.dll" diyerek
; uygulamayı çalıştırmadı. Artık tek dosya (gerekçesi ZapretTr.App.csproj'da).
; Inno yükseltmede eski dosyaları kendiliğinden silmiyor; silinmezse o imzasız
; DLL'ler ve ~150 MB .NET kalıntısı Program Files'ta kalır. [InstallDelete]
; dosya kopyalamadan ÖNCE çalışıyor: güncel yerel DLL'ler (msquic, WPF'in
; *_cor3 dosyaları) [Files] ile hemen geri yazılıyor. Yalnızca kök dizin; alt
; klasörler (zapret-winws, dnscrypt-proxy, profiles) bu desenlere girmiyor.
; Kullanıcı ayarları ProgramData'da, burada değil.
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\ZapretTR.deps.json"
Type: files; Name: "{app}\ZapretTR.runtimeconfig.json"
Type: files; Name: "{app}\createdump.exe"
; WPF/WinForms yerelleştirme derlemeleri; tek dosyada exe'nin içindeler.
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
; Uygulama ve kütüphaneleri
; Hata ayıklama sembolleri (.pdb) dışarıda. Yayın zaten DebugType=none ile
; üretiliyor ama bu süzgeç ikinci bir güvence: elle yapılmış bir yayın çıktısı
; pakete sembol sızdırmasın.
Source: "..\publish\app\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs

; winws ve bağımlılıkları. VendorPaths bu klasörü uygulamanın YANINDA arıyor,
; klasör adı bu yüzden birebir "zapret-winws" olmalı.
Source: "..\vendor\zapret-winws\*"; DestDir: "{app}\zapret-winws"; Flags: ignoreversion recursesubdirs

; dnscrypt-proxy, zapret-winws'in KARDEŞİ olmalı; VendorPaths onu böyle arıyor.
Source: "..\vendor\dnscrypt-proxy\*"; DestDir: "{app}\dnscrypt-proxy"; Flags: ignoreversion recursesubdirs

; İSS profilleri ve hedef listesi
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
; DNS bekçisi kaydı ve bekçi turu ARTIK BURADA DEĞİL, [Code] içinde (CurStepChanged).
;
; Sebep ölçüldü (2026-09-16, gerçek makine, 0.2.3): Akıllı Uygulama Denetimi açıkken
; Windows imzasız ZapretTR.exe'yi çalıştırmıyor ve Inno'nun [Run] girdisi bunu ham
; hâliyle kullanıcının yüzüne veriyordu:
;   "Şu dosya yürütülmedi: C:\Program Files\ZapretTR\ZapretTR.exe
;    CreateProcess tamamlanamadı; kod 4551. Uygulama Denetimi ilkesi bu dosyayı engelledi."
; Kurulumun ortasında, ne yapacağını söylemeyen bir kutu. [Code] içinde sonucu
; kendimiz denetliyor ve sonunda TEK ve anlaşılır bir açıklama veriyoruz.

; shellexec ZORUNLU. Uygulamanın manifesti requireAdministrator ve Inno, kurulum
; sonu "şimdi başlat" girdisini yükseltilmemiş bağlamda CreateProcess ile
; çalıştırıyor; CreateProcess UAC yükseltmesi yapamaz, yalnızca ShellExecute
; yapar. Bayrak olmadan kurulum sonunda şu hata çıkıyordu:
;   "CreateProcess tamamlanamadı; kod 740. The requested operation requires elevation."
; Kullanıcı için belirtisi kötü: kurulum bitiyor ama uygulama açılmıyor; yalnızca
; masaüstü kısayolundan açılıyor. Gerçek makinede görüldü.
; Check: ExeCalisabildi ZORUNLU. Ölçüldü (2026-09-16, SAC açık makine, 0.2.5 taslağı):
; kurulum sonunda açıklama kutumuz doğru çıktı, ama kullanıcı "şimdi başlat" işaretliyken
; Son düğmesine basınca Inno ham hatayı yine gösterdi:
;   "Unable to execute file: ...ZapretTR.exe / ShellExecuteEx failed; code 4551."
; Exe az önce çalışamadıysa bu kutu hem gereksiz hem de açıklamamızı bozuyor.
Filename: "{app}\{#AppExe}"; Description: "{#AppName} uygulamasını şimdi başlat"; Flags: nowait postinstall skipifsilent shellexec; Check: ExeCalisabildi

[UninstallRun]
; Bekçi ÖNCE siliniyor: servisler sökülürken bir bekçi turu araya girerse
; yarım kalmış bir durumu görüp DNS'i yeniden yönlendirmeye kalkabilir.
Filename: "{app}\{#AppExe}"; Parameters: "--unregister-dns-guard"; Flags: runhidden waituntilterminated; RunOnceId: "ZapretTrDnsGuard"

; Kaldırmadan ÖNCE servisleri söküp DNS'i geri al. Bu adım atlanırsa kullanıcının
; sistem DNS'i 127.0.0.1'de kalır, dnscrypt-proxy de silinmiş olur ve makine
; hiçbir adı çözemez. Kaldırma sırasında yapılabilecek en kötü şey bu.
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall-services"; Flags: runhidden waituntilterminated; RunOnceId: "ZapretTrServices"

[UninstallDelete]
; Uygulamanın ürettiği çalışma dosyaları (dnscrypt yapılandırması, çözümleyici
; listesi) [Files] altında listelenmediği için elle siliniyor.
Type: filesandordirs; Name: "{app}\dnscrypt-proxy"
Type: filesandordirs; Name: "{app}\zapret-winws"

[Code]
// Yükseltmeden ÖNCE otomatik başlatma servisi kurulu muydu. Kurulum, dosyaları
// değiştirebilmek için servisleri sökmek ZORUNDA (çalışan winws sürücüyü, dolayısıyla
// WinDivert64.sys'i kilitliyor); ama söktüğünü geri kurmazsa kullanıcının otomatik
// başlatma tercihi yükseltmede SESSİZCE kaybolur.
//
// 0.1.7 yükseltmesinde gerçek bir makinede görüldü: kurulum bitti, uygulama "SİSTEM
// HAZIR" dedi, servisler yoktu ve kullanıcı korumasız kaldı. Belirtisi yok: uygulama
// doğru davranıyor, kaybolan şey kullanıcının bir daha basmadığı bir düğmenin sonucu.
var
  ServisGeriKurulacak: Boolean;

// Kurulum sonunda ZapretTR.exe çalıştırılamadı mı (SAC, hata 4551). [Run]'daki
// "şimdi başlat" girdisi buna bakıyor; bkz. ExeCalisabildi.
var
  Engellendi: Boolean;

function ExeCalisabildi(): Boolean;
begin
  Result := not Engellendi;
end;

// sc query çıkış kodu: 0 = servis var, 1060 = yok.
function ServisKurulu(const Ad: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\sc.exe'), 'query ' + Ad, '',
                 SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

// Bir komutun çıktısında metin geçiyor mu (find bulunca 0 döner).
function CiktidaVar(const Komut, Metin: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'), '/c ' + Komut + ' | find /I "' + Metin + '" >nul',
                 '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

// taskkill /F sürecin ÖLDÜĞÜNÜ beklemeden dönüyor: sonlandırma isteği gönderiliyor,
// tutamaçlar birkaç yüz milisaniye sonra kapanıyor. O arada sürücüye "dur"
// denirse sürücü kimse kullanmıyor olsa bile "durduruluyor" durumunda takılı
// kalabiliyor ve yeni winws onu açamıyordu. Sahadan gelen "yeni sürümü kurdum,
// motor çalışmıyor, yeniden başlatınca açılıyor" bildiriminin en olası yolu bu.
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

// Sürücü servisi STOP_PENDING'den çıkana kadar bekle (en çok 10 sn).
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

// Kurulum başlamadan önce çalışan bir sürüm varsa kapat: açık bir uygulama
// dosyaları kilitler ve kurulum yarım kalır.
//
// ÖNCE NAZİKÇE. Eskiden doğrudan "taskkill /F" vardı ve bu, uygulamanın kendi
// temizlik yolunu tamamen atlıyordu: koruma açıkken yükseltme yapan bir
// kullanıcıda winws ve dnscrypt-proxy öksüz kalıyor, sistem DNS'i 127.0.0.1'de
// kalıyordu. dnscrypt sonradan ölürse makine hiçbir adı çözemez.
//
// /F'siz taskkill WM_CLOSE gönderiyor; uygulamanın pencere kapanma yolu winws'i
// durdurup DNS'i geri alıyor. Zorla öldürme yalnızca kapanmayan bir süreç için,
// son çare olarak kalıyor; kurulumun dosya kilidi yüzünden yarım kalması da
// kabul edilebilir değil.
// Akıllı Uygulama Denetimi ZORLAMA modunda mı.
//
// Değer: 0 kapalı, 1 açık (zorlama), 2 değerlendirme. ÖLÇÜLDÜ (2026-09-16):
// değerlendirme modundaki bir makinede 0.2.4 hatasız kuruldu ve çalıştı; SAC
// AÇIK bir makinede 0.2.5 taslağının kurulumu ZapretTR.exe'yi çalıştıramadı
// (hata 4551). Aynı açık makinede bu kontrol 1 okudu ve uyarı kurulumun başında
// çıktı; "1 = açık" eşlemesi böylece gerçek makinede de doğrulandı.
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
  // SAC açıksa kurulum BAŞTAN söylemeli. Eskiden kullanıcı önce kuruyor, sonra
  // "exe çalıştırılamadı" kutusunu görüyor ve elinde düzgün çalışmayan bir uygulama
  // kalıyordu. Kod imzası olmadan bu engeli aşmanın yolu yok; yapabildiğimiz tek
  // şey kullanıcıyı kurmadan önce bilgilendirmek. Sessiz kurulumda (CI) soru
  // sorulmuyor ve kurulum sürüyor.
  if SacAcik() then
  begin
    Mesaj := 'Bu bilgisayarda Akilli Uygulama Denetimi ACIK.' + #13#10 + #13#10;
    Mesaj := Mesaj + 'ZapretTR''in duzgun calismasi icin bu ozelligin KAPALI olmasi gerekiyor.'
             + ' ZapretTR''in ve kullandigi araclarin kod imzasi yok; ozellik acikken Windows'
             + ' onlari engelliyor (hata 4551). Pencere acilsa bile otomatik baslatma,'
             + ' DNS bekcisi ve koruma calismayabilir.' + #13#10 + #13#10;
    Mesaj := Mesaj + 'Kurmadan once, SIRASIYLA:' + #13#10;
    Mesaj := Mesaj + '1) Indirdiginiz kurulum dosyasina sag tiklayin -> Ozellikler ->' + #13#10;
    Mesaj := Mesaj + '   alttaki "Engellemeyi Kaldir" kutusunu isaretleyip Tamam deyin.' + #13#10;
    Mesaj := Mesaj + '2) Windows Guvenligi -> Uygulama ve tarayici denetimi ->' + #13#10;
    Mesaj := Mesaj + '   Akilli Uygulama Denetimi ayarlari -> Kapali.' + #13#10;
    Mesaj := Mesaj + '3) Kurulumu yeniden calistirin.' + #13#10 + #13#10;
    Mesaj := Mesaj + 'Not: bu ozellik kapatilinca bazi Windows surumlerinde yeniden'
             + ' acilamiyor. Microsoft Defender calismaya devam eder.' + #13#10 + #13#10;
    Mesaj := Mesaj + 'Yine de simdi kurulsun mu?';

    if SuppressibleMsgBox(Mesaj, mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDYES) <> IDYES then
    begin
      Result := False;
      Exit;
    end;
  end;

  Exec(ExpandConstant('{cmd}'), '/c taskkill /IM ZapretTR.exe',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Temizliğin bitmesi için süre tanınıyor: winws'in durması ve DNS'in geri
  // alınması anlık değil.
  Sleep(4000);

  Exec(ExpandConstant('{cmd}'), '/c taskkill /IM ZapretTR.exe /F',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

// Dosyalar değiştirilmeden ÖNCE önceki kurulumun sürücüsünü çekirdekten kaldır.
//
// Gerçek bir kullanıcıda görüldü: bir test koşumundan sonra WinDivert sürücüsü
// çekirdekte asılı kalıyor ve WinDivert64.sys kilitleniyor. Yükseltme o dosyayı
// değiştiremiyor ve kurulum şu hatayı veriyor:
//
//   "Var olan dosya değiştirilirken sorun çıktı:
//    DeleteFile tamamlanamadı; kod 5. Erişim engellendi."
//
// Kullanıcıya kalan tek seçenek "bu dosya atlansın" oluyor; yani eski sürücü
// dosyasıyla devam etmek. Aynı kilit, kaldırmadan sonra da klasörde kalıntı
// bırakıyor ve bir sonraki kurulum aynı duvara tosluyor.
//
// PrepareToInstall doğru kanca: kurulum yeri artık belli ({app} çözülebiliyor)
// ama dosya kopyalama henüz başlamadı.
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

  // Durum, sökme İŞLEMİNDEN ÖNCE okunmalı; sonra bakmanın anlamı olmaz.
  ServisGeriKurulacak := ServisKurulu('ZapretTR');

  // 1) Önceki sürümün kendi temizliği: servisleri söker ve DNS'i geri alır.
  //    Kullanıcı ayarları ve öğrenilmiş doğrulamalar KORUNUR.
  //
  //    SONUCU DENETLENİYOR ve denetlenmesi ZORUNLU. Eskiden burada Exec'in dönüş
  //    değeri de ResultCode da yok sayılıyordu. Gerçek bir makinede ölçüldü
  //    (2026-09-16, 0.2.3 kurulumu): Akıllı Uygulama Denetimi açıkken Windows
  //    ZapretTR.exe'yi çalıştırmıyor (hata 4551, "An Application Control policy
  //    has blocked this file"); yani bu çağrı başarısız oluyor, servisler
  //    SÖKÜLMÜYOR ve kimse fark etmiyor. Kullanıcının bildirimi: "güncelleme
  //    sırasında servisleri kapamıyor".
  TemizlikYapildi := False;
  if FileExists(OncekiExe) then
  begin
    TemizlikYapildi :=
      Exec(OncekiExe, '--uninstall-services', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
      and (ResultCode = 0);
  end;

  // 1b) Exe çalışmadıysa servisleri KURULUM kendisi söker.
  //
  //     sc.exe Microsoft imzalı; SAC onu engellemiyor, dolayısıyla bu yol imzasız
  //     exe'mize hiç bağlı değil. Tek başına yetmiyor: exe'nin yaptığı DNS geri
  //     almayı sc.exe yapamaz; ama servisin ayakta kalıp dosyaları kilitlemesi ve
  //     yükseltmenin yarım kalması bundan daha kötü.
  //
  //     DNS geri alma bu durumda DNS bekçisine kalıyor: görev SYSTEM olarak koşuyor
  //     ve çözümleyici yoksa yönlendirmeyi zaten geri alıyor.
  if not TemizlikYapildi then
  begin
    Exec(Sc, 'stop ZapretTR', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(Sc, 'stop ZapretTR-DNS', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    SurucuDussun('ZapretTR');
    SurucuDussun('ZapretTR-DNS');
    Exec(Sc, 'delete ZapretTR', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(Sc, 'delete ZapretTR-DNS', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;

  // 2) Sürücüyü KURULUM KENDİSİ kaldırır; önceki exe'ye güvenmez.
  //
  //    Sebep: bu adım eski sürüme DEVREDİLEMEZ. Sürücü kaldırma davranışı
  //    0.1.4'te eklendi, dolayısıyla 0.1.3 ve öncesinden yükseltirken çağrılan
  //    exe onu YAPMIYOR. Tam da düzeltmeye çalıştığımız kullanıcılar eski sürümde
  //    olacağı için kurulumun kendi ayakları üzerinde durması şart.
  //
  //    sc.exe çağrıları WinDivertCleanup'ın yaptığının aynısı. (Sc yukarıda atandı.)

  // winws sürücüyü açık tutuyor olabilir; önce o gitmeli.
  Exec(ExpandConstant('{cmd}'), '/c taskkill /IM winws.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{cmd}'), '/c taskkill /IM dnscrypt-proxy.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Sürücüye dokunmadan önce sürecin GERÇEKTEN bitmesini bekle; gerekçesi SurecBitsin'de.
  SurecBitsin('winws.exe');

  // Üç ad birden: WinDivert'i başka araçlar da kuruyor. WinDivert14 GoodbyeDPI'ın
  // ve WinDivert 1.4'ün adı, monkey bazı dağıtımlarınki. Biri geride kalıp sürücüsü
  // çekirdeğe yüklü duruyorsa dosya yine kilitli kalır ve aynı kod 5 hatası döner.
  Exec(Sc, 'stop windivert', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  SurucuDussun('windivert');
  Exec(Sc, 'delete windivert', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(Sc, 'stop WinDivert14', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  SurucuDussun('WinDivert14');
  Exec(Sc, 'delete WinDivert14', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(Sc, 'stop monkey', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  SurucuDussun('monkey');
  Exec(Sc, 'delete monkey', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Sürücü görüntüsünün çekirdekten düşmesi anlık değil.
  Sleep(2000);
end;

// Sökülen servisi geri kur. Dosyalar yerine geçtikten SONRA, çünkü servis yeni
// ikiliyi göstermeli.
//
// Geri kurma sessizce başarısız olmamalı: olursa kullanıcı korunduğunu sanarak
// korumasız kalır; düzeltmeye çalıştığımız şey de tam olarak bu.
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  Exe: String;
  Mesaj: String;
begin
  // ServisGeriKurulacak koşulu ARTIK BURADA DEĞİL: DNS bekçisi, servis kurulu
  // olmasa da her kurulumda yazılmalı (gerekçesi DnsGuard.cs). Eskiden bu satır
  // "not ServisGeriKurulacak" ile de çıkıyordu ama bekçi [Run] içindeydi, yani
  // ayrı koşuyordu; ikisi burada birleştiği için koşul daraltıldı.
  if CurStep <> ssPostInstall then
    Exit;

  Exe := ExpandConstant('{app}\{#AppExe}');
  Engellendi := False;

  // 1) DNS bekçisi görevi. Her kurulumda yeniden yazılıyor ki görev YENİ exe'yi
  //    göstersin. Gerekçesi DnsGuard.cs'de: servis modunda arkada DNS'i izleyen
  //    başka hiçbir şey yok.
  if not Exec(Exe, '--register-dns-guard', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Engellendi := True;

  // 2) Sökülen otomatik başlatma servisini geri kur. Sessizce başarısız olmamalı:
  //    olursa kullanıcı korunduğunu sanarak korumasız kalır.
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

  // 3) Kurulum biterken bir bekçi turu: yükseltme sırasında DNS geri alınamamışsa
  //    sistem 127.0.0.1'de çözümleyicisiz kalmış olabilir ve bir sonraki
  //    tetikleyiciyi (en geç 10 dk) beklemeye gerek yok. nowait: bekçi
  //    çözümleyiciyi 90 sn'ye kadar bekleyebiliyor, kurulum beklememeli.
  Exec(Exe, '--dns-guard', '', SW_HIDE, ewNoWait, ResultCode);

  // Exe HİÇ çalışmadıysa sebebi neredeyse her zaman Akıllı Uygulama Denetimi
  // (hata 4551). Kullanıcı bunu ham CreateProcess hatası olarak görmesin: ne
  // olduğunu ve ne yapacağını TEK bir yerde, sırasıyla söylüyoruz.
  //
  // İki adım da gerekli ve ikincisini gerçek kullanıcı bulup bildirdi
  // (2026-09-16): yalnızca SAC'yi kapatmak YETMEDİ, indirilen dosyanın
  // "Engellemeyi Kaldır" işaretinin de temizlenmesi gerekti.
  if Engellendi and (not WizardSilent()) then
  begin
    // Metin değişkene yazılıyor ve HİÇBİR SATIR '#' ile BAŞLAMIYOR. Sebep
    // ölçüldü: ISCC'nin önişlemcisi satır başındaki '#' karakterini yönerge
    // sayıyor, dolayısıyla '#13#10' ile başlayan bir devam satırı derlemeyi
    // "Unknown preprocessor directive" ile durduruyor. Bu, metin testlerinin
    // göremediği bir hata sınıfı; yalnızca gerçek derleme yakalıyor.
    Mesaj := 'Windows, ZapretTR.exe dosyasini calistirmadi'
             + ' (Akilli Uygulama Denetimi, hata 4551).' + #13#10 + #13#10;
    // "ZapretTR kurulu" yazıyordu; doğru ama yanıltıcı. "Açılmaz" da YANLIŞ çıktı:
    // aynı makinede (SAC açık, 2026-09-16) kurulum exe'yi çalıştıramadı ama kullanıcı
    // uygulamayı sonradan açabildi. Ölçülen: servis ve bekçi kurulamadı; winws.exe
    // ve dnscrypt-proxy.exe de imzasız.
    Mesaj := Mesaj + 'Dosyalar kopyalandi, ama bu ayar acikken ZapretTR duzgun calismaz:'
             + ' otomatik baslatma servisi ve DNS bekcisi kurulamadi.' + #13#10 + #13#10;
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
