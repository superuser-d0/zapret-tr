using System.Windows.Input;

namespace ZapretTr.App;

/// <summary>
/// Basit ICommand uygulaması.
/// </summary>
/// <remarks>
/// Harici bir MVVM kütüphanesi eklemek yerine bu yazıldı: uygulamanın bağımlılık
/// yüzeyi kasıtlı olarak sıfır tutuluyor. Paket yakalama sürücüsü taşıyan ve
/// antivirüs tarafından zaten şüpheyle karşılanacak bir uygulamada her ek
/// bağımlılık hem imza yüzeyini hem de açıklamamız gereken şeyi büyütüyor.
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
