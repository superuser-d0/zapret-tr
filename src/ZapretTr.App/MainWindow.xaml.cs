using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using ZapretTr.App.ViewModels;

namespace ZapretTr.App;

/// <summary>
/// MainWindow.xaml için etkileşim mantığı.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Kapanma iptal edilip temizlik koşuldu mu. İkinci kapanmada pencere gerçekten kapanır.
    /// </summary>
    private bool _cleanupRan;

    /// <summary>
    /// Kullanıcı GERÇEKTEN çıkmak istiyor mu ("Çıkış" düğmesi ya da tepsi menüsü).
    /// </summary>
    /// <remarks>
    /// X düğmesiyle ayrımın tek yolu bu bayrak: WPF ikisini de aynı Closing
    /// olayıyla bildiriyor.
    /// </remarks>
    private bool _exitRequested;

    private TrayIcon? _tray;

    public MainWindow()
    {
        InitializeComponent();

        // Varsayılan yükseklik, varsayılan durumdaki her şeyin (alt bilgi ve
        // "Ayrıntılar" dahil) kaydırmadan sığdığı ölçülmüş değer. 768 piksellik
        // bir dizüstü ekranda bu pencere ekrandan taşardı; üst kısım zaten
        // kayabildiği için çalışma alanına kırpmak hiçbir şeyi gizlemiyor.
        Height = Math.Max(MinHeight, Math.Min(Height, SystemParameters.WorkArea.Height));
        Closing += OnClosingAsync;
        Loaded += OnLoaded;

        // Başlık çubuğu Windows'un; tutamaç ancak burada var.
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
    /// Kısayola ikinci kez tıklandığında pencereyi öne getirir (InstanceActivation).
    /// </summary>
    /// <remarks>
    /// Kapanmakta olan pencere GÖSTERİLMEZ: WPF kapanma sırasında Show'u istisnayla
    /// reddediyor ("Cannot set Visibility ... while a Window is closing"). O durumda
    /// ikinci örnek bu örneğin çıkmasını bekleyip kendisi açılıyor.
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

    /// <summary>Gerçek çıkış: temizlik koşsun ve uygulama kapansın.</summary>
    private void Cik()
    {
        _exitRequested = true;
        _tray?.Goster();
        Close();
    }

    /// <summary>
    /// X ile kapatmak uygulamayı sonlandırmaz, bildirim alanına indirir.
    /// </summary>
    /// <remarks>
    /// Kullanıcının istediği davranış: koruma açıkken pencereyi kapatmak
    /// korumayı da kapatıyordu. Şimdi X yalnızca pencereyi gizliyor; winws ve
    /// dnscrypt çalışmaya devam ediyor. Uygulamayı gerçekten sonlandırmak
    /// "Çıkış" düğmesiyle ya da tepsi menüsüyle yapılıyor.
    ///
    /// Temizlik SADECE gerçek çıkışta koşuyor. X'te de koşarsa gizlenen
    /// uygulama korumayı kapatmış olurdu; yani özelliğin amacının tam tersi.
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

        // Close() DOĞRUDAN çağrılamaz. Hâlâ bu kapanma işleminin içindeyiz ve WPF
        // bunu reddediyor:
        //   "Cannot set Visibility to Visible or call Show, ShowDialog, Close, or
        //    WindowInteropHelper.EnsureHandle while a Window is closing."
        // İlk yazımda öyleydi ve duman testi, test barındırıcısını çökerterek
        // yakaladı; gerçek kullanıcıda da pencereyi kapatırken çökme olurdu.
        // Dispatcher'a bırakmak, mevcut kapanmanın çözülmesini bekletiyor.
        await Dispatcher.InvokeAsync(Close, DispatcherPriority.Background);
    }
}
