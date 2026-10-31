namespace Photos.Core
{
    /// <summary>
    /// The thumbnail sizes stored for every photo (in pixels, the shorter side).
    /// The grid draws from the smallest stored size that is large enough for the tile.
    /// </summary>
    public static class ThumbnailSizes
    {
        public const int Mini = 160;
        public const int Small = 256;
        public const int Medium = 352;
        public const int Large = 536;

        public static readonly int[] Sizes = { Mini, Small, Medium, Large };
        public static readonly string[] Names = { "Mini", "Small", "Medium", "Large" };

        /// <summary>Index of the smallest stored size that is at least <paramref name="drawSize"/>, or the largest.</summary>
        public static int SlotFor(int drawSize)
        {
            for (int i = 0; i < Sizes.Length; i++)
                if (Sizes[i] >= drawSize)
                    return i;
            return Sizes.Length - 1;
        }
    }
}
