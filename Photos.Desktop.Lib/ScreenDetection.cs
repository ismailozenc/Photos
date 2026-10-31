using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Photos.Desktop
{
    /// <summary>Detects the connected displays (Windows only).</summary>
    public static class ScreenDetection
    {
        public static IReadOnlyList<Rectangle> GetDisplayBounds() =>
            Screen.AllScreens.Select(s => s.Bounds).ToList();
    }
}
