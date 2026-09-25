using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Ampm2.Ui;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _run;
    private readonly Func<object?, bool>? _can;
    public RelayCommand(Action<object?> run, Func<object?, bool>? can = null) { _run = run; _can = can; }
    public RelayCommand(Action run, Func<bool>? can = null) : this(_ => run(), can == null ? null : _ => can()) { }
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? p) => _can?.Invoke(p) ?? true;
    public void Execute(object? p) => _run(p);
}

public sealed class AsyncCommand : ICommand
{
    private readonly Func<object?, Task> _run;
    private readonly Func<object?, bool>? _can;
    private bool _busy;
    public AsyncCommand(Func<object?, Task> run, Func<object?, bool>? can = null) { _run = run; _can = can; }
    public AsyncCommand(Func<Task> run, Func<bool>? can = null) : this(_ => run(), can == null ? null : _ => can()) { }
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? p) => !_busy && (_can?.Invoke(p) ?? true);
    public async void Execute(object? p)
    {
        _busy = true;
        CommandManager.InvalidateRequerySuggested();
        try { await _run(p); }
        catch (Exception ex) { App.Toast("Error", ex.Message, ToastKind.Error); }
        finally { _busy = false; CommandManager.InvalidateRequerySuggested(); }
    }
}
