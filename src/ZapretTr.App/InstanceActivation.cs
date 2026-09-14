using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ZapretTr.App;

/// <summary>Calisan ornegin pencereyi gosterme istegine cevabi.</summary>
public enum ShowOutcome
{
    /// <summary>Pencere one getirildi.</summary>
    Shown,

    /// <summary>Ornek kapanmakta; pencere gosterilemez.</summary>
    Closing,
}

/// <summary>Ikinci ornegin, calisan ornege "pencereni goster" demesinin sonucu.</summary>
public enum ActivationResult
{
    /// <summary>Dinleyen bir ornek yok (baska oturumda olabilir ya da henuz acilmadi).</summary>
    NoListener,

    /// <summary>Calisan ornek pencereyi one getirdi.</summary>
    Shown,

    /// <summary>Calisan ornek kapanmakta.</summary>
    Closing,

    /// <summary>Calisan ornek zamaninda cevap vermedi (donmus olabilir).</summary>
    NoAnswer,
}

/// <summary>
/// Kisayola ikinci kez tiklandiginda uyari yerine calisan ornegin penceresini one getirir.
/// </summary>
/// <remarks>
/// Eskiden ikinci ornek "ZapretTR zaten çalışıyor" penceresi gosterip cikiyordu; pencereyi
/// X ile kapatan (yani bildirim alanina indiren) kullanici kisayola tiklayinca uyari
/// goruyor, Tamam deyince yine pencereye ulasamiyordu.
///
/// Iki ornegin ayni anda calismasini engelleyen kilit DEGISMEDI (App.xaml.cs,
/// DnsGuard.AppInstanceMutexName). Burada yalnizca ikinci ornek ile ilki arasinda
/// adlandirilmis olaylarla bir el sikisma var:
///
///   ikinci -> "goster"          ilk -> pencereyi gosterir -> "gosterildi"
///                               ilk kapaniyorsa           -> "kapaniyor"
///
/// Ad oturum numarasini tasiyor: tek ornek kilidi Global, ama baska bir Windows
/// oturumundaki pencereyi one getirmek o kullaniciya bir sey gostermez. O durumda
/// dinleyici bulunamiyor ve eski uyari gosteriliyor.
///
/// Kapanmakta olan bir WPF penceresini gostermek istisna firlatiyor; bu yuzden ilk
/// ornek o durumda gostermeyip "kapaniyor" diyor ve ikinci ornek ilkinin cikmasini
/// bekleyip normal aciliyor.
/// </remarks>
public static class InstanceActivation
{
    /// <summary>Bu oturumun olay adi oneki.</summary>
    public static string SessionPrefix => $@"Global\ZapretTR-pencere-{Process.GetCurrentProcess().SessionId}";

    /// <summary>
    /// Calisan ornek olarak "goster" isteklerini dinlemeye baslar. Olaylar
    /// kurulamazsa null doner; ikinci ornek o zaman eski uyariyi gosterir.
    /// </summary>
    /// <param name="dispatcher">Pencerenin is parcacigi.</param>
    /// <param name="tryShow">Pencereyi gosterir; kapaniyorsa <see cref="ShowOutcome.Closing"/>.</param>
    /// <param name="prefix">Olay adi oneki; testler Local\ ile yaliyor.</param>
    public static IDisposable? StartListening(Dispatcher dispatcher, Func<ShowOutcome> tryShow, string? prefix = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(tryShow);

        try
        {
            return new Listener(dispatcher, tryShow, prefix ?? SessionPrefix);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
        {
            return null;
        }
    }

    /// <summary>Calisan ornekten pencereyi one getirmesini ister.</summary>
    /// <param name="answerTimeout">Cevap icin en fazla bu kadar beklenir.</param>
    /// <param name="prefix">Olay adi oneki; testler Local\ ile yaliyor.</param>
    public static ActivationResult TryActivateExisting(TimeSpan answerTimeout, string? prefix = null)
    {
        var ad = prefix ?? SessionPrefix;

        try
        {
            if (!EventWaitHandle.TryOpenExisting(ad + "-goster", out var goster))
            {
                return ActivationResult.NoListener;
            }

            using (goster)
            {
                if (!EventWaitHandle.TryOpenExisting(ad + "-gosterildi", out var gosterildi))
                {
                    return ActivationResult.NoListener;
                }

                using (gosterildi)
                {
                    if (!EventWaitHandle.TryOpenExisting(ad + "-kapaniyor", out var kapaniyor))
                    {
                        return ActivationResult.NoListener;
                    }

                    using (kapaniyor)
                    {
                        // Onceki bir istegin kalmis cevabi bu istegin cevabi sanilmasin.
                        gosterildi.Reset();
                        kapaniyor.Reset();

                        // Windows arka plandaki bir surecin kendini one cikarmasina izin
                        // vermiyor; kullanicinin az once actigi bu surec verebiliyor.
                        AllowOtherInstancesToComeForward();

                        goster.Set();

                        return WaitHandle.WaitAny([gosterildi, kapaniyor], answerTimeout) switch
                        {
                            0 => ActivationResult.Shown,
                            1 => ActivationResult.Closing,
                            _ => ActivationResult.NoAnswer,
                        };
                    }
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
        {
            return ActivationResult.NoListener;
        }
    }

    private static void AllowOtherInstancesToComeForward()
    {
        try
        {
            using var ben = Process.GetCurrentProcess();
            foreach (var surec in Process.GetProcessesByName(ben.ProcessName))
            {
                using (surec)
                {
                    if (surec.Id != ben.Id)
                    {
                        _ = AllowSetForegroundWindow(surec.Id);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Izin verilemediyse pencere yine gosterilir; yalnizca gorev cubugunda
            // yanip sonebilir.
        }
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    private sealed class Listener : IDisposable
    {
        private readonly EventWaitHandle _goster;
        private readonly EventWaitHandle _gosterildi;
        private readonly EventWaitHandle _kapaniyor;
        private readonly ManualResetEvent _dur = new(false);
        private readonly Dispatcher _dispatcher;
        private readonly Func<ShowOutcome> _tryShow;
        private readonly Thread _thread;

        public Listener(Dispatcher dispatcher, Func<ShowOutcome> tryShow, string prefix)
        {
            _dispatcher = dispatcher;
            _tryShow = tryShow;
            _goster = new EventWaitHandle(false, EventResetMode.AutoReset, prefix + "-goster");
            _gosterildi = new EventWaitHandle(false, EventResetMode.AutoReset, prefix + "-gosterildi");
            _kapaniyor = new EventWaitHandle(false, EventResetMode.AutoReset, prefix + "-kapaniyor");

            _thread = new Thread(Dinle) { IsBackground = true, Name = "ZapretTR pencere istegi" };
            _thread.Start();
        }

        private void Dinle()
        {
            while (WaitHandle.WaitAny([_dur, _goster]) == 1)
            {
                try
                {
                    // Sinirli bekleme: arayuz is parcacigi mesgulse bu is parcacigi
                    // sonsuza kadar asili kalmasin. Cevap gelmezse ikinci ornek eski
                    // uyariyi gosterir.
                    var sonuc = (ShowOutcome?)_dispatcher.Invoke(
                        () => (ShowOutcome?)_tryShow(),
                        DispatcherPriority.Normal,
                        CancellationToken.None,
                        TimeSpan.FromSeconds(2));

                    if (sonuc == ShowOutcome.Shown)
                    {
                        _gosterildi.Set();
                    }
                    else if (sonuc == ShowOutcome.Closing)
                    {
                        _kapaniyor.Set();
                    }
                }
                catch (Exception)
                {
                    // Dispatcher kapandi ya da gosterme basarisiz: cevap verilmez.
                }
            }
        }

        public void Dispose()
        {
            _dur.Set();

            // Tutamaclar is parcacigi cikmadan kapatilmamali: WaitAny kapali bir
            // tutamac uzerinde istisna firlatir ve arka plan is parcacigindaki
            // yakalanmamis istisna butun sureci dusurur. Is parcacigi bir tryShow
            // cagrisinin icindeyse en fazla o cagrinin 2 saniyelik siniri kadar surer.
            if (!_thread.Join(TimeSpan.FromSeconds(3)))
            {
                return;
            }

            _goster.Dispose();
            _gosterildi.Dispose();
            _kapaniyor.Dispose();
        }
    }
}
