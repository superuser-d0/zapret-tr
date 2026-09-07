using System.Reflection;
using System.Text.RegularExpressions;
using ZapretTr.Core.Engine;
using ZapretTr.Core.Profiles;

namespace ZapretTr.Tests;

/// <summary>
/// QUIC bolumunun ve yer tutucu cozumlemesinin sessizce bozulmasini engelleyen testler.
/// </summary>
/// <remarks>
/// Bu bolumdeki hatalarin ortak ozelligi GORUNMEZ olmalari: winws yanlis bir
/// argumanla da baslar, hicbir sey soylemez, yalnizca stratejiyi uygulamaz. Disaridan
/// bu "strateji ise yaramadi" ile birebir ayni goruntu -- ve 15 QUIC adayinin tamami
/// tam bu sekilde, hic uygulanmadan "basarisiz" raporlanmisti.
/// </remarks>
public sealed class QuicLadderTests
{
    private static readonly GenericLadder Ladder = ProfileStore.Load().Ladder;
    private static readonly VendorPaths Vendor = VendorPaths.ForRoot(@"C:\Program Files\ZapretTR\zapret-winws");

    [Fact]
    public void MerdivendekiYerTutucularin_Hepsi_Cozuluyor()
    {
        // Yanlis yazilmis bir yer tutucu ({FAKE_QUIC_VK} yerine {FAKE_QUIC_VK_}) oldugu
        // gibi winws'e geciyor; winws da adi kelimesi kelimesine boyle olan bir dosya
        // acmaya calisip o adayi sessizce isesiz birakiyor.
        foreach (var section in Enum.GetValues<StrategySection>())
        {
            foreach (var candidate in Ladder.Expand(section))
            {
                foreach (var part in candidate.Args.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var resolved = Vendor.ResolvePlaceholders(part);
                    Assert.False(
                        Regex.IsMatch(resolved, @"\{[A-Z0-9_]+\}"),
                        $"{section.ToJsonName()} / {candidate.Family}: cozulmemis yer tutucu -> {resolved}");
                }
            }
        }
    }

    [Fact]
    public void TanimliHerYerTutucu_ResolvePlaceholders_Tarafindan_Isleniyor()
    {
        // VendorPaths'e yeni bir yer tutucu sabiti eklenip ResolvePlaceholders'a
        // baglanmayi unutmak, tam da yukaridaki sessiz hatayi uretir. Sabitler
        // yansimayla geziliyor ki yeni eklenen biri testi kendiliginden kapsasin.
        var placeholders = typeof(VendorPaths)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(v => v.StartsWith('{') && v.EndsWith('}'))
            .ToList();

        Assert.NotEmpty(placeholders);

        foreach (var placeholder in placeholders)
        {
            var resolved = Vendor.ResolvePlaceholders(placeholder);
            Assert.NotEqual(placeholder, resolved);
        }
    }

    [Fact]
    public void AnyProtocol_HerZaman_Cutoff_Ile_Birlikte()
    {
        // winws'in kendi uyarisi: "you are using --dpi-desync-any-protocol without
        // --dpi-desync-cutoff". Cutoff'suz any-protocol baglantinin TUM paketlerine
        // mudahale eder. Olculmus zarar var: sorunsuz calisan bir QUIC baglantisi,
        // uzerine denenmemis bir QUIC stratejisi uygulaninca bozulmustu.
        foreach (var section in Enum.GetValues<StrategySection>())
        {
            var offenders = Ladder.Expand(section)
                .Where(c => c.Args.Contains("--dpi-desync-any-protocol=1", StringComparison.Ordinal))
                .Where(c => !c.Args.Contains("--dpi-desync-cutoff=", StringComparison.Ordinal))
                .Select(c => c.Args)
                .ToList();

            Assert.True(offenders.Count == 0,
                $"{section.ToJsonName()}: cutoff'suz any-protocol adayi: {string.Join(" | ", offenders)}");
        }
    }

    [Fact]
    public void QuicMerdiveni_TekBirYuke_Bagli_Degil()
    {
        // Ilk halde tek bir sahte yuk (google) vardi ve QUIC arama uzayi bu yuzden
        // yapay olarak dardi: hangi yukun ise yaradigi DPI kutusunun paketin neyini
        // dogruladigina bagli.
        var payloads = Ladder.Expand(StrategySection.Quic)
            .SelectMany(c => c.Args.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.StartsWith("--dpi-desync-fake-quic=", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(payloads.Count >= 3,
            $"QUIC sahte yuk cesidi yetersiz: {payloads.Count}");
    }

    [Fact]
    public void QuicMerdiveni_BirlesikModlari_Iceriyor()
    {
        // winws dilbilgisi --dpi-desync=[<mode0>,]<mode>[,<mode2>]. Onceki merdiven
        // fake / udplen / ipfrag2 ailelerini birbirini disliyor sanip her zaman tek
        // basina denemisti; birlesimleri hic sinanmamisti.
        var combined = Ladder.Expand(StrategySection.Quic)
            .Where(c => c.Args.Contains("--dpi-desync=fake,", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(combined);
    }
}
