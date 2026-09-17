using ZapretTr.Core.Profiles;
using ZapretTr.Prober;

namespace ZapretTr.Tests;

/// <summary>
/// Gerçek servislerin gerçek cevaplarıyla profil eşleşmesi.
/// </summary>
/// <remarks>
/// Dizgiler 2026-09-15'te ölçüldü: ipinfo.io'nun "org" alanı ve ip-api.com'un "as" ve
/// "isp" alanları, her sağlayıcının duyurduğu öneklerden seçilen adresler için. ASN
/// sahipleri RIPEstat as-overview ile doğrulandı.
///
/// Neden: 0.2.0 tespitte önce ipinfo.io'ya soruyor ve o, Türk Telekom sabit hattın adını
/// her zaman "Turk Telekomunikasyon" diye veriyor. TT Mobil profilindeki "turk telekom"
/// anahtar kelimesi bu adla da eşleşiyordu; her TTNET kullanıcısı test sırasında "birden
/// fazla profille eşleşti" sorusunu görüyordu. Vodafone Mobil'in ASN'si yoktu ve priority
/// sırası yüzünden mobil kullanıcıya Vodafone Net ÖNERİLİYORDU.
///
/// Kural: tespitin önerdiği profil (BestMatch) bağlantının ASN'sine ait profil olmalı;
/// soru yalnızca bir ASN'de gerçekten iki tür hizmet varsa (Vodafone) çıkmalı.
/// </remarks>
public sealed class IspMatchRealWorldTests
{
    private static readonly ProfileStore Store = ProfileStore.Load();

    public static TheoryData<string, string, string, bool> Cevaplar => new()
    {
        // kaynak dizgi (ASN'yi de içeren), ASN'nin yanındaki ad, beklenen öneri, soru çıkmalı mı

        // Türk Telekom sabit: ipinfo ve ip-api'nin farklı yazımları. Soru ÇIKMAMALI.
        { "AS9121 Turk Telekomunikasyon Anonim Sirketi", "Turk Telekomunikasyon Anonim Sirketi", "turk-telekom", false },
        { "AS9121 Turk Telekomunikasyon Anonim Sirketi", "Turk Telekomunikasyon A.S", "turk-telekom", false },
        { "AS9121 Turk Telekomunikasyon Anonim Sirketi", "TurkTelekom", "turk-telekom", false },

        // TT Mobil (eski Avea).
        { "AS20978 TT Mobil Iletisim Hizmetleri A.S", "TT Mobil Iletisim Hizmetleri A.S", "turk-telekom-mobil", false },
        { "AS20978 TT Mobil Iletisim Hizmetleri A.S", "Avea Iletisim Hizmetleri", "turk-telekom-mobil", false },

        // Turkcell mobil ve Superonline (ipinfo).
        { "AS16135 Turkcell A.S.", "Turkcell Internet", "turkcell-mobil", false },
        { "AS34984 Superonline Iletisim Hizmetleri A.S.", "Superonline Iletisim Hizmetleri A.S.", "superonline", false },

        // Vodafone: AS15897 karışık (çoğu mobil, bir kısmı FTTH), iki sabit ASN. Soru bilerek
        // ÇIKIYOR ama ÖNERİ ASN'ye göre doğru profil.
        { "AS15897 Vodafone Telekomunikasyon A.S.", "Vodafone Telekomunikasyon A.S.", "vodafone-mobil", true },
        { "AS15897 Vodafone Telekomunikasyon A.S.", "Vodafone Turkey 3G Pools", "vodafone-mobil", true },
        { "AS15924 Vodafone Net Iletisim Hizmetler AS", "Vodafone Net Iletisim Hizmetler", "vodafone-net", true },
        { "AS8386 Vodafone Net Iletisim Hizmetler AS", "Vodafone Net DSL - KADIKOY", "vodafone-net", true },

        // Adında başka sağlayıcının anahtar kelimesi olmayanlar.
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
        // ASN'siz yedek yol: "Turk Telekom" sabit hattın adı. TT Mobil'in kendi adları
        // "TT Mobil" ve "Avea".
        var eslesenler = Store.Match(asn: null, orgName: "Turk Telekomunikasyon A.S");

        Assert.Equal(["turk-telekom"], eslesenler.Select(p => p.Id));
    }

    [Fact]
    public void Her_asn_tek_bir_profile_ait()
    {
        // Aynı ASN iki profilde olursa ASN eşleşmesi de soru çıkarır ve öneri priority
        // sırasına kalır; tam da düzeltilen hata.
        var cakisan = Store.Profiles
            .SelectMany(p => p.Asns.Select(a => (Asn: a, p.Id)))
            .GroupBy(x => x.Asn)
            .Where(g => g.Count() > 1)
            .Select(g => $"AS{g.Key}: {string.Join(", ", g.Select(x => x.Id))}")
            .ToList();

        Assert.Empty(cakisan);
    }
}
