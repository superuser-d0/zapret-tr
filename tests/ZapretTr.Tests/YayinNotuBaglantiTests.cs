using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using IoPath = System.IO.Path;

namespace ZapretTr.Tests;

/// <summary>
/// Yayin notundaki baglantilar yayin sayfasindan tiklaninca calismali.
/// </summary>
/// <remarks>
/// OLCULDU (2026-09-16): yayin sayfasindaki "CHANGELOG.md" baglantisi
/// "404 - Cannot find a valid ref in blob/main/CHANGELOG.md" veriyordu. Not
/// ".../releases/tag/vX" adresinde gosteriliyor ve GitHub goreli baglantiyi oraya gore
/// cozuyor; "../blob/main/CHANGELOG.md" gecersiz bir adrese gidiyordu. 29 yayinin
/// hepsinde vardi. CHANGELOG'daki depo ici goreli baglantilar da notta ayni sekilde
/// bozuluyordu.
///
/// Uretici betik GERCEKTEN kosuluyor (powershell.exe); betigin yaptigi donusumun
/// metnine bakmak, calistigini gostermez.
/// </remarks>
public sealed class YayinNotuBaglantiTests
{
    private static readonly Regex GoreliBaglanti =
        new(@"\]\((?!https?://|#|mailto:)[^)]+\)", RegexOptions.Compiled);

    [Fact]
    public void Uretilen_Notta_Goreli_Baglanti_Kalmiyor()
    {
        var dizin = IoPath.Combine(IoPath.GetTempPath(), "zapret-tr-notlar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dizin);
        try
        {
            var changelog = IoPath.Combine(dizin, "CHANGELOG.md");
            File.WriteAllText(changelog,
                "# Degisiklik\n\n## [9.9.9]\n\n- Bkz. [rehber](docs/SORUN-GIDERME.md#dns-bekcisi-gorevi), "
                + "[yerel](./README.md), [sayfa ici](#ust), [dis](https://example.com/x).\n\n## [9.9.8]\n\n- eski\n",
                new UTF8Encoding(false));

            var cikti = IoPath.Combine(dizin, "not.md");
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = XmlCommentTests.RepoRoot,
            };
            foreach (var a in new[]
                     {
                         "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                         IoPath.Combine(XmlCommentTests.RepoRoot, "tools", "release-notes.ps1"),
                         "-Version", "9.9.9", "-ChangelogPath", changelog, "-OutPath", cikti,
                     })
            {
                psi.ArgumentList.Add(a);
            }

            using var p = Process.Start(psi)!;
            var hata = p.StandardError.ReadToEndAsync();
            _ = p.StandardOutput.ReadToEnd();
            Assert.True(p.WaitForExit(60000), "release-notes.ps1 60 saniyede bitmedi.");
            Assert.True(p.ExitCode == 0, "release-notes.ps1 basarisiz: " + hata.Result);

            var not = File.ReadAllText(cikti);

            Assert.Empty(GoreliBaglanti.Matches(not).Select(m => m.Value));
            Assert.Contains("](https://github.com/superuser-d0/zapret-tr/blob/main/docs/SORUN-GIDERME.md#dns-bekcisi-gorevi)", not, StringComparison.Ordinal);
            Assert.Contains("](https://github.com/superuser-d0/zapret-tr/blob/main/README.md)", not, StringComparison.Ordinal);
            Assert.Contains("](https://github.com/superuser-d0/zapret-tr/blob/main/CHANGELOG.md)", not, StringComparison.Ordinal);
            Assert.Contains("](#ust)", not, StringComparison.Ordinal);
            Assert.Contains("](https://example.com/x)", not, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dizin, recursive: true); } catch (IOException) { }
        }
    }
}
