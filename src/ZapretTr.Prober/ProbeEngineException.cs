namespace ZapretTr.Prober;

/// <summary>
/// Motor (winws) hicbir adayda baslamadigi icin olcum YAPILAMADI.
/// </summary>
/// <remarks>
/// Bu, "hicbir strateji ise yaramadi" ile karistirilmamasi gereken ayri bir
/// durum. Gercek bir kullanicida 304 aday 23 saniyede "denendi" ve hepsi ayni
/// sebeple dustu: winws hic baslamadi. Arayuz sonunda "CALISAN STRATEJI YOK"
/// dedi, kullanici da bu hatta aracin yetmedigini dusundu.
///
/// Oysa dogru cumle "hicbir strateji DENENEMEDI" idi. Fark kullanici icin
/// belirleyici: birincisi "baska bir cozum ara" demek, ikincisi "makinede bir
/// sey bozuk, duzelt ve tekrar dene" demek. Ayni ekrani gostermek, kullaniciyi
/// yanlis yone gonderiyordu.
/// </remarks>
public sealed class ProbeEngineException : Exception
{
    public ProbeEngineException(string message)
        : base(message)
    {
    }

    public ProbeEngineException()
    {
    }

    public ProbeEngineException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
