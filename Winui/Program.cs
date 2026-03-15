using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Pelicano;

internal static class Program
{
    private const string InstanceKey = "Pelicano.Main";
    private static IntPtr _redirectEventHandle = IntPtr.Zero;

    internal static event EventHandler? RedirectedActivation;

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (DecideRedirection())
        {
            return 0;
        }

        Application.Start(callbackParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = callbackParams;
            _ = new App();
        });

        return 0;
    }

    private static bool DecideRedirection()
    {
        var activationArguments = AppInstance.GetCurrent().GetActivatedEventArgs();
        var keyInstance = AppInstance.FindOrRegisterForKey(InstanceKey);

        if (keyInstance.IsCurrent)
        {
            keyInstance.Activated += HandleActivated;
            return false;
        }

        RedirectActivationTo(activationArguments, keyInstance);
        return true;
    }

    private static void HandleActivated(object? sender, AppActivationArguments args)
    {
        RedirectedActivation?.Invoke(null, EventArgs.Empty);
    }

    private static void RedirectActivationTo(
        AppActivationArguments activationArguments,
        AppInstance keyInstance)
    {
        _redirectEventHandle = CreateEvent(IntPtr.Zero, true, false, null);

        Task.Run(() =>
        {
            keyInstance.RedirectActivationToAsync(activationArguments).AsTask().Wait();
            SetEvent(_redirectEventHandle);
        });

        const uint CwmoDefault = 0;
        const uint Infinite = 0xFFFFFFFF;
        try
        {
            _ = CoWaitForMultipleObjects(
                CwmoDefault,
                Infinite,
                1,
                [_redirectEventHandle],
                out _);

            var process = Process.GetProcessById((int)keyInstance.ProcessId);
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(process.MainWindowHandle);
            }
        }
        catch
        {
        }
        finally
        {
            if (_redirectEventHandle != IntPtr.Zero)
            {
                CloseHandle(_redirectEventHandle);
                _redirectEventHandle = IntPtr.Zero;
            }
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEvent(
        IntPtr lpEventAttributes,
        bool bManualReset,
        bool bInitialState,
        string? lpName);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetEvent(IntPtr hEvent);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(
        uint dwFlags,
        uint dwMilliseconds,
        ulong nHandles,
        IntPtr[] pHandles,
        out uint dwIndex);
}
