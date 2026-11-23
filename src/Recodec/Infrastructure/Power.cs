using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Recodec.Infrastructure;

public enum WhenDoneAction { Nothing, Sleep, Shutdown }

/// <summary>Evita que el equipo se suspenda mientras convierte y lo suspende o apaga al terminar la cola.</summary>
public static class Power
{
    private const uint EsContinuous = 0x80000000, EsSystemRequired = 0x00000001;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    [DllImport("powrprof.dll")]
    private static extern bool SetSuspendState(bool hibernate, bool force, bool disableWakeEvents);

    /// <summary>true: el equipo no se suspende por inactividad (la pantalla sí puede apagarse). Hay que llamarlo desde el mismo hilo.</summary>
    public static void KeepAwake(bool on) => SetThreadExecutionState(on ? EsContinuous | EsSystemRequired : EsContinuous);

    public static void Run(WhenDoneAction action)
    {
        switch (action)
        {
            case WhenDoneAction.Sleep:
                SetSuspendState(false, false, false);
                break;
            case WhenDoneAction.Shutdown:
                Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false });
                break;
        }
    }
}
