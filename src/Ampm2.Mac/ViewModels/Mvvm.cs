using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Ampm2.Mac.ViewModels;

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

/// <summary>
/// Command whose CanExecute is re-evaluated when anything calls <see cref="Command.Requery"/>
/// (Avalonia has no WPF-style CommandManager).
/// </summary>
public sealed class Command : ICommand
{
    private static event Action? RequeryAll;
    public static void Requery() => RequeryAll?.Invoke();

    private readonly Func<object?, Task> _run;
    private readonly Func<object?, bool>? _can;
    private bool _busy;

    public Command(Action<object?> run, Func<object?, bool>? can = null) : this(p => { run(p); return Task.CompletedTask; }, can) { }
    public Command(Action run, Func<bool>? can = null) : this(_ => run(), can == null ? null : _ => can()) { }
    public Command(Func<object?, Task> run, Func<object?, bool>? can = null)
    {
        _run = run; _can = can;
        RequeryAll += () => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
    public static Command Async(Func<Task> run, Func<bool>? can = null) => new(_ => run(), can == null ? null : _ => can());

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? p) => !_busy && (_can?.Invoke(p) ?? true);

    public async void Execute(object? p)
    {
        if (!CanExecute(p)) return;
        _busy = true; Requery();
        try { await _run(p); }
        catch (Exception ex) { App.ReportError(ex); }
        finally { _busy = false; Requery(); }
    }
}

public enum ToastKind { Info, Success, Error }

public sealed class Toast
{
    public Toast(string title, string message, ToastKind kind) { Title = title; Message = message; Kind = kind; }
    public string Title { get; }
    public string Message { get; }
    public ToastKind Kind { get; }
    public bool IsError => Kind == ToastKind.Error;
    public bool IsSuccess => Kind == ToastKind.Success;
}

public enum LogKind { Out, Err, Marker }

public sealed class LogLine
{
    public LogLine(string text, LogKind kind) { Text = text; Kind = kind; }
    public string Text { get; }
    public LogKind Kind { get; }
    public bool IsErr => Kind == LogKind.Err;
    public bool IsMarker => Kind == LogKind.Marker;
}

public sealed class DialogModel
{
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string OkText { get; init; } = "OK";
    public bool Danger { get; init; }
    public TaskCompletionSource<bool> Result { get; } = new();
}
