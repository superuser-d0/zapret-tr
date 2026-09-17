using System.IO;
using ZapretTr.Core.Engine;
using ZapretTr.Core.Profiles;
using ZapretTr.Prober;
using IoPath = System.IO.Path;

namespace ZapretTr.Tests;

/// <summary>
/// Stratejinin YALNIZCA engelli adreslere uygulanması.
/// </summary>
/// <remarks>
/// Bu testlerin var olma sebebi ölçülmüş bir hata: issue #1 (Vodafone Net,
/// 2026-09-15). Kazanan 443 stratejisi (<c>--dpi-desync-fooling=badseq</c>) bütün
/// 443 trafiğine uygulanıyordu ve koruma açıkken GitHub çalışmıyordu. badseq'te
/// sahte paket gerçek sunucuya ulaşır; engellenmemiş bir siteye dokunmanın
/// kazancı yok, riski var.
///
/// Ayrıca bu, kendi güncelleme yolumuzu da vuruyordu: UpdateChecker
/// api.github.com'a, UpdateDownloader github.com'a gidiyor. Koruma açıkken GitHub
/// bozuksa düzeltmeyi kullanıcıya ulaştıran yol da kapanıyordu.
/// </remarks>
public sealed class HostlistTests
{
    private static readonly VendorPaths Vendor =
        VendorPaths.ForRoot(@"C:\Program Files\ZapretTR\zapret-winws");

    private static readonly WinwsCommandBuilder Builder = new(Vendor);

    private static string ProfilesRoot => IoPath.Combine(XmlCommentTests.RepoRoot, "profiles");

    // --- Komut üretimi ----------------------------------------------------------

    [Fact]
    public void Alanlar_Verilince_HerBolume_AyriAyri_Ekleniyor()
    {
        // winws hostlist'i profil (yani --new ile ayrılan bölüm) başına denetliyor.
        // Tek bir yere yazmak yalnızca ilk bölümü daraltır, kalanı genel kalırdı.
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
        // STUN/UDP medya trafiğinde alan adı YOK. Hostlist eklenirse winws o profil
        // için ad eşleşmesi arar, bulamaz ve bölüm hiç devreye girmez: Discord sesi
        // SESSİZCE korumasız kalırdı.
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

        // İki bölüm var ama hostlist yalnızca birinde.
        Assert.Single(args.Where(a => a.StartsWith("--hostlist-domains=", StringComparison.Ordinal)));

        // Ve o, ses bölümünün filtresinden ÖNCE gelmeli (tcp443 bölümünün içinde).
        var liste = args.ToList();
        var hostlistIndex = liste.IndexOf("--hostlist-domains=discord.com");
        var voiceIndex = liste.IndexOf(StrategySection.DiscordVoice.ToWinwsFilter());
        Assert.True(hostlistIndex < voiceIndex, "hostlist ses bolumune sizmis.");
    }

    [Fact]
    public void Alan_Verilmezse_Eski_Genel_Davranis_Suruyor()
    {
        // Boş liste "daraltma yapma" demek. Bayrağı boş değerle eklemek korumayı
        // SESSİZCE tamamen kapatırdı; bozuk bir veri dosyasının bedeli bu olmamalı.
        var args = Builder.BuildRuntimeCommand(new Dictionary<StrategySection, string>
        {
            [StrategySection.Tcp443] = "--dpi-desync=fake",
        });

        Assert.DoesNotContain(args, a => a.StartsWith("--hostlist-domains=", StringComparison.Ordinal));
    }

    // --- Veri dosyası -----------------------------------------------------------

    [Fact]
    public void Dagitilan_Liste_Discordu_Kapsiyor_GitHubi_KAPSAMIYOR()
    {
        var store = HostlistStore.Load(ProfilesRoot);

        var domains = store.DomainsFor(["discord", "discord-guncelleme"]);

        Assert.Contains("discord.com", domains);
        Assert.Contains("discord.gg", domains);

        // Issue #1'in ta kendisi: engellenmemiş siteler listeye GİRMEMELİ.
        Assert.DoesNotContain("github.com", domains);
        Assert.DoesNotContain("youtube.com", domains);
    }

    [Fact]
    public void Roblox_Kategorisi_Yalnizca_Engelli_Olculen_Adi_Kapsiyor()
    {
        // Ölçüldü (2026-09-16, Türk Telekom): roblox.com TLS'te kesiliyor, rbxcdn.com
        // açılıyor. Daraltmadan sonra Roblox hiç kapsanmıyordu (iki kullanıcı bildirdi).
        var domains = HostlistStore.Load(ProfilesRoot).DomainsFor(["roblox"]);

        Assert.Equal(["roblox.com"], domains);
    }

    [Fact]
    public void Her_Test_Kategorisinin_Hostlist_Karsiligi_Var()
    {
        // Test bir kategoriyi engelli bulup strateji doğrulayıp da hostlist'te o
        // kategori yoksa koruma o siteyi SESSİZCE kapsamaz; Roblox'ta olan buydu
        // (tersi yönden: hedef hiç yoktu). kontrol bilerek boş; discord-voice UDP
        // bölümü hostlist almıyor.
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
        // example.com / cloudflare-quic.com engellenmemesi BEKLENEN ölçüm hedefleri.
        // Onlara strateji uygulamak ölçümün kendisini bozar.
        var domains = HostlistStore.Load(ProfilesRoot).DomainsFor(["kontrol"]);

        Assert.Empty(domains);
    }

    [Fact]
    public void Kategori_Verilmezse_Bilinen_Butun_Hedefler_Donuyor()
    {
        // Kullanıcı elle doğrulanmamış bir strateji seçtiyse hangi kategorinin
        // engelli olduğu BİLİNMİYOR. O zaman daraltmayı tamamen bırakmak yerine
        // aracın hedeflediği adreslere iniyoruz; GitHub yine dışarıda kalıyor.
        var domains = HostlistStore.Load(ProfilesRoot).DomainsFor();

        Assert.Contains("discord.com", domains);
        Assert.Contains("youtube.com", domains);
        Assert.DoesNotContain("github.com", domains);
    }

    [Fact]
    public void Kullanicinin_Kendi_Hedefi_Listeye_Giriyor()
    {
        // Bu olmazsa "Açılmayan site" test edilir, doğrulanır ama çalışma zamanında
        // korunmaz: kullanıcı için özellik sessizce çalışmamış olur.
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
        // Değer virgülle ayrılmış listeye giriyor; virgül içeren tek bir girdi
        // listeye iki ad eklemek anlamına gelirdi.
        Assert.Null(HostlistStore.TryParseDomain("a.com,b.com"));
    }

    [Fact]
    public void Bozuk_Ya_Da_Eksik_Dosya_Korumayi_Kapatmiyor()
    {
        // Dosya yoksa: boş liste -> bayrak eklenmez -> strateji eskisi gibi genel
        // uygulanır. Yani en kötü durumda 0.2.1 davranışı, korumasızlık değil.
        var domains = HostlistStore.Load(IoPath.Combine(IoPath.GetTempPath(), "olmayan-dizin-" + Guid.NewGuid()))
            .DomainsFor(["discord"]);

        Assert.Empty(domains);
    }

    // --- Kategori kaynağı -------------------------------------------------------

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
        // Issue #1'in ta kendisi, depodaki GERÇEK profil verisiyle: Vodafone Net,
        // doğrulanmış HTTPS adayı. Üretilen komut Discord'a dokunmalı, GitHub'a
        // DOKUNMAMALI. Bu test, parçaları değil zinciri sabitliyor:
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

        // QUIC bölümü komuta GİRMEMELİ: o hatta çalışan aday yok (25/25 zaman aşımı).
        Assert.DoesNotContain("--filter-l7=quic", args);

        // Hostlist, strateji argümanlarından ÖNCE ve her bölümde.
        var bolumSayisi = args.Count(a => a.StartsWith("--filter-", StringComparison.Ordinal));
        var hostlistSayisi = args.Count(a => a.StartsWith("--hostlist-domains=", StringComparison.Ordinal));
        Assert.Equal(bolumSayisi, hostlistSayisi);
    }

    [Fact]
    public void Veri_Dosyasi_Dagitima_Giriyor()
    {
        // profiles/ dizininin tamamı kurulum paketine kopyalanıyor; dosya orada
        // olmazsa kurulu uygulamada daraltma sessizce devre dışı kalır.
        Assert.True(File.Exists(IoPath.Combine(ProfilesRoot, "hostlist-domains.json")));
    }
}
