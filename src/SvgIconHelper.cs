using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using Svg;

namespace ModProfileSwitcher
{
    /// <summary>
    /// Loads SVG files from the "svgs/" subfolder next to the executable,
    /// rasterizes them at the requested size, optionally tints them,
    /// and applies the result as a button image (clearing the text).
    /// </summary>
    internal static class SvgIconHelper
    {
        private static readonly string SvgDir = Path.Combine(
            AppContext.BaseDirectory, "svgs");

        /// <summary>
        /// Load an SVG by filename (without extension), render it at <paramref name="size"/>×<paramref name="size"/>
        /// pixels, optionally re-colour all pixels to <paramref name="tint"/>, and return the bitmap.
        /// Returns null if the file does not exist or rendering fails.
        /// </summary>
        public static Bitmap Load(string name, int size = 16, Color? tint = null)
        {
            var path = Path.Combine(SvgDir, name + ".svg");
            if (!File.Exists(path)) return null;

            try
            {
                var doc = SvgDocument.Open(path);
                var bmp = doc.Draw(size, size);

                if (tint.HasValue)
                    bmp = Tint(bmp, tint.Value);

                return bmp;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Apply an SVG image to a button, clearing its text.
        /// The image is centred; a tooltip is set to the original text as a fallback.
        /// </summary>
        public static void Apply(Button button, string svgName, int size = 16,
                                  Color? tint = null, ToolTip toolTip = null)
        {
            var bmp = Load(svgName, size, tint);
            if (bmp == null) return;   // keep text if SVG not found

            if (toolTip != null && !string.IsNullOrEmpty(button.Text))
                toolTip.SetToolTip(button, button.Text);

            button.Image = bmp;
            button.Text = "";
            button.ImageAlign = ContentAlignment.MiddleCenter;
            button.Padding = new Padding(2);
        }

        /// <summary>
        /// Re-colours every non-transparent pixel of <paramref name="src"/> to <paramref name="colour"/>,
        /// preserving the original alpha channel. Used to match system button foreground colour.
        /// </summary>
        private static Bitmap Tint(Bitmap src, Color colour)
        {
            var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);

            var srcData = src.LockBits(
                new Rectangle(0, 0, src.Width, src.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var dstData = dst.LockBits(
                new Rectangle(0, 0, dst.Width, dst.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            unsafe
            {
                byte* s = (byte*)srcData.Scan0;
                byte* d = (byte*)dstData.Scan0;
                int len = srcData.Stride * srcData.Height;

                for (int i = 0; i < len; i += 4)
                {
                    byte alpha = s[i + 3];
                    d[i + 0] = colour.B;   // B
                    d[i + 1] = colour.G;   // G
                    d[i + 2] = colour.R;   // R
                    d[i + 3] = alpha;      // A
                }
            }

            src.UnlockBits(srcData);
            dst.UnlockBits(dstData);
            src.Dispose();
            return dst;
        }
    }
}
