using System.ComponentModel;
using System.Windows;
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

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosingAsync;
    }

    /// <summary>
    /// Pencere kapanirken winws'i durdurur ve sistem DNS'ini geri alir.
    /// </summary>
    /// <remarks>
    /// Bu isleyici olmadan pencereyi X ile kapatmak HICBIR SEY temizlemiyordu:
    /// temizlik yalnizca "Çıkış" dugmesinin icindeydi. Gercek makinede olculdu --
    /// koruma acikken pencere kapatildi, winws ve dnscrypt-proxy oksuz kaldi,
    /// sistem DNS'i 127.0.0.1'de kaldi ve DNS yedegi diskte "geri alinmamis"
    /// olarak durdu. dnscrypt sonradan olurse makine hicbir adi cozemez.
    ///
    /// Kapanma once IPTAL ediliyor cunku temizlik asenkron ve pencere kapanma
    /// isleyicisi beklenemez; temizlik bitince Close() yeniden cagriliyor ve bu
    /// sefer isleyici yol veriyor.
    /// </remarks>
    private async void OnClosingAsync(object? sender, CancelEventArgs e)
    {
        if (_cleanupRan)
        {
            return;
        }

        e.Cancel = true;
        _cleanupRan = true;

        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.ShutdownAsync();
        }

        Close();
    }
}
