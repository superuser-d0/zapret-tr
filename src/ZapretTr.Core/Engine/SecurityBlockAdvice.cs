using System.ComponentModel;
using System.Text.RegularExpressions;

namespace ZapretTr.Core.Engine;

/// <summary>Bir dosyanın çalışmasını hangi Windows koruması engelledi.</summary>
public enum SecurityBlockKind
{
    /// <summary>Microsoft Defender (ya da başka bir antivirüs) dosyayı zararlı saydı.</summary>
    Antivirus,

    /// <summary>Akıllı Uygulama Denetimi ya da bir uygulama denetimi (WDAC) ilkesi.</summary>
    ApplicationControl,

    /// <summary>Grup ilkesi (kurumsal bilgisayar).</summary>
    GroupPolicy,
}

/// <summary>
/// Windows bir ikiliyi güvenlik gerekçesiyle başlatmadığında kullanıcıya ne olduğunu
/// ve ne yapacağını söyler.
/// </summary>
/// <remarks>
/// <para>
/// 2026-09: "Windows güncellemesinden sonra Defender ZapretTR'yi siliyor, kurulum bile
/// açılmıyor" duyumu geldi. Ölçülen: Defender aynı gün zapret2 arşivini indirme anında
/// <c>Trojan:Win32/Tecabans.STV!cl</c> diye silmişti (bulut makine öğrenmesi, FastPath);
/// geliştirici makinesinde Akıllı Uygulama Denetimi "Değerlendirme" modunda. İkisi de
/// winws.exe, cygwin1.dll ve dnscrypt-proxy.exe gibi İMZASIZ ikilileri hedef alabiliyor.
/// </para>
/// <para>
/// Eskiden bu durumda günlükte yalnızca Windows'un kendi cümlesi kalıyordu ("Bir
/// Uygulama Denetimi ilkesi bu dosyayı engelledi"); hangi ürünün engellediği, dosyanın
/// bizden geldiği ve çözüm yolu yazmıyordu. Kullanıcıya görünen sonuç "Başlat
/// çalışmıyor"du.
/// </para>
/// <para>
/// Kodlar winerror.h'den; anlamları bu makinede <see cref="Win32Exception"/> ile
/// doğrulandı. 4556 ve 4580-4582, Akıllı Uygulama Denetimi'nin itibar kararları.
/// </para>
/// </remarks>
public static class SecurityBlockAdvice
{
    /// <summary>ERROR_VIRUS_INFECTED: dosya virüs ya da istenmeyen yazılım içeriyor.</summary>
    public const int VirusInfected = 225;

    /// <summary>ERROR_VIRUS_DELETED: dosya aynı sebeple yerinden silindi.</summary>
    public const int VirusDeleted = 226;

    /// <summary>ERROR_ACCESS_DISABLED_BY_POLICY: grup ilkesi engelledi.</summary>
    public const int BlockedByGroupPolicy = 1260;

    /// <summary>ERROR_SYSTEM_INTEGRITY_POLICY_VIOLATION: uygulama denetimi ilkesi engelledi.</summary>
    public const int ApplicationControlBlocked = 4551;

    private static readonly int[] ApplicationControlCodes = [ApplicationControlBlocked, 4556, 4580, 4581, 4582];

    /// <summary>Hata kodu bir güvenlik engeliyse türünü döndürür, değilse null.</summary>
    public static SecurityBlockKind? Classify(int nativeErrorCode) => nativeErrorCode switch
    {
        VirusInfected or VirusDeleted => SecurityBlockKind.Antivirus,
        BlockedByGroupPolicy => SecurityBlockKind.GroupPolicy,
        _ when ApplicationControlCodes.Contains(nativeErrorCode) => SecurityBlockKind.ApplicationControl,
        _ => null,
    };

    /// <summary>
    /// Süreç başlatma hatası bir güvenlik engeliyse kullanıcıya gösterilecek metni
    /// döndürür, değilse null.
    /// </summary>
    /// <param name="exception"><see cref="System.Diagnostics.Process.Start()"/> hatası.</param>
    /// <param name="fileName">Engellenen dosyanın adı (ör. winws.exe).</param>
    public static string? Describe(Win32Exception exception, string fileName)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Describe(exception.NativeErrorCode, fileName, exception.Message);
    }

    /// <summary>
    /// <c>sc start</c> çıktısındaki "FAILED &lt;kod&gt;" bir güvenlik engeliyse metni
    /// döndürür, değilse null.
    /// </summary>
    /// <param name="scOutput">sc.exe'nin tüm çıktısı.</param>
    /// <param name="fileName">Servisin çalıştırdığı dosyanın adı.</param>
    public static string? DescribeScOutput(string? scOutput, string fileName)
    {
        if (string.IsNullOrWhiteSpace(scOutput))
        {
            return null;
        }

        var eslesme = Regex.Match(scOutput, @"FAILED\s+(\d+)\s*:");
        return eslesme.Success && int.TryParse(eslesme.Groups[1].Value, out var kod)
            ? Describe(kod, fileName, windowsMessage: null)
            : null;
    }

    /// <summary>STATUS_INVALID_IMAGE_HASH: yüklenen bir dosyanın imzası kod bütünlüğünden geçmedi.</summary>
    public const int InvalidImageHashStatus = unchecked((int)0xC0000428);

    /// <summary>
    /// Süreç açılır açılmaz bir güvenlik engeliyle kapandıysa kullanıcıya gösterilecek
    /// metni döndürür, değilse null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Describe(Win32Exception, string)"/> yalnızca exe'nin KENDİSİNİN
    /// engellendiği durumu görüyor. Akıllı Uygulama Denetimi exe'ye izin verip onun
    /// yüklediği imzasız bir DLL'i engellerse <c>Process.Start</c> başarılı dönüyor ve
    /// süreç yükleyicinin NTSTATUS koduyla kapanıyor. 2026-09-15'te bir kullanıcı
    /// ZapretTR.dll için tam bu engeli gördü ("Bu uygulamanın bir kısmı engellendi").
    /// winws.exe'nin yüklediği cygwin1.dll ve WinDivert.dll de imzasız.
    /// </para>
    /// <para>
    /// NTSTATUS → Win32 eşlemeleri geliştirici makinesinde <c>RtlNtStatusToDosError</c>
    /// ile bütün 0xC00xxxxx aralığı taranarak ölçüldü: 0xC0000428 → 577,
    /// 0xC0000906/0xC0000907 → 225/226, 0xC0000361-0xC0000364 → 1260. 4551 ailesine
    /// eşlenen yerel bir NTSTATUS YOK; kod bütünlüğünün DLL reddi yükleyicide
    /// 0xC0000428 olarak görünüyor.
    /// </para>
    /// <para>
    /// 4551'in kendisi ARTIK ÖLÇÜLDÜ (2026-09-16, gerçek makine, 0.2.3 kurulumu):
    /// Akıllı Uygulama Denetimi açıkken Windows hem Inno'nun <c>%TEMP%</c>'teki
    /// <c>setup.tmp</c>'sini hem de kurulmuş <c>ZapretTR.exe</c>'yi çalıştırmadı:
    /// "CreateProcess tamamlanamadı; kod 4551. Uygulama Denetimi ilkesi bu dosyayı
    /// engelledi." Yani bu kod bir varsayım değil, görülmüş bir durum.
    /// </para>
    /// </remarks>
    /// <param name="exitCode"><see cref="System.Diagnostics.Process.ExitCode"/>.</param>
    /// <param name="fileName">Kapanan dosyanın adı (ör. winws.exe).</param>
    public static string? DescribeExitCode(int exitCode, string fileName)
    {
        var tur = unchecked((uint)exitCode) switch
        {
            0xC0000428 => SecurityBlockKind.ApplicationControl,
            0xC0000906 or 0xC0000907 => SecurityBlockKind.Antivirus,
            >= 0xC0000361 and <= 0xC0000364 => SecurityBlockKind.GroupPolicy,
            _ => (SecurityBlockKind?)null,
        };

        if (tur is null)
        {
            return null;
        }

        var kod = $"(çıkış kodu 0x{unchecked((uint)exitCode):X8})";
        var bas = tur switch
        {
            SecurityBlockKind.ApplicationControl =>
                $"{fileName} açılır açılmaz kapandı {kod}: yüklediği imzasız bir DLL'in yayımcısı " +
                "doğrulanamadığı için Windows onu engelledi. Bildirimde \"Bu uygulamanın bir kısmı " +
                "engellendi\" yazar; bu Windows'un Akıllı Uygulama Denetimi (Smart App Control). ",
            SecurityBlockKind.Antivirus =>
                $"{fileName} açılır açılmaz kapandı {kod}: yüklediği bir dosya antivirüs tarafından engellendi. ",
            _ => $"{fileName} açılır açılmaz kapandı {kod}: yüklediği bir dosya grup ilkesiyle engellendi. ",
        };

        return bas + Solution(tur.Value);
    }

    private static string? Describe(int code, string fileName, string? windowsMessage)
    {
        var tur = Classify(code);
        if (tur is null)
        {
            return null;
        }

        var windows = string.IsNullOrWhiteSpace(windowsMessage)
            ? $"(Windows hata kodu {code})"
            : $"(Windows: \"{windowsMessage.Trim()}\", kod {code})";

        var bas = tur switch
        {
            SecurityBlockKind.Antivirus => $"{fileName} antivirüs tarafından engellendi {windows}. ",
            SecurityBlockKind.ApplicationControl =>
                $"{fileName} Windows'un Akıllı Uygulama Denetimi (Smart App Control) tarafından engellendi {windows}. ",
            _ => $"{fileName} bir grup ilkesi tarafından engellendi {windows}. ",
        };

        return bas + Solution(tur.Value);
    }

    private static string Solution(SecurityBlockKind kind)
    {
        return kind switch
        {
            SecurityBlockKind.Antivirus =>
                "Microsoft Defender ve bazı antivirüsler, imzasız olan ve ağ sürücüsü kullanan " +
                "bu dosyayı yanlışlıkla zararlı sayabiliyor. Çözüm: Windows Güvenliği → Virüs ve " +
                "tehdit koruması → Koruma geçmişi'nde ZapretTR kaydını açıp \"İzin ver\" deyin, " +
                "sonra kurulum paketini yeniden çalıştırın. Başka bir antivirüs kullanıyorsanız " +
                "onun karantinasına bakın. Adımlar: docs/SORUN-GIDERME.md, \"Windows engelliyor\".",

            // İki adım da gerekli ve İKİNCİSİ TEK BAŞINA YETMİYOR. Gerçek makinede
            // ölçüldü (2026-09-16, 0.2.3 kurulumu): kullanıcı Akıllı Uygulama
            // Denetimi'ni kapattı ve kurulum yine engellendi; ancak indirdiği
            // dosyanın "Engellemeyi Kaldır" işaretini de temizleyince geçti.
            // Önceki metin yalnızca SAC'den bahsediyordu, yani kullanıcıyı
            // "yaptım, yine olmadı" noktasında bırakıyordu.
            SecurityBlockKind.ApplicationControl =>
                "Bu özellik imzasız uygulamaları çalıştırmıyor ve tek tek " +
                "istisna tanımlanamıyor; ZapretTR'in kod imzalama sertifikası yok. Sırasıyla: " +
                "(1) indirdiğiniz kurulum dosyasına sağ tıklayıp Özellikler'i açın ve alttaki " +
                "\"Engellemeyi Kaldır\" kutusunu işaretleyip Tamam deyin; (2) Windows Güvenliği → " +
                "Uygulama ve tarayıcı denetimi → Akıllı Uygulama Denetimi ayarları'ndan özelliği " +
                "kapatın; (3) kurulumu yeniden çalıştırın. Yalnızca ikincisini yapmak yetmeyebilir. " +
                "Kurumsal bilgisayarda bu bir BT ilkesi olabilir; o durumda yöneticinize danışın. " +
                "Adımlar: docs/SORUN-GIDERME.md, \"Windows engelliyor\".",

            _ =>
                "Bu genellikle " +
                "kurumsal bilgisayarlarda BT yöneticisinin koyduğu bir kısıtlamadır; ZapretTR " +
                "tarafından aşılamaz. Yöneticinize danışın.",
        };
    }
}
