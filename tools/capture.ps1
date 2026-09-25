# Captures a window of a running process to PNG with PrintWindow (works while covered).
#   pwsh tools\capture.ps1 -ProcessId 1234 -Out shot.png
param([int]$ProcessId, [string]$Out, [string]$TitleLike = "")
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class Cap {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static IntPtr Find(uint pid, string like) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h)) {
        var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
        var t = sb.ToString();
        if (t.Length > 0 && (like.Length == 0 || t.Contains(like))) { found = h; return false; }
      }
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
'@
$h = [Cap]::Find([uint32]$ProcessId, $TitleLike)
if ($h -eq [IntPtr]::Zero) { Write-Error "window not found"; exit 1 }
$r = New-Object Cap+RECT; [void][Cap]::GetWindowRect($h, [ref]$r)
$w = $r.R - $r.L; $hh = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc(); [void][Cap]::PrintWindow($h, $hdc, 2); $g.ReleaseHdc($hdc); $g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
"saved $Out ($w x $hh)"
