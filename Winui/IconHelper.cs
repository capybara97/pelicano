using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Pelicano;

/// <summary>
/// Win32 트레이 아이콘에 사용할 HICON을 로드한다.
/// </summary>
internal static class IconHelper
{
    public static IntPtr LoadApplicationIconHandle(IntPtr ownerWindowHandle, out bool ownsIconHandle)
    {
        var windowIconHandle = TryCopyWindowIcon(ownerWindowHandle);
        if (windowIconHandle != IntPtr.Zero)
        {
            ownsIconHandle = true;
            return windowIconHandle;
        }

        if (File.Exists(AppPaths.AppIcoPath))
        {
            var iconWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON);
            var iconHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSMICON);
            var iconHandle = NativeMethods.LoadImage(
                IntPtr.Zero,
                AppPaths.AppIcoPath,
                NativeMethods.IMAGE_ICON,
                iconWidth,
                iconHeight,
                NativeMethods.LR_LOADFROMFILE | NativeMethods.LR_DEFAULTSIZE);

            if (iconHandle != IntPtr.Zero)
            {
                ownsIconHandle = true;
                return iconHandle;
            }

            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"트레이 아이콘을 로드하지 못했습니다: {AppPaths.AppIcoPath}");
        }

        ownsIconHandle = false;
        return NativeMethods.LoadIcon(IntPtr.Zero, NativeMethods.IDI_APPLICATION);
    }

    private static IntPtr TryCopyWindowIcon(IntPtr ownerWindowHandle)
    {
        if (ownerWindowHandle == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var iconHandle = NativeMethods.SendMessage(
            ownerWindowHandle,
            NativeMethods.WM_GETICON,
            new IntPtr(NativeMethods.ICON_SMALL2),
            IntPtr.Zero);

        if (iconHandle == IntPtr.Zero)
        {
            iconHandle = NativeMethods.SendMessage(
                ownerWindowHandle,
                NativeMethods.WM_GETICON,
                new IntPtr(NativeMethods.ICON_SMALL),
                IntPtr.Zero);
        }

        if (iconHandle == IntPtr.Zero)
        {
            iconHandle = NativeMethods.GetClassLongPtr(ownerWindowHandle, NativeMethods.GCLP_HICONSM);
        }

        if (iconHandle == IntPtr.Zero)
        {
            iconHandle = NativeMethods.GetClassLongPtr(ownerWindowHandle, NativeMethods.GCLP_HICON);
        }

        return iconHandle == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.CopyIcon(iconHandle);
    }
}
