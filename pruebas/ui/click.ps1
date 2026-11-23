param([int]$ProcessId, [int]$X, [int]$Y)
# Clic en (X, Y) relativo a la esquina de la ventana principal (mismas coordenadas que las capturas de shot.ps1).
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class M {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  public struct R { public int L, T, Rr, B; }
}
"@
$h = (Get-Process -Id $ProcessId).MainWindowHandle
[void][M]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 300
$r = New-Object M+R
[void][M]::GetWindowRect($h, [ref]$r)
[void][M]::SetCursorPos($r.L + $X, $r.T + $Y)
Start-Sleep -Milliseconds 150
[M]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); [M]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero)
"clic en $($r.L + $X), $($r.T + $Y)"
