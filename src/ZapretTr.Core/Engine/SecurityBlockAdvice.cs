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
/// 2026-09: "Windows güncellemesinden sonra Defender ZapretTR'i siliyor, kurulum bile
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

        return tur switch
        {
            SecurityBlockKind.Antivirus =>
                $"{fileName} antivirüs tarafından engellendi {windows}. " +
                "Microsoft Defender ve bazı antivirüsler, imzasız olan ve ağ sürücüsü kullanan " +
                "bu dosyayı yanlışlıkla zararlı sayabiliyor. Çözüm: Windows Güvenliği → Virüs ve " +
                "tehdit koruması → Koruma geçmişi'nde ZapretTR kaydını açıp \"İzin ver\" deyin, " +
                "sonra kurulum paketini yeniden çalıştırın. Başka bir antivirüs kullanıyorsanız " +
                "onun karantinasına bakın. Adımlar: docs/SORUN-GIDERME.md, \"Windows engelliyor\".",

            SecurityBlockKind.ApplicationControl =>
                $"{fileName} Windows'un Akıllı Uygulama Denetimi (Smart App Control) tarafından " +
                $"engellendi {windows}. Bu özellik imzasız uygulamaları çalıştırmıyor ve tek tek " +
                "istisna tanımlanamıyor; ZapretTR'in kod imzalama sertifikası yok. Tek yol Windows " +
                "Güvenliği → Uygulama ve tarayıcı denetimi → Akıllı Uygulama Denetimi ayarları'ndan " +
                "özelliği kapatmak. Kurumsal bilgisayarda bu bir BT ilkesi olabilir; o durumda " +
                "yöneticinize danışın. Adımlar: docs/SORUN-GIDERME.md, \"Windows engelliyor\".",

            _ =>
                $"{fileName} bir grup ilkesi tarafından engellendi {windows}. Bu genellikle " +
                "kurumsal bilgisayarlarda BT yöneticisinin koyduğu bir kısıtlamadır; ZapretTR " +
                "tarafından aşılamaz. Yöneticinize danışın.",
        };
    }
}
