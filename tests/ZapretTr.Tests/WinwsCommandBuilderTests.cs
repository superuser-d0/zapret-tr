using ZapretTr.Core.Engine;
using ZapretTr.Core.Profiles;

namespace ZapretTr.Tests;

public sealed class WinwsCommandBuilderTests
{
    // Icinde bosluk olan kasitli bir yol: kullanicilarin yarisi
    // "C:\Users\Ali Veli\..." altinda calisacak ve arguman bolme hatalari
    // tam olarak orada ortaya cikar.
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

        // Uc bolum, aralarinda iki ayrac.
        Assert.Equal(2, args.Count(a => a == "--new"));
        Assert.NotEqual("--new", args[^1]);
    }

    [Fact]
    public void Bolumler_KanonikSirada_Yazilir()
    {
        // Sozlukteki sira ne olursa olsun cikti deterministik olmali:
        // gunlukler ve testler ancak boyle karsilastirilabilir.
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
        // Fazladan trafik yakalamak yalnizca CPU harcar ve baglantiyi yavaslatir;
        // upstream belgesi de bunu acikca soyluyor.
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
        // Discord ses trafigi sabit bir porta oturmaz; bu parcalar olmadan
        // trafik cekirdekten hic gelmez ve strateji sessizce hicbir sey yapmaz.
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

    // --- Yer tutucu cozumu ------------------------------------------------------

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
        // Bu testin butun mesele: once bosluklardan bol, SONRA yer tutucuyu coz.
        // Ters sirada yapilsaydi "C:\Program Files\..." iki argumana bolunurdu ve
        // winws dosyayi bulamazdi -- uretimde tespiti zor bir hata.
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
        // Paralel testin izolasyon mekanizmasi: strateji yalnizca kendi hedef IP'sine
        // uygulanir, diger paketler dokunulmadan gecer.
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
}
