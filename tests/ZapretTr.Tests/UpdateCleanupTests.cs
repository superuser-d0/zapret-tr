using System.IO;
using ZapretTr.Core.Engine;
using IoPath = System.IO.Path;

namespace ZapretTr.Tests;

/// <summary>
/// Guncelleme klasorundeki eski kurulum paketlerinin temizligi.
/// </summary>
/// <remarks>
/// Guncelleme indirdigi paketi hic silmiyordu; gercek bir makinede
/// <c>%TEMP%\ZapretTR-guncelleme</c> icinde alti paket (yaklasik 330 MB) birikmisti.
/// Silme kodunun asil tehlikesi ters yonde: yanlis dosyayi silmek. Bu yuzden
/// testlerin cogu neyin SILINMEDIGINE bakiyor.
/// </remarks>
public sealed class UpdateCleanupTests : IDisposable
{
    private readonly string _klasor =
        IoPath.Combine(IoPath.GetTempPath(), "zapret-tr-test-" + Guid.NewGuid().ToString("N"));

    public UpdateCleanupTests() => Directory.CreateDirectory(_klasor);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_klasor, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Eski_kurulum_paketleri_siliniyor_ve_boyutlari_sayiliyor()
    {
        Yaz("ZapretTR-Setup-0.1.16.exe", 100);
        Yaz("ZapretTR-Setup-0.1.22.exe", 250);

        var sonuc = UpdateDownloader.DeleteOldInstallers(_klasor);

        Assert.Equal(2, sonuc.Deleted);
        Assert.Equal(350, sonuc.Bytes);
        Assert.Equal(0, sonuc.Skipped);
        Assert.Empty(Directory.EnumerateFiles(_klasor));
    }

    [Fact]
    public void Desene_uymayan_dosyalara_dokunulmuyor()
    {
        Yaz("ZapretTR-Setup-0.1.22.exe", 10);
        Yaz("ZapretTR-Setup-0.1.22.exe_", 10);   // Win32'nin eski "*.exe" deseni buna uyardi
        Yaz("ZapretTR-Setup-0.1.22.txt", 10);
        Yaz("baska-bir-kurulum.exe", 10);
        Directory.CreateDirectory(IoPath.Combine(_klasor, "alt"));
        File.WriteAllBytes(IoPath.Combine(_klasor, "alt", "ZapretTR-Setup-0.1.1.exe"), new byte[10]);

        var sonuc = UpdateDownloader.DeleteOldInstallers(_klasor);

        Assert.Equal(1, sonuc.Deleted);
        Assert.False(File.Exists(IoPath.Combine(_klasor, "ZapretTR-Setup-0.1.22.exe")));
        Assert.True(File.Exists(IoPath.Combine(_klasor, "ZapretTR-Setup-0.1.22.exe_")));
        Assert.True(File.Exists(IoPath.Combine(_klasor, "ZapretTR-Setup-0.1.22.txt")));
        Assert.True(File.Exists(IoPath.Combine(_klasor, "baska-bir-kurulum.exe")));
        Assert.True(File.Exists(IoPath.Combine(_klasor, "alt", "ZapretTR-Setup-0.1.1.exe")));
    }

    [Fact]
    public void Kullanimdaki_paket_atlaniyor_ve_digerleri_yine_siliniyor()
    {
        // Calisan kurulum kendi dosyasini boyle kilitli tutuyor.
        Yaz("ZapretTR-Setup-0.1.21.exe", 10);
        Yaz("ZapretTR-Setup-0.1.22.exe", 10);

        using (new FileStream(IoPath.Combine(_klasor, "ZapretTR-Setup-0.1.22.exe"),
                   FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var sonuc = UpdateDownloader.DeleteOldInstallers(_klasor);

            Assert.Equal(1, sonuc.Deleted);
            Assert.Equal(1, sonuc.Skipped);
        }

        Assert.True(File.Exists(IoPath.Combine(_klasor, "ZapretTR-Setup-0.1.22.exe")));
    }

    [Fact]
    public void Klasor_yoksa_hata_vermiyor()
    {
        var sonuc = UpdateDownloader.DeleteOldInstallers(IoPath.Combine(_klasor, "yok"));

        Assert.Equal(new InstallerCleanupResult(0, 0, 0), sonuc);
    }

    private void Yaz(string ad, int boyut)
        => File.WriteAllBytes(IoPath.Combine(_klasor, ad), new byte[boyut]);
}
