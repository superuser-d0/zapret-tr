using System.Windows.Threading;
using ZapretTr.App;

namespace ZapretTr.Tests;

/// <summary>
/// Kisayola ikinci kez tiklandiginda calisan ornegin penceresini one getirme el sikismasi.
/// </summary>
/// <remarks>
/// Gercek uygulama Global\ adli olaylar kullaniyor; testler yonetici yetkisi olmadan
/// kosabildigi icin Local\ ve her test icin ayri bir ad kullaniyor. Arayuz is
/// parcacigi gercek bir STA Dispatcher: dinleyici pencereyi onun uzerinden gosteriyor
/// ve uygulamadaki gibi ikinci ornek de STA is parcaciginda bekliyor.
/// </remarks>
public sealed class InstanceActivationTests
{
    private static string YeniAd() => @"Local\ZapretTR-test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void Calisan_ornek_pencereyi_gosterir_ve_ikinci_ornege_bildirir()
    {
        using var arayuz = new ArayuzIsParcacigi();
        var ad = YeniAd();
        var gosterildi = 0;

        using var dinleyici = InstanceActivation.StartListening(arayuz.Dispatcher, () =>
        {
            Assert.True(arayuz.Dispatcher.CheckAccess(), "Pencere arayuz is parcaciginda gosterilmeli.");
            Interlocked.Increment(ref gosterildi);
            return ShowOutcome.Shown;
        }, ad);

        Assert.NotNull(dinleyici);
        Assert.Equal(ActivationResult.Shown, StaIcinde(() => InstanceActivation.TryActivateExisting(TimeSpan.FromSeconds(5), ad)));
        Assert.Equal(1, gosterildi);

        // Ikinci tiklama da calismali (olaylar tek kullanimlik degil).
        Assert.Equal(ActivationResult.Shown, StaIcinde(() => InstanceActivation.TryActivateExisting(TimeSpan.FromSeconds(5), ad)));
        Assert.Equal(2, gosterildi);
    }

    [Fact]
    public void Kapanan_ornek_kapaniyor_der()
    {
        using var arayuz = new ArayuzIsParcacigi();
        var ad = YeniAd();

        using var dinleyici = InstanceActivation.StartListening(arayuz.Dispatcher, () => ShowOutcome.Closing, ad);

        Assert.Equal(ActivationResult.Closing, StaIcinde(() => InstanceActivation.TryActivateExisting(TimeSpan.FromSeconds(5), ad)));
    }

    [Fact]
    public void Dinleyen_yoksa_hemen_doner()
    {
        var sure = System.Diagnostics.Stopwatch.StartNew();

        Assert.Equal(ActivationResult.NoListener, InstanceActivation.TryActivateExisting(TimeSpan.FromSeconds(5), YeniAd()));
        Assert.True(sure.Elapsed < TimeSpan.FromSeconds(2), "Dinleyen yokken beklenmemeli: " + sure.Elapsed);
    }

    [Fact]
    public void Donmus_ornek_cevap_vermezse_sinirli_surede_doner()
    {
        using var arayuz = new ArayuzIsParcacigi();
        var ad = YeniAd();
        using var serbest = new ManualResetEventSlim();

        // Arayuz is parcacigi mesgul: gosterme istegi islenemez.
        arayuz.Dispatcher.BeginInvoke(() => serbest.Wait(TimeSpan.FromSeconds(10)));
        using var dinleyici = InstanceActivation.StartListening(arayuz.Dispatcher, () => ShowOutcome.Shown, ad);

        var sure = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(ActivationResult.NoAnswer, StaIcinde(() => InstanceActivation.TryActivateExisting(TimeSpan.FromSeconds(1), ad)));
        Assert.True(sure.Elapsed < TimeSpan.FromSeconds(4), "Cevapsiz ornek sinirli surede birakilmali: " + sure.Elapsed);

        serbest.Set();
    }

    [Fact]
    public void Dinleyici_kapatilinca_surec_dusmuyor_ve_ad_serbest_kaliyor()
    {
        using var arayuz = new ArayuzIsParcacigi();
        var ad = YeniAd();

        var dinleyici = InstanceActivation.StartListening(arayuz.Dispatcher, () => ShowOutcome.Shown, ad);
        Assert.NotNull(dinleyici);
        dinleyici!.Dispose();

        // Kapatilan dinleyicinin arka plan is parcacigi kapali tutamaca dokunsaydi test
        // sureci dusmus olurdu. Ad da artik kimse tarafindan tutulmuyor.
        Thread.Sleep(200);
        Assert.Equal(ActivationResult.NoListener, InstanceActivation.TryActivateExisting(TimeSpan.FromSeconds(1), ad));
    }

    /// <summary>Uygulamadaki gibi: ikinci ornek STA is parcaciginda bekler.</summary>
    private static T StaIcinde<T>(Func<T> is_)
    {
        T sonuc = default!;
        Exception? hata = null;
        var thread = new Thread(() =>
        {
            try { sonuc = is_(); }
            catch (Exception ex) { hata = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "STA is parcacigi 15 saniyede bitmedi.");
        if (hata is not null)
        {
            throw new InvalidOperationException("STA is parcaciginda hata", hata);
        }

        return sonuc;
    }

    private sealed class ArayuzIsParcacigi : IDisposable
    {
        public Dispatcher Dispatcher { get; }

        public ArayuzIsParcacigi()
        {
            Dispatcher? d = null;
            using var hazir = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                d = Dispatcher.CurrentDispatcher;
                hazir.Set();
                Dispatcher.Run();
            })
            {
                IsBackground = true,
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            hazir.Wait();
            Dispatcher = d!;
        }

        public void Dispose() => Dispatcher.InvokeShutdown();
    }
}
