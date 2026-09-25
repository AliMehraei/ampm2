using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Ampm2.Sys;

/// <summary>Minimal Shell_NotifyIcon wrapper (avoids loading WinForms just for a tray icon).</summary>
public sealed class TrayIcon : IDisposable
{
    private const int WM_APP_TRAY = 0x8000 + 42;
    private const int WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_LBUTTONDBLCLK = 0x0203;
    private const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const int NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_SHOWTIP = 0x80;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIconW(int msg, ref NOTIFYICONDATAW data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessageW(string s);

    private readonly HwndSource _src;
    private NOTIFYICONDATAW _data;
    private readonly int _taskbarCreated;
    private bool _added;
    public event Action? LeftClick;
    public event Action? RightClick;

    public TrayIcon(IntPtr hIcon, string tip)
    {
        _src = new HwndSource(new HwndSourceParameters("ampm2-tray") { Width = 0, Height = 0, WindowStyle = 0 });
        _src.AddHook(WndProc);
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        _data = new NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _src.Handle,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP,
            uCallbackMessage = WM_APP_TRAY,
            hIcon = hIcon,
            szTip = tip,
            szInfo = "", szInfoTitle = "",
        };
        Add();
    }

    private void Add()
    {
        _added = Shell_NotifyIconW(NIM_ADD, ref _data);
        _data.uVersion = 4;
        Shell_NotifyIconW(NIM_SETVERSION, ref _data);
    }

    public void Update(IntPtr hIcon, string tip)
    {
        _data.hIcon = hIcon;
        _data.szTip = tip.Length > 127 ? tip[..127] : tip;
        if (_added) Shell_NotifyIconW(NIM_MODIFY, ref _data); else Add();
    }

    public IntPtr Handle => _src.Handle;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_APP_TRAY)
        {
            int ev = (int)((long)lParam & 0xFFFF);
            if (ev == WM_LBUTTONUP) LeftClick?.Invoke();
            else if (ev == WM_RBUTTONUP || ev == 0x007B /* WM_CONTEXTMENU */) RightClick?.Invoke();
            handled = true;
        }
        else if (msg == _taskbarCreated) { _added = false; Add(); }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_added) Shell_NotifyIconW(NIM_DELETE, ref _data);
        _added = false;
        _src.Dispose();
    }
}
