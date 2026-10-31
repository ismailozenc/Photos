using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Photos.Desktop
{
    /// <summary>
    /// Decides what each display is for, from its shape alone:
    /// the largest display is the main library window, a portrait display lists the folders,
    /// and a landscape display shows the fullscreen preview of the open photo.
    /// A missing role is simply not shown.
    /// </summary>
    internal sealed class MonitorRoles
    {
        public Rectangle? Main { get; private set; }
        public Rectangle? Folders { get; private set; }
        public Rectangle? Preview { get; private set; }

        public bool HasSecondaryRoles => Folders.HasValue || Preview.HasValue;

        public static MonitorRoles Assign(IReadOnlyList<Rectangle> displays)
        {
            var roles = new MonitorRoles();
            if (displays == null || displays.Count == 0)
                return roles;

            int mainIndex = 0;
            for (int i = 1; i < displays.Count; i++)
                if ((long)displays[i].Width * displays[i].Height > (long)displays[mainIndex].Width * displays[mainIndex].Height)
                    mainIndex = i;
            roles.Main = displays[mainIndex];

            var rest = displays.Where((_, i) => i != mainIndex).ToList();

            int portrait = rest.FindIndex(d => d.Height > d.Width);
            if (portrait >= 0)
            {
                roles.Folders = rest[portrait];
                rest.RemoveAt(portrait);
            }

            int landscape = rest.FindIndex(d => d.Width >= d.Height);
            if (landscape >= 0)
                roles.Preview = rest[landscape];

            return roles;
        }
    }
}
