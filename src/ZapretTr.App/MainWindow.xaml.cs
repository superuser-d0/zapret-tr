using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using ZapretTr.App.ViewModels;

namespace ZapretTr.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Kapanma iptal edilip temizlik kosuldu mu. Ikinci kapanmada pencere gercekten kapanir.
    /// </summary>
    private bool _cleanupRan;

    /// <summary>
    /// Kullanici GERCEKTEN cikmak istiyor mu ("Çıkış" dugmesi ya da tepsi menusu).
    /// </summary>
    /// <remarks>
    /// X dugmesiyle ayrimin tek yolu bu bayrak: WPF ikisini de ayni Closing
    /// olayiyla bildiriyor.
    /// </remarks>
    private bool _exitRequested;

    private TrayIcon? _tray;

    public MainWindow()
    {
        InitializeComponent();

        // Varsayilan yukseklik, varsayilan durumdaki her seyin (alt bilgi ve
        // "Ayrıntılar" dahil) kaydirmadan sigdigi olculmus deger. 768 piksellik
        // bir dizustu ekranda bu pencere ekrandan tasardi; ust kisim zaten
        // kayabildigi icin calisma alanina kirpmak hicbir seyi gizlemiyor.
        Height = Math.Max(MinHeight, Math.Min(Height, SystemParameters.WorkArea.Height));
        Closing += OnClosingAsync;
        Loaded += OnLoaded;

        // Baslik cubugu Windows'un; tutamac ancak burada var.
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _tray = new TrayIcon(this);
        _tray.ExitRequested += (_, _) => Cik();

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ExitRequested += (_, _) => Cik();
        }
    }

    /// <summary>
    /// Kisayola ikinci kez tiklandiginda pencereyi one getirir (InstanceActivation).
    /// </summary>
    /// <remarks>
    /// Kapanmakta olan pencere GOSTERILMEZ: WPF kapanma sirasinda Show'u istisnayla
    /// reddediyor ("Cannot set Visibility ... while a Window is closing"). O durumda
    /// ikinci ornek bu ornegin cikmasini bekleyip kendisi aciliyor.
    /// </remarks>
    public ShowOutcome BringToFront()
    {
        if (_exitRequested || _cleanupRan)
        {
            return ShowOutcome.Closing;
        }

        if (_tray is not null)
        {
            _tray.Goster();
        }
        else
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
        }

        return ShowOutcome.Shown;
    }

    /// <summary>Gercek cikis: temizlik kossun ve uygulama kapansin.</summary>
    private void Cik()
    {
        _exitRequested = true;
        _tray?.Goster();
        Close();
    }

    /// <summary>
    /// X ile kapatmak uygulamayi sonlandirmaz, bildirim alanina indirir.
    /// </summary>
    /// <remarks>
    /// Kullanicinin istedigi davranis: koruma acikken pencereyi kapatmak
    /// korumayi da kapatiyordu. Simdi X yalnizca pencereyi gizliyor; winws ve
    /// dnscrypt calismaya devam ediyor. Uygulamayi gercekten sonlandirmak
    /// "Çıkış" dugmesiyle ya da tepsi menusuyle yapiliyor.
    ///
    /// Temizlik SADECE gercek cikista kosuyor. X'te de kosursa, gizlenen
    /// uygulama korumayi kapatmis olurdu -- yani ozelligin amacinin tam tersi.
    /// </remarks>
    private async void OnClosingAsync(object? sender, CancelEventArgs e)
    {
        if (!_exitRequested)
        {
            e.Cancel = true;
            _tray?.Gizle();
            return;
        }

        if (_cleanupRan)
        {
            _tray?.Dispose();
            return;
        }

        e.Cancel = true;
        _cleanupRan = true;

        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.ShutdownAsync();
        }

        // Close() DOGRUDAN cagrilamaz. Hala bu kapanma isleminin icindeyiz ve WPF
        // bunu reddediyor:
        //   "Cannot set Visibility to Visible or call Show, ShowDialog, Close, or
        //    WindowInteropHelper.EnsureHandle while a Window is closing."
        // Ilk yazimda oyleydi ve duman testi test barindiricisini cokerterek
        // yakaladi -- gercek kullanicida da pencereyi kapatirken cokme olurdu.
        // Dispatcher'a birakmak, mevcut kapanmanin cozulmesini bekletiyor.
        await Dispatcher.InvokeAsync(Close, DispatcherPriority.Background);
    }
}
