using System.IO;
using System.Text.RegularExpressions;
using ZapretTr.Core.Engine;
using ZapretTr.Core.Profiles;
using IoPath = System.IO.Path;

namespace ZapretTr.Tests;

/// <summary>
/// Testte yeni dogrulanan bir kategori, uygulama yeniden acilmadan korumaya yansimali.
/// </summary>
/// <remarks>
/// OLCULDU (2026-09-16, 0.2.6, Turk Telekom): test "acilan: discord-guncelleme, discord,
/// roblox" dedi, hemen ardindan Baslat "yalnizca su adreslere (6): discord..." dedi --
/// roblox.com listede yoktu ve baslatma sonrasi dogrulama "Acilmayanlar: www.roblox.com"
/// diye uyardi. Dogrulama diske yaziliyordu, bellekteki profil ise acilistaki haliyle
/// kaliyordu.
/// </remarks>
public sealed class YeniDogrulamaTests
{
    private static string ProfilesRoot => IoPath.Combine(XmlCommentTests.RepoRoot, "profiles");

    private static string ViewModel => File.ReadAllText(
        IoPath.Combine(XmlCommentTests.RepoRoot, "src", "ZapretTr.App", "ViewModels", "MainViewModel.cs"));

    private static LearnedCandidate Ogrenilen(params string[] kategoriler) => new()
    {
        IspId = "turk-telekom",
        CandidateId = "tt-443-fake-ttl4",
        Section = "tcp443",
        Args = "--dpi-desync=fake --dpi-desync-ttl=4",
        VerifiedFor = kategoriler,
        LastVerified = "2026-09-16",
    };

    [Fact]
    public void Yeniden_Yuklenen_Profil_Yeni_Kategoriyi_Adres_Listesine_Tasiyor()
    {
        // Zincirin veri tarafi: ogrenilen roblox -> dogrulanmis kategori -> roblox.com.
        var winners = new Dictionary<StrategySection, string>
        {
            [StrategySection.Tcp443] = "--dpi-desync=fake --dpi-desync-ttl=4",
        };

        var eski = ProfileStore.Load(ProfilesRoot, [Ogrenilen("discord", "kullanici", "discord-guncelleme")])
            .FindById("turk-telekom");
        var yeni = ProfileStore.Load(ProfilesRoot, [Ogrenilen("discord-guncelleme", "discord", "roblox")])
            .FindById("turk-telekom");

        var hostlist = HostlistStore.Load(ProfilesRoot);

        Assert.DoesNotContain("roblox.com", hostlist.DomainsFor(RuntimeSelection.VerifiedCategories(eski, winners)));
        Assert.Contains("roblox.com", hostlist.DomainsFor(RuntimeSelection.VerifiedCategories(yeni, winners)));
    }

    [Fact]
    public void Calisma_Zamani_Kararlari_Bellekteki_Eski_Profili_Okumuyor()
    {
        var kaynak = ViewModel;

        Assert.Contains("RuntimeSelection.Build(CurrentProfile(),", kaynak, StringComparison.Ordinal);
        Assert.Contains("RuntimeSelection.VerifiedCategories(CurrentProfile(),", kaynak, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeSelection.Build(SelectedIsp?.Profile", kaynak, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeSelection.VerifiedCategories(SelectedIsp?.Profile", kaynak, StringComparison.Ordinal);
    }

    [Fact]
    public void Kayittan_Sonra_Profiller_Yeniden_Yukleniyor()
    {
        var m = Regex.Match(ViewModel, @"private void PersistLearned\(ProbeReport report\)(?<govde>.*?)\r?\n    }\r?\n",
            RegexOptions.Singleline);

        Assert.True(m.Success, "PersistLearned bulunamadi.");
        Assert.Contains("_profiles = ProfileStore.Load(learned: ConfigStore.LoadLearned());", m.Groups["govde"].Value,
            StringComparison.Ordinal);
    }
}
