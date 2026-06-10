using PdfiumViewer;

namespace PrintDesktopClient.Models
{
    public enum ScaleMode
    {
        ShrinkToMargin,
        ActualSize,
        Custom
    }

    public class AdvancedPrintOptions
    {
        public string PrinterName { get; set; } = string.Empty;
        
        /// <summary>
        /// True for Landscape, False for Portrait.
        /// </summary>
        public bool Landscape { get; set; } = false;

        /// <summary>
        /// Scaling mode (e.g. ShrinkToMargin, ActualSize, Custom).
        /// </summary>
        public ScaleMode ScaleMode { get; set; } = ScaleMode.ShrinkToMargin;

        /// <summary>
        /// Custom scale percentage, defaults to 100%. Only active when ScaleMode is Custom.
        /// </summary>
        public int CustomScalePercentage { get; set; } = 100;

        // Keep PdfPrintMode for backward compatibility or direct Pdfium calls if needed
        public PdfPrintMode PrintMode { get; set; } = PdfPrintMode.ShrinkToMargin;

        // Margins in hundredths of an inch
        public int MarginTop { get; set; } = 0;
        public int MarginBottom { get; set; } = 0;
        public int MarginLeft { get; set; } = 0;
        public int MarginRight { get; set; } = 0;
    }
}
