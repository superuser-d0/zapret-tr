using System.IO;
using System.Text.RegularExpressions;
using IoPath = System.IO.Path;

namespace ZapretTr.Tests;

/// <summary>
/// Depodaki XML/XAML dosyalarinin gecerliligi.
/// </summary>
/// <remarks>
/// Bu testin varlik sebebi ayni hatayi iki kez yapmis olmam. XML yorumlari cift
/// tire iceremez ve bunu unutmak iki farkli yerde soruna yol acti:
///
///   app.manifest  -> uygulama "side-by-side configuration is incorrect" ile HIC
///                    ACILMADI. Derleme bu hatayi yakalamiyor; manifest ancak
///                    isletim sistemi surec olustururken ayristiriliyor.
///   MainWindow.xaml -> derleme hatasi (bunu en azindan derleyici yakaladi).
///
/// Tehlikeli olan birincisi: derlemesi gecen, testleri gecen, ama hic acilmayan
/// bir uygulama. Bu test o bosluku kapatiyor.
/// </remarks>
public sealed class XmlCommentTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(IoPath.Combine(dir.FullName, "ZapretTr.sln")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new DirectoryNotFoundException("Depo koku bulunamadi.");
        }
    }

    public static TheoryData<string> XmlFiles
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var pattern in new[] { "*.xaml", "*.manifest", "*.csproj" })
            {
                foreach (var file in Directory.EnumerateFiles(RepoRoot, pattern, SearchOption.AllDirectories))
                {
                    if (file.Contains($"{IoPath.DirectorySeparatorChar}obj{IoPath.DirectorySeparatorChar}",
                            StringComparison.Ordinal)
                        || file.Contains($"{IoPath.DirectorySeparatorChar}bin{IoPath.DirectorySeparatorChar}",
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    data.Add(IoPath.GetRelativePath(RepoRoot, file));
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(XmlFiles))]
    public void YorumlarindaCiftTire_Yok(string relativePath)
    {
        var content = File.ReadAllText(IoPath.Combine(RepoRoot, relativePath));

        foreach (Match match in Regex.Matches(content, "<!--.*?-->", RegexOptions.Singleline))
        {
            var body = match.Value[4..^3];
            Assert.False(
                body.Contains("--", StringComparison.Ordinal),
                $"{relativePath}: XML yorumu cift tire iceriyor. " +
                "Bu dosya gecersiz XML olur ve manifest dosyalarinda uygulama hic acilmaz.");
        }
    }

    [Theory]
    [MemberData(nameof(XmlFiles))]
    public void GecerliXml(string relativePath)
    {
        var path = IoPath.Combine(RepoRoot, relativePath);

        // Ayristirma hatasi firlatirsa test duser; mesaj dosyayi ve konumu soyler.
        string? failure = null;
        try
        {
            System.Xml.Linq.XDocument.Load(path);
        }
        catch (Exception ex)
        {
            failure = ex.Message;
        }

        Assert.True(failure is null, $"{relativePath}: gecersiz XML — {failure}");
    }
}
