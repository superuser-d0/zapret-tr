using System.Runtime.Versioning;
using System.Security.Principal;

namespace ZapretTr.Core.Engine;

/// <summary>
/// Yonetici yetkisi kontrolu.
/// </summary>
/// <remarks>
/// winws.exe kernel modunda calisan WinDivert surucusunu kullaniyor ve yukseltilmis
/// yetki olmadan hicbir sey yapmiyor -- <c>--help</c> bile "requires elevation" ile
/// dusuyor. Bu yuzden uygulamanin tamami elevated calismak zorunda; yetkiyi sonradan
/// istemek diye bir secenek yok.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class ElevationGuard
{
    /// <summary>Surecin yonetici olarak calisip calismadigi.</summary>
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Yetki yoksa acik bir mesajla dusurur.
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
