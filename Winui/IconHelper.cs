using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Pelicano;

/// <summary>
/// Win32 트레이 아이콘에 사용할 HICON을 로드한다.
/// </summary>
internal static class IconHelper
{
    public static IntPtr LoadApplicationIconHandle(out bool ownsIconHandle)
    {
        if (File.Exists(AppPaths.AppIcoPath))
        {
            var iconHandle = NativeMethods.LoadImage(
                IntPtr.Zero,
                AppPaths.AppIcoPath,
                NativeMethods.IMAGE_ICON,
                0,
                0,
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
}
