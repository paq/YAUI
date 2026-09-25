namespace Yaui
{
    /// <summary>
    /// Settings of the dynamic atlas, which packs small sprites into shared textures so that more images are drawn
    /// in one draw call (like UI Toolkit's dynamic atlas). Changes apply to sprites added afterwards.
    /// </summary>
    public static class YauiTextureAtlas
    {
        /// <summary>Whether small sprites are packed at all.</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>Sprites up to this size (in texels, both sides) are packed.</summary>
        public static int MaxSubTextureSize { get; set; } = 64;

        /// <summary>Size of an atlas page (a power of two). More pages are added as needed.</summary>
        public static int PageSize { get; set; } = 1024;
    }
}
