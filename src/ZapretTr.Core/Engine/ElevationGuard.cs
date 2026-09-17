using System.Runtime.Versioning;
using System.Security.Principal;

namespace ZapretTr.Core.Engine;

/// <summary>
/// Yönetici yetkisi kontrolü.
/// </summary>
/// <remarks>
/// winws.exe çekirdek modunda çalışan WinDivert sürücüsünü kullanıyor ve yükseltilmiş
/// yetki olmadan hiçbir şey yapmıyor; <c>--help</c> bile "requires elevation" ile
/// düşüyor. Bu yüzden uygulamanın tamamı yükseltilmiş yetkiyle çalışmak zorunda;
/// yetkiyi sonradan istemek diye bir seçenek yok.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class ElevationGuard
{
    /// <summary>Sürecin yönetici olarak çalışıp çalışmadığı.</summary>
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Yetki yoksa açık bir mesajla düşürür.
    /// </summary>
    public static void EnsureElevated()
    {
        if (!IsElevated())
        {
            throw new UnauthorizedAccessException(
                "ZapretTR yonetici yetkisiyle calismali. winws.exe kernel modunda calisan " +
                "WinDivert surucusunu kullaniyor ve yukseltilmis yetki olmadan baslatilamiyor.");
        }
    }
}
