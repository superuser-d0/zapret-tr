using System.Reflection;
using System.Text.RegularExpressions;
using ZapretTr.Core.Engine;
using ZapretTr.Core.Profiles;

namespace ZapretTr.Tests;

/// <summary>
/// QUIC bölümünün ve yer tutucu çözümlemesinin sessizce bozulmasını engelleyen testler.
/// </summary>
/// <remarks>
/// Bu bölümdeki hataların ortak özelliği GÖRÜNMEZ olmaları: winws yanlış bir
/// argümanla da başlar, hiçbir şey söylemez, yalnızca stratejiyi uygulamaz. Dışarıdan
/// bu, "strateji işe yaramadı" ile birebir aynı görüntü; 15 QUIC adayının tamamı
/// tam bu şekilde, hiç uygulanmadan "başarısız" raporlanmıştı.
/// </remarks>
public sealed class QuicLadderTests
{
    private static readonly GenericLadder Ladder = ProfileStore.Load().Ladder;
    private static readonly VendorPaths Vendor = VendorPaths.ForRoot(@"C:\Program Files\ZapretTR\zapret-winws");

    [Fact]
    public void MerdivendekiYerTutucularin_Hepsi_Cozuluyor()
    {
        // Yanlış yazılmış bir yer tutucu ({FAKE_QUIC_VK} yerine {FAKE_QUIC_VK_}) olduğu
        // gibi winws'e geçiyor; winws da adı kelimesi kelimesine böyle olan bir dosyayı
        // açmaya çalışıp o adayı sessizce işlevsiz bırakıyor.
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
        // bağlanmayı unutmak, tam da yukarıdaki sessiz hatayı üretir. Sabitler
        // yansımayla geziliyor ki yeni eklenen biri testi kendiliğinden kapsasın.
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
        // winws'in kendi uyarısı: "you are using --dpi-desync-any-protocol without
        // --dpi-desync-cutoff". Cutoff'suz any-protocol bağlantının TÜM paketlerine
        // müdahale eder. Ölçülmüş zarar var: sorunsuz çalışan bir QUIC bağlantısı,
        // üzerine denenmemiş bir QUIC stratejisi uygulanınca bozulmuştu.
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
        // İlk hâlde tek bir sahte yük (google) vardı ve QUIC arama uzayı bu yüzden
        // yapay olarak dardı: hangi yükün işe yaradığı DPI kutusunun paketin neyini
        // doğruladığına bağlı.
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
        // winws dilbilgisi --dpi-desync=[<mode0>,]<mode>[,<mode2>]. Önceki merdiven
        // fake / udplen / ipfrag2 ailelerini birbirini dışlıyor sanıp her zaman tek
        // başına denemişti; birleşimleri hiç sınanmamıştı.
        var combined = Ladder.Expand(StrategySection.Quic)
            .Where(c => c.Args.Contains("--dpi-desync=fake,", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(combined);
    }
}
