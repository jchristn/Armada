namespace Armada.Publisher.Build
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;

    /// <summary>
    /// Generates a macOS .icns from a single square PNG with the built-in sips and iconutil tools.
    /// </summary>
    public static class IcnsBuilder
    {
        #region Private-Members

        private static readonly int[] _BaseSizes = new int[] { 16, 32, 128, 256, 512 };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Render every iconset size (1x and 2x) from the source PNG and compile them into an .icns.
        /// </summary>
        /// <param name="sourcePng">Square source image, ideally 1024x1024.</param>
        /// <param name="outputIcns">Destination .icns path.</param>
        /// <param name="workDirectory">Scratch directory for the intermediate .iconset.</param>
        public static void Build(string sourcePng, string outputIcns, string workDirectory)
        {
            if (string.IsNullOrEmpty(sourcePng)) throw new ArgumentNullException(nameof(sourcePng));
            if (string.IsNullOrEmpty(outputIcns)) throw new ArgumentNullException(nameof(outputIcns));
            if (string.IsNullOrEmpty(workDirectory)) throw new ArgumentNullException(nameof(workDirectory));
            if (!File.Exists(sourcePng)) throw new FileNotFoundException("Icon source PNG not found.", sourcePng);

            string iconset = Path.Combine(workDirectory, "AppIcon.iconset");
            if (Directory.Exists(iconset)) Directory.Delete(iconset, true);
            Directory.CreateDirectory(iconset);

            foreach (int size in _BaseSizes)
            {
                Render(sourcePng, size, Path.Combine(iconset, "icon_" + Dim(size) + ".png"));
                Render(sourcePng, size * 2, Path.Combine(iconset, "icon_" + Dim(size) + "@2x.png"));
            }

            string? parent = Path.GetDirectoryName(outputIcns);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            ProcessRunner.Run("iconutil", new List<string> { "--convert", "icns", "--output", outputIcns, iconset });
            Directory.Delete(iconset, true);
        }

        #endregion

        #region Private-Methods

        private static string Dim(int size)
        {
            string text = size.ToString(CultureInfo.InvariantCulture);
            return text + "x" + text;
        }

        private static void Render(string sourcePng, int pixels, string destination)
        {
            string text = pixels.ToString(CultureInfo.InvariantCulture);
            ProcessRunner.Capture("sips", new List<string> { "-z", text, text, sourcePng, "--out", destination });
        }

        #endregion
    }
}
