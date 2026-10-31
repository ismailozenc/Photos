using System;

namespace Photos.Core
{
    /// <summary>
    /// Works out where each screen's slideshow starts in the photo list. The starts are spread
    /// evenly, so screens do not show the same photo at the same time.
    /// </summary>
    public static class SlideshowPlanner
    {
        public static int[] StartIndices(int photoCount, int screenCount)
        {
            if (screenCount <= 0)
                return Array.Empty<int>();
            if (photoCount <= 0)
                return new int[screenCount];

            var starts = new int[screenCount];
            for (int i = 0; i < screenCount; i++)
                starts[i] = (int)((long)i * photoCount / screenCount);
            return starts;
        }
    }
}
