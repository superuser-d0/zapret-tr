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
        Closing += OnClosingAsync;
        Loaded += OnLoaded;
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
