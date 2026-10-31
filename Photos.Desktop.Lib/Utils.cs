using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Photos.Desktop
{
    internal static class Utils
    {
        public static SKPoint ToSK(this Vector2 v) => new(v.X, v.Y);
        public static SKSize ToSKSize(this Vector2i v) => new(v.X, v.Y);

    }

    [SupportedOSPlatform("windows")]
    internal static class WinUtils
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DWM_BLURBEHIND blurBehind);

        public static unsafe bool PrepareWindow(NativeWindow w)
        {
            try
            {
                var par = new DWM_BLURBEHIND() { dwFlags = DWM_BB.Enable, fEnable = true };
                var err = DwmEnableBlurBehindWindow(GLFW.GetWin32Window(w.WindowPtr), ref par);
                if (err != 0)
                {
                    Core.Utils.Trace($"blur error: {err}");
                    return false;
                }
            }
            catch (Exception e)
            {
                Core.Utils.TraceError(e);
                return false;
            }

            return true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DWM_BLURBEHIND
    {
        public DWM_BB dwFlags;
        public bool fEnable;
        public IntPtr hRgnBlur;
        public bool fTransitionOnMaximized;

      
    }

    enum DWM_BB
    {
        Enable = 1,
        BlurRegion = 2,
        TransitionMaximized = 4
    }


    ///////////////////////////////////////////////////////////////////////////////////////////////////
    
    
    

}
