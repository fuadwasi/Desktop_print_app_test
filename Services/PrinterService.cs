using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows;
using System.Windows.Media;
using System.Printing;
using System.Threading;
using System.Threading.Tasks;
using PdfiumViewer;
using System.Drawing.Printing;
using PrintDesktopClient.Models;

namespace PrintDesktopClient.Services
{
    public class PrinterService
    {
        // ── Printer Discovery ─────────────────────────────────────────────────

        public List<string> GetAvailablePrinters()
        {
            try
            {
                var printServer = new PrintServer();
                return printServer.GetPrintQueues().Select(q => q.FullName).ToList();
            }
            catch
            {
                return System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>().ToList();
            }
        }

        // ── File Printing ─────────────────────────────────────────────────────

        public bool PrintFile(string filePath, string printerName, ProfileSettings? profile = null)
        {
            try
            {
                if (string.IsNullOrEmpty(printerName)) return false;

                if (filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    return PrintPdfFile(filePath, printerName, profile);
                }

                var psi = new ProcessStartInfo
                {
                    FileName    = filePath,
                    Verb        = "PrintTo",
                    Arguments   = $"\"{printerName}\"",
                    CreateNoWindow  = true,
                    WindowStyle     = ProcessWindowStyle.Hidden,
                    UseShellExecute = true
                };

                try { Process.Start(psi); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Printing failed: {ex.Message}");
                    psi.Verb      = "Print";
                    psi.Arguments = "";
                    Process.Start(psi);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Printing failed: {ex.Message}");
                return false;
            }
        }

        // ── PDF from byte[] (Cloud Job) ───────────────────────────────────────

        /// <summary>
        /// Saves <paramref name="pdfBytes"/> to a temp file, prints it silently
        /// (applying <paramref name="profile"/>'s margin mode), then optionally deletes
        /// the temp file.
        /// </summary>
        public bool PrintPdfBytes(byte[] pdfBytes, string printerName, ProfileSettings? profile = null, bool deleteTempFile = true)
        {
            var tmp = Path.Combine(Path.GetTempPath(), $"print_{Guid.NewGuid()}.pdf");
            try
            {
                File.WriteAllBytes(tmp, pdfBytes);
                return PrintPdfFile(tmp, printerName, profile);
            }
            finally
            {
                if (deleteTempFile && File.Exists(tmp))
                {
                    Thread.Sleep(500); // Ensure printing has started before deletion
                    try { 
                        File.Delete(tmp); 
                    } 
                    catch { 
                        /* best-effort */ 
                    }
                }
            }
        }

        /// <summary>
        /// Saves <paramref name="pdfBytes"/> to a temp file, prints it silently with advanced options,
        /// then optionally deletes the temp file based on <paramref name="deleteTempFile"/>.
        /// </summary>
        public bool PrintPdfBytes(byte[] pdfBytes, AdvancedPrintOptions options, bool deleteTempFile = true)
        {
            var tmp = Path.Combine(Path.GetTempPath(), $"print_{Guid.NewGuid()}.pdf");
            try
            {
                File.WriteAllBytes(tmp, pdfBytes);
                return PrintPdfFile(tmp, options);
            }
            finally
            {
                if (deleteTempFile && File.Exists(tmp))
                {
                    Thread.Sleep(500);
                    try { File.Delete(tmp); } catch { /* best-effort */ }
                }
            }
        }

        // ── PDF printing via PdfiumViewer / CustomPdfPrintDocument ─────────────

        /// <summary>
        /// Resolves a <see cref="ProfileSettings"/> margin mode into the four margin integers
        /// (in hundredths of an inch) and applies them to <paramref name="opts"/>.
        /// </summary>
        public static void ApplyProfileMargins(AdvancedPrintOptions opts, ProfileSettings? profile)
        {
            if (profile == null) return;

            const int NarrowMargin = 50; // 0.5 inch in hundredths

            switch (profile.MarginMode)
            {
                case MarginMode.Narrow:
                    opts.MarginTop    = NarrowMargin;
                    opts.MarginBottom = NarrowMargin;
                    opts.MarginLeft   = NarrowMargin;
                    opts.MarginRight  = NarrowMargin;
                    break;

                case MarginMode.Custom:
                    opts.MarginTop    = profile.MarginTop;
                    opts.MarginBottom = profile.MarginBottom;
                    opts.MarginLeft   = profile.MarginLeft;
                    opts.MarginRight  = profile.MarginRight;
                    break;

                default: // MarginMode.Default — zero margins, top-left anchor
                    opts.MarginTop    = 0;
                    opts.MarginBottom = 0;
                    opts.MarginLeft   = 0;
                    opts.MarginRight  = 0;
                    break;
            }
        }

        /// <summary>
        /// Prints a PDF file using <see cref="CustomPdfPrintDocument"/> so that the
        /// top-left-anchor fix and any explicit margin mode are always applied.
        /// </summary>
        private bool PrintPdfFile(string pdfPath, string printerName, ProfileSettings? profile = null)
        {
            var opts = new AdvancedPrintOptions { PrinterName = printerName };
            ApplyProfileMargins(opts, profile);
            return PrintPdfFile(pdfPath, opts);
        }

        private bool PrintPdfFile(string pdfPath, AdvancedPrintOptions options)
        {
            try
            {
                using (var document = PdfDocument.Load(pdfPath))
                {
                    using (var printDocument = new CustomPdfPrintDocument(document, options))
                    {
                        printDocument.PrinterSettings.PrinterName = options.PrinterName;
                        printDocument.PrintController = new StandardPrintController(); // Silent printing
                        printDocument.Print();
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PDF printing failed: {ex.Message}");
                return false;
            }
        }


        // ── Plain Text Printing (WPF FlowDocument) ────────────────────────────

        public bool PrintText(string text, string printerName)
        {
            bool success = false;

            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(printerName)) return;

                    var doc = new FlowDocument
                    {
                        PagePadding  = new Thickness(50),
                        ColumnWidth  = double.PositiveInfinity,
                        FontFamily   = new FontFamily("Segoe UI")
                    };

                    doc.Blocks.Add(new Paragraph(new Run("MQTT INCOMING MESSAGE"))
                    {
                        FontSize   = 20,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.DarkSlateBlue,
                        Margin     = new Thickness(0, 0, 0, 10)
                    });

                    doc.Blocks.Add(new Paragraph(new Run($"Received at: {DateTime.Now:F}"))
                    {
                        FontSize   = 10,
                        FontStyle  = FontStyles.Italic,
                        Foreground = Brushes.Gray,
                        Margin     = new Thickness(0, 0, 0, 20)
                    });

                    doc.Blocks.Add(new Paragraph(new Run(text))
                    {
                        FontSize   = 13,
                        LineHeight = 1.5
                    });

                    var printDialog = new PrintDialog();
                    var queue = new PrintServer().GetPrintQueue(printerName);
                    if (queue != null)
                    {
                        printDialog.PrintQueue = queue;
                        printDialog.PrintDocument(
                            ((IDocumentPaginatorSource)doc).DocumentPaginator,
                            $"MQTT_{DateTime.Now.Ticks}");
                        success = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"WPF text printing failed: {ex.Message}");
                }
            });

            return success;
        }

        // ── Wait helpers ───────────────────────────────────────────────────────
        /// <summary>
        /// Asynchronously waits for five seconds. This does not block the calling thread.
        /// Prefer this method in UI scenarios to avoid freezing the UI thread.
        /// </summary>
        public Task WaitFiveSecondsAsync(CancellationToken cancellationToken = default)
        {
            return Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }

        /// <summary>
        /// Synchronously blocks the current thread for five seconds.
        /// Use only when a blocking wait is explicitly required.
        /// </summary>
        public void WaitForBlocking(int sec)
        {
            Thread.Sleep(TimeSpan.FromSeconds(sec));
        }
    }
}
