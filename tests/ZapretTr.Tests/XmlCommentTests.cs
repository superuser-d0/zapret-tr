using System.IO;
using System.Text.RegularExpressions;
using IoPath = System.IO.Path;

namespace ZapretTr.Tests;

/// <summary>
/// Depodaki XML/XAML dosyalarının geçerliliği.
/// </summary>
/// <remarks>
/// Bu testin var olma sebebi aynı hatayı iki kez yapmış olmam. XML yorumları çift
/// tire içeremez ve bunu unutmak iki farklı yerde soruna yol açtı:
///
///   app.manifest  -> uygulama "side-by-side configuration is incorrect" ile HİÇ
///                    AÇILMADI. Derleme bu hatayı yakalamıyor; manifest ancak
///                    işletim sistemi süreç oluştururken ayrıştırılıyor.
///   MainWindow.xaml -> derleme hatası (bunu en azından derleyici yakaladı).
///
/// Tehlikeli olan birincisi: derlemesi geçen, testleri geçen, ama hiç açılmayan
/// bir uygulama. Bu test o boşluğu kapatıyor.
/// </remarks>
public sealed class XmlCommentTests
{
    internal static string RepoRoot
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

        // Ayrıştırma hata fırlatırsa test düşer; mesaj dosyayı ve konumu söyler.
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
