using System.IO;
using ZapretTr.Core.Engine;
using ZapretTr.Core.Profiles;
using ZapretTr.Prober;
using IoPath = System.IO.Path;

namespace ZapretTr.Tests;

/// <summary>
/// Stratejinin YALNIZCA engelli adreslere uygulanmasi.
/// </summary>
/// <remarks>
/// Bu testlerin varlik sebebi olculmus bir hata: issue #1 (Vodafone Net,
/// 2026-09-15). Kazanan 443 stratejisi (<c>--dpi-desync-fooling=badseq</c>) butun
/// 443 trafigine uygulaniyordu ve koruma acikken GitHub calismiyordu. badseq'te
/// sahte paket gercek sunucuya ulasir; engellenmemis bir siteye dokunmanin
/// kazanci yok, riski var.
///
/// Ayrica bu, kendi guncelleme yolumuzu da vuruyordu: UpdateChecker
/// api.github.com'a, UpdateDownloader github.com'a gidiyor. Koruma acikken GitHub
/// bozuksa duzeltmeyi kullaniciya ulastiran yol da kapaniyordu.
/// </remarks>
public sealed class HostlistTests
{
    private static readonly VendorPaths Vendor =
        VendorPaths.ForRoot(@"C:\Program Files\ZapretTR\zapret-winws");

    private static readonly WinwsCommandBuilder Builder = new(Vendor);

    private static string ProfilesRoot => IoPath.Combine(XmlCommentTests.RepoRoot, "profiles");

    // --- Komut uretimi ----------------------------------------------------------

    [Fact]
    public void Alanlar_Verilince_HerBolume_AyriAyri_Ekleniyor()
    {
        // winws hostlist'i profil (yani --new ile ayrilan bolum) basina denetliyor.
        // Tek bir yere yazmak yalnizca ilk bolumu daraltir, kalani genel kalirdi.
        var args = Builder.BuildRuntimeCommand(
            new Dictionary<StrategySection, string>
            {
                [StrategySection.Tcp443] = "--dpi-desync=fake",
                [StrategySection.Tcp80] = "--dpi-desync=multisplit",
                [StrategySection.Quic] = "--dpi-desync=fake",
            },
            ["discord.com", "discord.gg"]);

        var hostlist = args.Where(a => a.StartsWith("--hostlist-domains=", StringComparison.Ordinal)).ToList();

        Assert.Equal(3, hostlist.Count);
        Assert.All(hostlist, h => Assert.Equal("--hostlist-domains=discord.com,discord.gg", h));
    }

    [Fact]
    public void DiscordSesi_Hostlist_ALMAZ()
    {
        // STUN/UDP medya trafiginde alan adi YOK. Hostlist eklenirse winws o profil
        // icin ad esleismesi arar, bulamaz ve bolum hic devreye girmez: Discord sesi
        // SESSIZCE korumasiz kalirdi.
        var args = Builder.BuildRuntimeCommand(
            new Dictionary<StrategySection, string>
            {
                [StrategySection.DiscordVoice] = "--dpi-desync=fake",
            },
            ["discord.com"]);

        Assert.DoesNotContain(args, a => a.StartsWith("--hostlist-domains=", StringComparison.Ordinal));
    }

    [Fact]
    public void DiscordSesi_Diger_Bolumlerle_Birlikte_Yine_Hostlist_ALMAZ()
    {
        var args = Builder.BuildRuntimeCommand(
            new Dictionary<StrategySection, string>
            {
                [StrategySection.Tcp443] = "--dpi-desync=fake",
                [StrategySection.DiscordVoice] = "--dpi-desync=fake",
            },
            ["discord.com"]);

        // Iki bolum var ama hostlist yalnizca birinde.
        Assert.Single(args.Where(a => a.StartsWith("--hostlist-domains=", StringComparison.Ordinal)));

        // Ve o, ses bolumunun filtresinden ONCE gelmeli (tcp443 bolumunun icinde).
        var liste = args.ToList();
        var hostlistIndex = liste.IndexOf("--hostlist-domains=discord.com");
        var voiceIndex = liste.IndexOf(StrategySection.DiscordVoice.ToWinwsFilter());
        Assert.True(hostlistIndex < voiceIndex, "hostlist ses bolumune sizmis.");
    }

    [Fact]
    public void Alan_Verilmezse_Eski_Genel_Davranis_Suruyor()
    {
        // Bos liste "daraltma yapma" demek. Bayragi bos degerle eklemek korumayi
        // SESSIZCE tamamen kapatirdi; bozuk bir veri dosyasinin bedeli bu olmamali.
        var args = Builder.BuildRuntimeCommand(new Dictionary<StrategySection, string>
        {
            [StrategySection.Tcp443] = "--dpi-desync=fake",
        });

        Assert.DoesNotContain(args, a => a.StartsWith("--hostlist-domains=", StringComparison.Ordinal));
    }

    // --- Veri dosyasi -----------------------------------------------------------

    [Fact]
    public void Dagitilan_Liste_Discordu_Kapsiyor_GitHubi_KAPSAMIYOR()
    {
        var store = HostlistStore.Load(ProfilesRoot);

        var domains = store.DomainsFor(["discord", "discord-guncelleme"]);

        Assert.Contains("discord.com", domains);
        Assert.Contains("discord.gg", domains);

        // Issue #1'in ta kendisi: engellenmemis siteler listeye GIRMEMELI.
        Assert.DoesNotContain("github.com", domains);
        Assert.DoesNotContain("youtube.com", domains);
    }

    [Fact]
    public void Roblox_Kategorisi_Yalnizca_Engelli_Olculen_Adi_Kapsiyor()
    {
        // Olculdu (2026-09-16, Turk Telekom): roblox.com TLS'te kesiliyor, rbxcdn.com
        // aciliyor. Daraltmadan sonra Roblox hic kapsanmiyordu (iki kullanici bildirdi).
        var domains = HostlistStore.Load(ProfilesRoot).DomainsFor(["roblox"]);

        Assert.Equal(["roblox.com"], domains);
    }

    [Fact]
    public void Her_Test_Kategorisinin_Hostlist_Karsiligi_Var()
    {
        // Test bir kategoriyi engelli bulup strateji dogrulayip da hostlist'te o
        // kategori yoksa koruma o siteyi SESSIZCE kapsamaz -- Roblox'ta olan buydu
        // (tersi yonden: hedef hic yoktu). kontrol bilerek bos; discord-voice UDP
        // bolumu hostlist almiyor.
        var store = HostlistStore.Load(ProfilesRoot);
        var kategoriler = ProbeTargetStore.Load(ProfilesRoot)
            .Select(t => t.Category)
            .Where(c => c is not ("kontrol" or "discord-voice"))
            .Distinct(StringComparer.Ordinal);

        foreach (var kategori in kategoriler)
        {
            Assert.True(store.DomainsFor([kategori]).Count > 0, $"'{kategori}' kategorisinin hostlist karsiligi yok.");
        }

        Assert.Contains(ProbeTargetStore.Load(ProfilesRoot), t => t.Host == "www.roblox.com" && t.Category == "roblox");
    }

    [Fact]
    public void Kontrol_Kategorisi_Bos_Kalmali()
    {
        // example.com / cloudflare-quic.com engellenmemesi BEKLENEN olcum hedefleri.
        // Onlara strateji uygulamak olcumun kendisini bozar.
        var domains = HostlistStore.Load(ProfilesRoot).DomainsFor(["kontrol"]);

        Assert.Empty(domains);
    }

    [Fact]
    public void Kategori_Verilmezse_Bilinen_Butun_Hedefler_Donuyor()
    {
        // Kullanici elle dogrulanmamis bir strateji sectiyse hangi kategorinin
        // engelli oldugu BILINMIYOR. O zaman daraltmayi tamamen birakmak yerine
        // aracin hedefledigi adreslere iniyoruz -- GitHub yine disarida kaliyor.
        var domains = HostlistStore.Load(ProfilesRoot).DomainsFor();

        Assert.Contains("discord.com", domains);
        Assert.Contains("youtube.com", domains);
        Assert.DoesNotContain("github.com", domains);
    }

    [Fact]
    public void Kullanicinin_Kendi_Hedefi_Listeye_Giriyor()
    {
        // Bu olmazsa "Acilmayan site" test edilir, dogrulanir ama calisma zamaninda
        // korunmaz: kullanici icin ozellik sessizce calismamis olur.
        var domains = HostlistStore.Load(ProfilesRoot)
            .DomainsFor(["discord"], "https://ornek-site.com/bir/yol");

        Assert.Contains("ornek-site.com", domains);
    }

    [Theory]
    [InlineData("discord.com", "discord.com")]
    [InlineData("  Discord.COM  ", "discord.com")]
    [InlineData("https://x.com/abc", "x.com")]
    [InlineData("x.com/abc", "x.com")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Kullanici_Girdisinden_Alan_Adi_Cikariliyor(string? girdi, string? beklenen)
        => Assert.Equal(beklenen, HostlistStore.TryParseDomain(girdi));

    [Fact]
    public void Virgullu_Girdi_Reddediliyor()
    {
        // Deger virgulle ayrilmis listeye giriyor; virgul iceren tek bir girdi
        // listeye iki ad eklemek anlamina gelirdi.
        Assert.Null(HostlistStore.TryParseDomain("a.com,b.com"));
    }

    [Fact]
    public void Bozuk_Ya_Da_Eksik_Dosya_Korumayi_Kapatmiyor()
    {
        // Dosya yoksa: bos liste -> bayrak eklenmez -> strateji eskisi gibi genel
        // uygulanir. Yani en kotu durumda 0.2.1 davranisi, korumasizlik degil.
        var domains = HostlistStore.Load(IoPath.Combine(IoPath.GetTempPath(), "olmayan-dizin-" + Guid.NewGuid()))
            .DomainsFor(["discord"]);

        Assert.Empty(domains);
    }

    // --- Kategori kaynagi -------------------------------------------------------

    [Fact]
    public void Kategoriler_Dogrulanmis_Adaydan_Okunuyor()
    {
        var profile = ProfileStore
            .Load(ProfilesRoot)
            .Profiles
            .First(p => p.Candidates.Any(c => c.VerifiedFor.Count > 0));

        var verified = profile.Candidates.First(c => c.VerifiedFor.Count > 0);

        var kategoriler = RuntimeSelection.VerifiedCategories(
            profile,
            new Dictionary<StrategySection, string> { [verified.Section] = verified.Args });

        Assert.NotEmpty(kategoriler);
        Assert.All(verified.VerifiedFor, k => Assert.Contains(k, kategoriler));
    }

    [Fact]
    public void Profil_Yoksa_Kategori_Uydurulmuyor()
        => Assert.Empty(RuntimeSelection.VerifiedCategories(
            null,
            new Dictionary<StrategySection, string> { [StrategySection.Tcp443] = "--dpi-desync=fake" }));

    [Fact]
    public void Issue1_Senaryosu_Ucbastan_Uretiliyor()
    {
        // Issue #1'in ta kendisi, depodaki GERCEK profil verisiyle: Vodafone Net,
        // dogrulanmis HTTPS adayi. Uretilen komut Discord'a dokunmali, GitHub'a
        // DOKUNMAMALI. Bu test, parcalari degil zinciri sabitliyor:
        // profil -> verifiedFor -> hostlist -> winws komutu.
        var profile = ProfileStore.Load(ProfilesRoot).FindById("vodafone-net");
        Assert.NotNull(profile);

        var https = profile.CandidatesFor(StrategySection.Tcp443)
            .First(c => c.Source == CandidateSource.Verified);

        var winners = RuntimeSelection.Build(profile, https.Args);
        var kategoriler = RuntimeSelection.VerifiedCategories(profile, winners);
        var alanlar = HostlistStore.Load(ProfilesRoot).DomainsFor(kategoriler);
        var args = Builder.BuildRuntimeCommand(winners, alanlar);

        var komut = WinwsCommandBuilder.ToDisplayString(args);

        Assert.Contains("--hostlist-domains=", komut, StringComparison.Ordinal);
        Assert.Contains("discord.com", komut, StringComparison.Ordinal);
        Assert.DoesNotContain("github", komut, StringComparison.OrdinalIgnoreCase);

        // QUIC bolumu komuta GIRMEMELI: o hatta calisan aday yok (25/25 zaman asimi).
        Assert.DoesNotContain("--filter-l7=quic", args);

        // Hostlist, strateji argumanlarindan ONCE ve her bolumde.
        var bolumSayisi = args.Count(a => a.StartsWith("--filter-", StringComparison.Ordinal));
        var hostlistSayisi = args.Count(a => a.StartsWith("--hostlist-domains=", StringComparison.Ordinal));
        Assert.Equal(bolumSayisi, hostlistSayisi);
    }

    [Fact]
    public void Veri_Dosyasi_Dagitima_Giriyor()
    {
        // profiles/ dizininin tamami kurulum paketine kopyalaniyor; dosya orada
        // olmazsa kurulu uygulamada daraltma sessizce devre disi kalir.
        Assert.True(File.Exists(IoPath.Combine(ProfilesRoot, "hostlist-domains.json")));
    }
}
