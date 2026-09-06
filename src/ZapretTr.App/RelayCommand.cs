using System.Windows.Input;

namespace ZapretTr.App;

/// <summary>
/// Basit ICommand uygulamasi.
/// </summary>
/// <remarks>
/// Harici bir MVVM kutuphanesi eklemek yerine bu yazildi: uygulamanin bagimlilik
/// yuzeyi kasitli olarak sifir tutuluyor. Paket yakalama surucusu tasiyan ve
/// antivirus tarafindan zaten suphe ile karsilanacak bir uygulamada, her ek
/// bagimlilik hem imza yuzeyini hem de aciklamamiz gereken seyi buyutuyor.
/// </remarks>
public sealed class RelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    private bool _isRunning;

    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
        => !_isRunning && (canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isRunning = true;
        RaiseCanExecuteChanged();

        try
        {
            await execute(parameter).ConfigureAwait(true);
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
