using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ZapretTr.App;

/// <summary>Çalışan örneğin pencereyi gösterme isteğine cevabı.</summary>
public enum ShowOutcome
{
    /// <summary>Pencere öne getirildi.</summary>
    Shown,

    /// <summary>Örnek kapanmakta; pencere gösterilemez.</summary>
    Closing,
}

/// <summary>İkinci örneğin, çalışan örneğe "pencereni göster" demesinin sonucu.</summary>
public enum ActivationResult
{
    /// <summary>Dinleyen bir örnek yok (başka oturumda olabilir ya da henüz açılmadı).</summary>
    NoListener,

    /// <summary>Çalışan örnek pencereyi öne getirdi.</summary>
    Shown,

    /// <summary>Çalışan örnek kapanmakta.</summary>
    Closing,

    /// <summary>Çalışan örnek zamanında cevap vermedi (donmuş olabilir).</summary>
    NoAnswer,
}

/// <summary>
/// Kısayola ikinci kez tıklandığında uyarı yerine çalışan örneğin penceresini öne getirir.
/// </summary>
/// <remarks>
/// Eskiden ikinci örnek "ZapretTR zaten çalışıyor" penceresi gösterip çıkıyordu; pencereyi
/// X ile kapatan (yani bildirim alanına indiren) kullanıcı kısayola tıklayınca uyarı
/// görüyor, Tamam deyince yine pencereye ulaşamıyordu.
///
/// İki örneğin aynı anda çalışmasını engelleyen kilit DEĞİŞMEDİ (App.xaml.cs,
/// DnsGuard.AppInstanceMutexName). Burada yalnızca ikinci örnek ile ilki arasında
/// adlandırılmış olaylarla bir el sıkışma var:
///
///   ikinci -> "göster"          ilk -> pencereyi gösterir -> "gösterildi"
///                               ilk kapanıyorsa           -> "kapanıyor"
///
/// Ad oturum numarasını taşıyor: tek örnek kilidi Global, ama başka bir Windows
/// oturumundaki pencereyi öne getirmek o kullanıcıya bir şey göstermez. O durumda
/// dinleyici bulunamıyor ve eski uyarı gösteriliyor.
///
/// Kapanmakta olan bir WPF penceresini göstermek istisna fırlatıyor; bu yüzden ilk
/// örnek o durumda göstermeyip "kapanıyor" diyor ve ikinci örnek ilkinin çıkmasını
/// bekleyip normal açılıyor.
/// </remarks>
public static class InstanceActivation
{
    /// <summary>Bu oturumun olay adı öneki.</summary>
    public static string SessionPrefix => $@"Global\ZapretTR-pencere-{Process.GetCurrentProcess().SessionId}";

    /// <summary>
    /// Çalışan örnek olarak "göster" isteklerini dinlemeye başlar. Olaylar
    /// kurulamazsa null döner; ikinci örnek o zaman eski uyarıyı gösterir.
    /// </summary>
    /// <param name="dispatcher">Pencerenin iş parçacığı.</param>
    /// <param name="tryShow">Pencereyi gösterir; kapanıyorsa <see cref="ShowOutcome.Closing"/>.</param>
    /// <param name="prefix">Olay adı öneki; testler Local\ ile yalıtıyor.</param>
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

    /// <summary>Çalışan örnekten pencereyi öne getirmesini ister.</summary>
    /// <param name="answerTimeout">Cevap için en fazla bu kadar beklenir.</param>
    /// <param name="prefix">Olay adı öneki; testler Local\ ile yalıtıyor.</param>
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
                        // Önceki bir isteğin kalmış cevabı bu isteğin cevabı sanılmasın.
                        gosterildi.Reset();
                        kapaniyor.Reset();

                        // Windows arka plandaki bir sürecin kendini öne çıkarmasına izin
                        // vermiyor; kullanıcının az önce açtığı bu süreç verebiliyor.
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
            // İzin verilemediyse pencere yine gösterilir; yalnızca görev çubuğunda
            // yanıp sönebilir.
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
                    // Sınırlı bekleme: arayüz iş parçacığı meşgulse bu iş parçacığı
                    // sonsuza kadar asılı kalmasın. Cevap gelmezse ikinci örnek eski
                    // uyarıyı gösterir.
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
                    // Dispatcher kapandı ya da gösterme başarısız: cevap verilmez.
                }
            }
        }

        public void Dispose()
        {
            _dur.Set();

            // Tutamaçlar iş parçacığı çıkmadan kapatılmamalı: WaitAny kapalı bir
            // tutamaç üzerinde istisna fırlatır ve arka plan iş parçacığındaki
            // yakalanmamış istisna bütün süreci düşürür. İş parçacığı bir tryShow
            // çağrısının içindeyse en fazla o çağrının 2 saniyelik sınırı kadar sürer.
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
