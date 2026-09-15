using ZapretTr.Core.Profiles;
using ZapretTr.Prober;

namespace ZapretTr.Tests;

/// <summary>
/// Gercek servislerin gercek cevaplariyla profil eslesmesi.
/// </summary>
/// <remarks>
/// Dizgiler 2026-09-15'te olculdu: ipinfo.io'nun "org" alani ve ip-api.com'un "as" ve
/// "isp" alanlari, her saglayicinin duyurdugu oneklerden secilen adresler icin. ASN
/// sahipleri RIPEstat as-overview ile dogrulandi.
///
/// Neden: 0.2.0 tespitte once ipinfo.io'ya soruyor ve o, Turk Telekom sabit hattin adini
/// her zaman "Turk Telekomunikasyon" diye veriyor. TT Mobil profilindeki "turk telekom"
/// anahtar kelimesi bu adla da eslesiyordu; her TTNET kullanicisi test sirasinda "birden
/// fazla profille eslesti" sorusunu goruyordu. Vodafone Mobil'in ASN'si yoktu ve priority
/// sirasi yuzunden mobil kullaniciya Vodafone Net ONERILIYORDU.
///
/// Kural: tespitin onerdigi profil (BestMatch) baglantinin ASN'sine ait profil olmali;
/// soru yalnizca bir ASN'de gercekten iki tur hizmet varsa (Vodafone) cikmali.
/// </remarks>
public sealed class IspMatchRealWorldTests
{
    private static readonly ProfileStore Store = ProfileStore.Load();

    public static TheoryData<string, string, string, bool> Cevaplar => new()
    {
        // kaynak dizgi (ASN'yi de iceren), ASN'nin yanindaki ad, beklenen oneri, soru cikmali mi

        // Turk Telekom sabit: ipinfo ve ip-api'nin farkli yazimlari. Soru CIKMAMALI.
        { "AS9121 Turk Telekomunikasyon Anonim Sirketi", "Turk Telekomunikasyon Anonim Sirketi", "turk-telekom", false },
        { "AS9121 Turk Telekomunikasyon Anonim Sirketi", "Turk Telekomunikasyon A.S", "turk-telekom", false },
        { "AS9121 Turk Telekomunikasyon Anonim Sirketi", "TurkTelekom", "turk-telekom", false },

        // TT Mobil (eski Avea).
        { "AS20978 TT Mobil Iletisim Hizmetleri A.S", "TT Mobil Iletisim Hizmetleri A.S", "turk-telekom-mobil", false },
        { "AS20978 TT Mobil Iletisim Hizmetleri A.S", "Avea Iletisim Hizmetleri", "turk-telekom-mobil", false },

        // Turkcell mobil ve Superonline (ipinfo).
        { "AS16135 Turkcell A.S.", "Turkcell Internet", "turkcell-mobil", false },
        { "AS34984 Superonline Iletisim Hizmetleri A.S.", "Superonline Iletisim Hizmetleri A.S.", "superonline", false },

        // Vodafone: AS15897 karisik (cogu mobil, bir kismi FTTH), iki sabit ASN. Soru bilerek
        // CIKIYOR ama ONERI ASN'ye gore dogru profil.
        { "AS15897 Vodafone Telekomunikasyon A.S.", "Vodafone Telekomunikasyon A.S.", "vodafone-mobil", true },
        { "AS15897 Vodafone Telekomunikasyon A.S.", "Vodafone Turkey 3G Pools", "vodafone-mobil", true },
        { "AS15924 Vodafone Net Iletisim Hizmetler AS", "Vodafone Net Iletisim Hizmetler", "vodafone-net", true },
        { "AS8386 Vodafone Net Iletisim Hizmetler AS", "Vodafone Net DSL - KADIKOY", "vodafone-net", true },

        // Adinda baska saglayicinin anahtar kelimesi olmayanlar.
        { "AS12735 TurkNet Iletisim Hizmetleri A.S.", "TurkNet Iletisim Hizmetleri A.S.", "turknet", false },
        { "AS47524 Turksat Uydu Haberlesme Kablo TV ve Isletme A.S.", "Turksat Internet Services", "turksat", false },
        { "AS34296 Millenicom Telekomunikasyon Hizmetleri Anonim Sirketi", "Millenicom Telekomunikasyon Hizmetleri Anonim Sirketi", "millenicom", false },
    };

    [Theory]
    [MemberData(nameof(Cevaplar))]
    public void Gercek_cevapla_dogru_profil_oneriliyor(string asnDizgisi, string ad, string beklenen, bool soruCikmali)
    {
        var asn = IspDetector.ParseAsn(asnDizgisi);
        Assert.NotNull(asn);

        var sonuc = new IspDetectionResult(new IspIdentity(asn, ad, "test"), Store.Match(asn, ad));

        Assert.Equal(beklenen, sonuc.BestMatch?.Id);
        Assert.Equal(soruCikmali, sonuc.IsAmbiguous);
    }

    [Fact]
    public void Asn_bilinmezse_Turk_Telekom_adi_mobil_profille_eslesmiyor()
    {
        // ASN'siz yedek yol: "Turk Telekom" sabit hattin adi. TT Mobil'in kendi adlari
        // "TT Mobil" ve "Avea".
        var eslesenler = Store.Match(asn: null, orgName: "Turk Telekomunikasyon A.S");

        Assert.Equal(["turk-telekom"], eslesenler.Select(p => p.Id));
    }

    [Fact]
    public void Her_asn_tek_bir_profile_ait()
    {
        // Ayni ASN iki profilde olursa ASN eslesmesi de soru cikarir ve oneri priority
        // sirasina kalir -- tam da duzeltilen hata.
        var cakisan = Store.Profiles
            .SelectMany(p => p.Asns.Select(a => (Asn: a, p.Id)))
            .GroupBy(x => x.Asn)
            .Where(g => g.Count() > 1)
            .Select(g => $"AS{g.Key}: {string.Join(", ", g.Select(x => x.Id))}")
            .ToList();

        Assert.Empty(cakisan);
    }
}
