namespace ZapretTr.Prober;

/// <summary>
/// Motor (winws) hiçbir adayda başlamadığı için ölçüm YAPILAMADI.
/// </summary>
/// <remarks>
/// Bu, "hiçbir strateji işe yaramadı" ile karıştırılmaması gereken ayrı bir
/// durum. Gerçek bir kullanıcıda 304 aday 23 saniyede "denendi" ve hepsi aynı
/// sebeple düştü: winws hiç başlamadı. Arayüz sonunda "ÇALIŞAN STRATEJİ YOK"
/// dedi, kullanıcı da bu hatta aracın yetmediğini düşündü.
///
/// Oysa doğru cümle "hiçbir strateji DENENEMEDİ" idi. Fark kullanıcı için
/// belirleyici: birincisi "başka bir çözüm ara" demek, ikincisi "makinede bir
/// şey bozuk, düzelt ve tekrar dene" demek. Aynı ekranı göstermek kullanıcıyı
/// yanlış yöne gönderiyordu.
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
