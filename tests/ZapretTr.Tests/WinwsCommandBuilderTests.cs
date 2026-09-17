using ZapretTr.Core.Engine;
using ZapretTr.Core.Profiles;

namespace ZapretTr.Tests;

public sealed class WinwsCommandBuilderTests
{
    // İçinde boşluk olan kasıtlı bir yol: kullanıcıların yarısı
    // "C:\Users\Ali Veli\..." altında çalışacak ve argüman bölme hataları
    // tam olarak orada ortaya çıkar.
    private static readonly VendorPaths Vendor = VendorPaths.ForRoot(@"C:\Program Files\ZapretTR\zapret-winws");
    private static readonly WinwsCommandBuilder Builder = new(Vendor);

    [Fact]
    public void TekBolum_NewAyraci_Icermez()
    {
        var args = Builder.BuildRuntimeCommand(new Dictionary<StrategySection, string>
        {
            [StrategySection.Tcp443] = "--dpi-desync=fake --dpi-desync-fooling=md5sig",
        });

        Assert.DoesNotContain("--new", args);
        Assert.Contains("--wf-tcp=443", args);
        Assert.Contains("--filter-tcp=443", args);
    }

    [Fact]
    public void CokBolum_AralaraNewKoyar_SonaKoymaz()
    {
        var args = Builder.BuildRuntimeCommand(new Dictionary<StrategySection, string>
        {
            [StrategySection.Tcp443] = "--dpi-desync=fake",
            [StrategySection.Tcp80] = "--dpi-desync=multisplit",
            [StrategySection.Quic] = "--dpi-desync=fake",
        });

        // Üç bölüm, aralarında iki ayraç.
        Assert.Equal(2, args.Count(a => a == "--new"));
        Assert.NotEqual("--new", args[^1]);
    }

    [Fact]
    public void Bolumler_KanonikSirada_Yazilir()
    {
        // Sözlükteki sıra ne olursa olsun çıktı deterministik olmalı:
        // günlükler ve testler ancak böyle karşılaştırılabilir.
        var args = Builder.BuildRuntimeCommand(new Dictionary<StrategySection, string>
        {
            [StrategySection.DiscordVoice] = "--dpi-desync=fake",
            [StrategySection.Tcp80] = "--dpi-desync=multisplit",
            [StrategySection.Quic] = "--dpi-desync=fake",
            [StrategySection.Tcp443] = "--dpi-desync=fake",
        });

        var order = args
            .Where(a => a.StartsWith("--filter-", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(
            ["--filter-tcp=80", "--filter-tcp=443", "--filter-l7=quic", "--filter-l7=discord,stun"],
            order);
    }

    [Fact]
    public void GlobalFiltre_YalnizcaGerekenBolumleriKapsar()
    {
        // Fazladan trafik yakalamak yalnızca CPU harcar ve bağlantıyı yavaşlatır;
        // upstream belgesi de bunu açıkça söylüyor.
        var args = Builder.BuildRuntimeCommand(new Dictionary<StrategySection, string>
        {
            [StrategySection.Tcp443] = "--dpi-desync=fake",
        });

        Assert.Contains("--wf-tcp=443", args);
        Assert.DoesNotContain("--wf-udp=443", args);
        Assert.DoesNotContain(args, a => a.StartsWith("--wf-raw-part", StringComparison.Ordinal));
    }

    [Fact]
    public void HemSeksen_HemDortYuzKirkUc_TekWfTcpArgumaninda_Birlesir()
    {
        var args = Builder.BuildRuntimeCommand(new Dictionary<StrategySection, string>
        {
            [StrategySection.Tcp80] = "--dpi-desync=multisplit",
            [StrategySection.Tcp443] = "--dpi-desync=fake",
        });

        Assert.Contains("--wf-tcp=80,443", args);
        Assert.Single(args.Where(a => a.StartsWith("--wf-tcp", StringComparison.Ordinal)));
    }

    [Fact]
    public void DiscordSes_HamFiltreParcalarini_Ekler()
    {
        // Discord ses trafiği sabit bir porta oturmaz; bu parçalar olmadan
        // trafik çekirdekten hiç gelmez ve strateji sessizce hiçbir şey yapmaz.
        var args = Builder.BuildRuntimeCommand(new Dictionary<StrategySection, string>
        {
            [StrategySection.DiscordVoice] = "--dpi-desync=fake",
        });

        Assert.Contains(args, a => a.Contains("windivert_part.discord_media.txt", StringComparison.Ordinal));
        Assert.Contains(args, a => a.Contains("windivert_part.stun.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void BosKazananSozlugu_Reddedilir()
    {
        Assert.Throws<ArgumentException>(() =>
            Builder.BuildRuntimeCommand(new Dictionary<StrategySection, string>()));
    }

    // --- Yer tutucu çözümü ------------------------------------------------------

    [Fact]
    public void YerTutucu_GercekYolaCevrilir()
    {
        var args = Builder.BuildRuntimeCommand(new Dictionary<StrategySection, string>
        {
            [StrategySection.Quic] = "--dpi-desync=fake --dpi-desync-fake-quic={FAKE_QUIC_GOOGLE}",
        });

        Assert.DoesNotContain(args, a => a.Contains("{FAKE_QUIC_GOOGLE}", StringComparison.Ordinal));
        Assert.Contains(args, a => a.EndsWith("quic_initial_www_google_com.bin", StringComparison.Ordinal));
    }

    [Fact]
    public void BosluklarIcerenYol_TekArgumanOlarakKalir()
    {
        // Bu testin bütün meselesi: önce boşluklardan böl, SONRA yer tutucuyu çöz.
        // Ters sırada yapılsaydı "C:\Program Files\..." iki argümana bölünürdü ve
        // winws dosyayı bulamazdı; üretimde tespiti zor bir hata.
        var args = Builder.BuildRuntimeCommand(new Dictionary<StrategySection, string>
        {
            [StrategySection.Quic] = "--dpi-desync=fake --dpi-desync-fake-quic={FAKE_QUIC_GOOGLE}",
        });

        var quicArg = Assert.Single(args.Where(a => a.StartsWith("--dpi-desync-fake-quic=", StringComparison.Ordinal)));
        Assert.Contains("Program Files", quicArg, StringComparison.Ordinal);
        Assert.EndsWith("quic_initial_www_google_com.bin", quicArg, StringComparison.Ordinal);
    }

    // --- Test (probe) komutu ----------------------------------------------------

    [Fact]
    public void ProbeKomutu_HedefIpsetIni_Icerir()
    {
        // Paralel testin yalıtım mekanizması: strateji yalnızca kendi hedef IP'sine
        // uygulanır, diğer paketler dokunulmadan geçer.
        var args = Builder.BuildProbeCommand(StrategySection.Tcp443, "--dpi-desync=fake", "142.250.1.1");

        Assert.Contains("--ipset-ip=142.250.1.1", args);
        Assert.Contains("--filter-tcp=443", args);
        Assert.DoesNotContain("--new", args);
    }

    [Fact]
    public void ProbeKomutu_YalnizcaKendiBolumunun_GlobalFiltresiniAcar()
    {
        var args = Builder.BuildProbeCommand(StrategySection.Quic, "--dpi-desync=fake", "142.250.1.1");

        Assert.Contains("--wf-udp=443", args);
        Assert.DoesNotContain(args, a => a.StartsWith("--wf-tcp", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ProbeKomutu_BosStratejiyi_Reddeder(string strategyArgs)
    {
        Assert.Throws<ArgumentException>(() =>
            Builder.BuildProbeCommand(StrategySection.Tcp443, strategyArgs, "1.2.3.4"));
    }

    [Fact]
    public void GosterimDizgisi_BosluklarIcerenArgumanlari_Tirnaklar()
    {
        var args = Builder.BuildProbeCommand(
            StrategySection.Quic,
            "--dpi-desync=fake --dpi-desync-fake-quic={FAKE_QUIC_GOOGLE}",
            "1.2.3.4");

        var display = WinwsCommandBuilder.ToDisplayString(args);

        Assert.Contains("\"", display, StringComparison.Ordinal);
        Assert.Contains("--ipset-ip=1.2.3.4", display, StringComparison.Ordinal);
    }

    [Fact]
    public void ProbeKomutu_CokHedefi_TekIpsetTe_Birlestirir()
    {
        // Paralel sınamanın temeli. Hedef başına AYRI winws örneği çalışmıyor:
        // --ipset-ip global WinDivert filtresine girmediği için iki örnek birebir
        // aynı filtreyi kuruyor ve winws ikincisini "A copy of winws is already
        // running with the same filter" diyerek öldürür. Bu sessiz bir hataydı;
        // aday, hedeflerin yalnızca birinde ölçülüyordu.
        var args = Builder.BuildProbeCommand(
            StrategySection.Quic, "--dpi-desync=fake", ["1.2.3.4", "5.6.7.8"]);

        Assert.Contains("--ipset-ip=1.2.3.4,5.6.7.8", args);

        // Tekrarlı bayrak DEĞİL: winws --ipset-ip=<ip_list> bekliyor.
        Assert.Equal(1, args.Count(a => a.StartsWith("--ipset-ip=", StringComparison.Ordinal)));
    }

    [Fact]
    public void ProbeKomutu_HedefsizIpseti_Reddeder()
    {
        // Boş ipset bütün trafiğe dokunurdu; "sorunu olmayan bölüme dokunma"
        // kuralının en sert ihlali.
        Assert.Throws<ArgumentException>(() =>
            Builder.BuildProbeCommand(StrategySection.Quic, "--dpi-desync=fake", Array.Empty<string>()));

        Assert.Throws<ArgumentException>(() =>
            Builder.BuildProbeCommand(StrategySection.Quic, "--dpi-desync=fake", ["1.2.3.4", "  "]));
    }
}
