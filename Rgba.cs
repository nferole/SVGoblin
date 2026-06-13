namespace SVGoblin
{
    /// <summary>Channel access for packed 32-bit ARGB pixels.</summary>
    internal static class Rgba
    {
        /// <summary>Alpha value below which a pixel counts as transparent.</summary>
        private const int OpaqueThreshold = 128;

        public static int Alpha(uint pixel) => (int)(pixel >> 24) & 0xFF;
        public static int Red(uint pixel) => (int)(pixel >> 16) & 0xFF;
        public static int Green(uint pixel) => (int)(pixel >> 8) & 0xFF;
        public static int Blue(uint pixel) => (int)pixel & 0xFF;

        public static bool IsOpaque(uint pixel) => Alpha(pixel) >= OpaqueThreshold;
    }
}
