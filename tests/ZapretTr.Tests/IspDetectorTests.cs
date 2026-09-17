using ZapretTr.Prober;

namespace ZapretTr.Tests;

/// <summary>
/// ASN ayıklaması. Ağ erişimi gerektirmeyen, saf kısım.
/// </summary>
/// <remarks>
/// Bu ayıklama yanlış çalışırsa "Bilmiyorum" akışı sessizce yanlış profili
/// önerir: kullanıcı test yapar, İSS'ine ait olmayan adaylar denenir ve sonuç
/// alınamaz. Hata görünür bir çökme değil, sadece "çalışmıyor" hissi olurdu.
/// </remarks>
public sealed class IspDetectorTests
{
    [Theory]
    [InlineData("AS9121 Turk Telekomunikasyon Anonim Sirketi", 9121)]
    [InlineData("AS34984 TELLCOM-AS", 34984)]
    [InlineData("as16135 Turkcell", 16135)]
    [InlineData("AS206375 NETSPEED INTERNET A.S.", 206375)]
    public void AsnDizgiden_Ayiklanir(string text, int expected)
    {
        Assert.Equal(expected, IspDetector.ParseAsn(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Turk Telekomunikasyon Anonim Sirketi")]
    [InlineData("ASN yok burada")]
    public void AsnYoksa_Null(string? text)
    {
        Assert.Null(IspDetector.ParseAsn(text));
    }

    [Fact]
    public void RakamOlmayan_AS_Onegi_Eslesmez()
    {
        // "ASELSAN" gibi bir kuruluş adı AS ile başlıyor ama ASN değil.
        Assert.Null(IspDetector.ParseAsn("ASELSAN Elektronik"));
    }

    [Fact]
    public void MetnnIcindeki_Asn_de_Bulunur()
    {
        // Bazı servisler ASN'yi cümlenin ortasında veriyor.
        Assert.Equal(12735, IspDetector.ParseAsn("Provider AS12735 TurkNet Iletisim"));
    }

    [Fact]
    public void KimlikBos_ise_Bilinmeyen_Sayilir()
    {
        Assert.False(new IspIdentity(null, null, "test").IsKnown);
        Assert.False(new IspIdentity(null, "   ", "test").IsKnown);
        Assert.True(new IspIdentity(9121, null, "test").IsKnown);
        Assert.True(new IspIdentity(null, "TTNET", "test").IsKnown);
    }

    [Fact]
    public void TekEslesme_Belirsiz_Sayilmaz()
    {
        var store = ZapretTr.Core.Profiles.ProfileStore.Load();
        var matches = store.Match(34984, null);

        var result = new IspDetectionResult(new IspIdentity(34984, null, "test"), matches);

        Assert.False(result.IsAmbiguous);
        Assert.Equal("superonline", result.BestMatch?.Id);
    }

    [Fact]
    public void CokluEslesme_Belirsiz_Sayilir()
    {
        // "vodafone" hem sabit hat hem mobil profiliyle eşleşir. Bu durumda
        // birini sessizce seçmek yerine kullanıcıya sormak doğru davranış.
        var store = ZapretTr.Core.Profiles.ProfileStore.Load();
        var matches = store.Match(null, "Vodafone Turkey");

        var result = new IspDetectionResult(new IspIdentity(null, "Vodafone Turkey", "test"), matches);

        Assert.True(result.IsAmbiguous);
    }
}
