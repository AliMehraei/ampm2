using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ampm2.Ui;

public enum LogKind { Out, Err, Marker }

public sealed class LogLine
{
    public LogLine(string text, LogKind kind, string time = "") { Text = text; Kind = kind; Time = time; }
    public string Text { get; }
    public LogKind Kind { get; }
    public string Time { get; }
    public bool IsErr => Kind == LogKind.Err;
    public bool IsMarker => Kind == LogKind.Marker;
}

public enum ToastKind { Info, Success, Error }

public sealed class Toast
{
    public Toast(string title, string message, ToastKind kind) { Title = title; Message = message; Kind = kind; }
    public string Title { get; }
    public string Message { get; }
    public ToastKind Kind { get; }
    public string Glyph => Kind switch { ToastKind.Success => "", ToastKind.Error => "", _ => "" };
    public string BrushKey => Kind switch { ToastKind.Success => "Online", ToastKind.Error => "Errored", _ => "Accent" };
}
