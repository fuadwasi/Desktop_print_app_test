using System;
using System.Drawing;
using System.Drawing.Printing;
using PdfiumViewer;
using PrintDesktopClient.Models;

namespace PrintDesktopClient.Services
{
    public class CustomPdfPrintDocument : PrintDocument
    {
        private readonly PdfDocument _document;
        private readonly AdvancedPrintOptions _options;
        private int _currentPage;

        public CustomPdfPrintDocument(PdfDocument document, AdvancedPrintOptions options)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        protected override void OnBeginPrint(PrintEventArgs e)
        {
            _currentPage = PrinterSettings.FromPage == 0 ? 0 : PrinterSettings.FromPage - 1;
            base.OnBeginPrint(e);
        }

        protected override void OnQueryPageSettings(QueryPageSettingsEventArgs e)
        {
            base.OnQueryPageSettings(e);
            
            // Apply selected printer settings
            e.PageSettings.Landscape = _options.Landscape;
            e.PageSettings.Margins = new Margins(
                _options.MarginLeft,
                _options.MarginRight,
                _options.MarginTop,
                _options.MarginBottom
            );
        }

        protected override void OnPrintPage(PrintPageEventArgs e)
        {
            base.OnPrintPage(e);

            if (_currentPage >= _document.PageCount)
            {
                e.HasMorePages = false;
                return;
            }

            RenderPage(e, _currentPage);
            _currentPage++;

            int pageCount = PrinterSettings.ToPage == 0
                ? _document.PageCount
                : Math.Min(PrinterSettings.ToPage, _document.PageCount);

            e.HasMorePages = _currentPage < pageCount;
        }

        private void RenderPage(PrintPageEventArgs e, int pageIndex)
        {
            if (e.Graphics == null) return;

            // Get PDF page size in points (1/72 inch)
            var sizeFPoints = _document.PageSizes[pageIndex];

            // Convert PDF page dimensions to hundredths of an inch
            double pdfWidth = sizeFPoints.Width * 100.0 / 72.0;
            double pdfHeight = sizeFPoints.Height * 100.0 / 72.0;

            // Get orientation of PDF page and printable area
            var pageOrientation = GetOrientation(pdfWidth, pdfHeight);
            var printOrientation = GetOrientation(e.MarginBounds.Width, e.MarginBounds.Height);

            // Swap if orientation mismatch
            if (pageOrientation != printOrientation)
            {
                double temp = pdfWidth;
                pdfWidth = pdfHeight;
                pdfHeight = temp;
            }

            // Printable area bounds in hundredths of an inch (margins applied)
            Rectangle marginBounds = e.MarginBounds;

            // Calculate Scale Factor
            double scale = 1.0;
            if (_options.ScaleMode == ScaleMode.ShrinkToMargin)
            {
                double scaleX = (double)marginBounds.Width / pdfWidth;
                double scaleY = (double)marginBounds.Height / pdfHeight;
                scale = Math.Min(scaleX, scaleY);
                if (scale > 1.0) scale = 1.0; // Shrink only, don't grow
            }
            else if (_options.ScaleMode == ScaleMode.Custom)
            {
                scale = _options.CustomScalePercentage / 100.0;
            }
            else // ActualSize / CutMargin
            {
                scale = 1.0;
            }

            // Calculate scaled size
            double drawWidth = pdfWidth * scale;
            double drawHeight = pdfHeight * scale;

            // Anchor PDF content to the top-left corner of the printable area.
            // Do NOT centre — centring pushes POS content into the middle of the receipt roll.
            double left = marginBounds.Left;
            double top  = marginBounds.Top;

            // Convert back to pixels based on Graphics DPI
            int pixelX = ConvertToPixels(left, e.Graphics.DpiX);
            int pixelY = ConvertToPixels(top, e.Graphics.DpiY);
            int pixelWidth = ConvertToPixels(drawWidth, e.Graphics.DpiX);
            int pixelHeight = ConvertToPixels(drawHeight, e.Graphics.DpiY);

            var destRect = new Rectangle(pixelX, pixelY, pixelWidth, pixelHeight);

            _document.Render(
                pageIndex,
                e.Graphics,
                e.Graphics.DpiX,
                e.Graphics.DpiY,
                destRect,
                PdfRenderFlags.ForPrinting | PdfRenderFlags.Annotations
            );
        }

        private static int ConvertToPixels(double hundredthsOfInch, float dpi)
        {
            return (int)Math.Round((hundredthsOfInch / 100.0) * dpi);
        }

        private static Orientation GetOrientation(double width, double height)
        {
            return (height > width) ? Orientation.Portrait : Orientation.Landscape;
        }

        private enum Orientation
        {
            Portrait,
            Landscape
        }
    }
}
