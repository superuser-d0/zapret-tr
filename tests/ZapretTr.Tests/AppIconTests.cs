using System.IO;
using IoPath = System.IO.Path;

namespace ZapretTr.Tests;

/// <summary>
/// Uygulamanin kendi simgesi exe'de, bildirim alaninda ve kurulum paketinde olmali.
/// </summary>
/// <remarks>
/// 0.1.21'e kadar simge yoktu ve bu hicbir derlemede ya da testte gorunmedi: exe,
/// gorev cubugu ve bildirim alani .NET'in varsayilan simgesini, kurulum sihirbazi
/// Inno Setup'in resmini gosteriyordu. Eksik bir simge uygulamayi bozmaz; o yuzden
/// kimse fark etmez ve bir "sadelestirme" onu sessizce geri goturebilir.
/// </remarks>
public sealed class AppIconTests
{
    private static string AppDir => IoPath.Combine(XmlCommentTests.RepoRoot, "src", "ZapretTr.App");

    [Fact]
    public void Bildirim_alani_icin_gomulu_simge_var_ve_kucuk_boyutlari_iceriyor()
    {
        using var akis = typeof(global::ZapretTr.App.TrayIcon).Assembly
            .GetManifestResourceStream("ZapretTR.ico");

        Assert.NotNull(akis);

        var boyutlar = IcoBoyutlari(akis!);

        // 16/20/24: %100, %125, %150 olcekte bildirim alani. Eksikse Windows daha
        // buyugunu kucultur ve Z bulaniklasir -- simgenin ayri cizilme sebebi bu.
        foreach (var beklenen in new[] { 16, 20, 24, 32, 48, 256 })
        {
            Assert.Contains(beklenen, boyutlar);
        }
    }

    [Fact]
    public void Exe_simgesi_projede_tanimli()
    {
        var csproj = File.ReadAllText(IoPath.Combine(AppDir, "ZapretTr.App.csproj"));

        Assert.Contains(@"<ApplicationIcon>Assets\ZapretTR.ico</ApplicationIcon>", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void Kurulum_paketi_simgeyi_ve_sihirbaz_resmini_kullaniyor()
    {
        var installerDir = IoPath.Combine(XmlCommentTests.RepoRoot, "installer");
        var iss = File.ReadAllLines(IoPath.Combine(installerDir, "setup.iss"));

        string Deger(string anahtar) =>
            iss.Select(l => l.Trim())
               .FirstOrDefault(l => l.StartsWith(anahtar + "=", StringComparison.Ordinal))
               ?.Substring(anahtar.Length + 1)
            ?? throw new InvalidOperationException($"setup.iss'te {anahtar} yok.");

        // Yollar .iss'e gore; dosya yoksa ISCC CI'da derlemeyi durdurur, ama bu test
        // bunu yerelde, paketi uretmeden de yakaliyor.
        Assert.True(File.Exists(IoPath.Combine(installerDir, Deger("SetupIconFile"))));

        foreach (var resim in Deger("WizardSmallImageFile").Split(','))
        {
            Assert.True(File.Exists(IoPath.Combine(installerDir, resim.Trim())), resim);
        }
    }

    /// <summary>.ico dizinindeki goruntu boyutlarini okur (0 = 256).</summary>
    private static List<int> IcoBoyutlari(Stream akis)
    {
        using var okuyucu = new BinaryReader(akis);

        Assert.Equal(0, okuyucu.ReadUInt16());
        Assert.Equal(1, okuyucu.ReadUInt16());

        var adet = okuyucu.ReadUInt16();
        var boyutlar = new List<int>();

        for (var i = 0; i < adet; i++)
        {
            var genislik = okuyucu.ReadByte();
            okuyucu.ReadBytes(15);
            boyutlar.Add(genislik == 0 ? 256 : genislik);
        }

        return boyutlar;
    }
}
