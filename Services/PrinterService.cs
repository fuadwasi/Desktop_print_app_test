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
using PdfiumViewer;

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

        public bool PrintFile(string filePath, string printerName)
        {
            try
            {
                if (string.IsNullOrEmpty(printerName)) return false;

                // Use the PDF silent path for PDF files
                if (Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                    return PrintPdfFile(filePath, printerName);

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
                catch
                {
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
        /// Saves <paramref name="pdfBytes"/> to a temp file, prints it silently,
        /// then deletes the temp file.
        /// </summary>
        public bool PrintPdfBytes(byte[] pdfBytes, string printerName)
        {
            var tmp = Path.Combine(Path.GetTempPath(), $"print_{Guid.NewGuid()}.pdf");
            try
            {
                File.WriteAllBytes(tmp, pdfBytes);
                return PrintPdfFile(tmp, printerName);
            }
            finally
            {
                // Privacy: always remove the temp file
                if (File.Exists(tmp))
                {
                    try { File.Delete(tmp); } catch { /* best-effort */ }
                }
            }
        }

        // ── PDF silent printing via PdfiumViewer ──────────────────────────────

        private bool PrintPdfFile(string pdfPath, string printerName)
        {
            try
            {
                using var doc       = PdfDocument.Load(pdfPath);
                using var printDoc  = doc.CreatePrintDocument();

                printDoc.PrinterSettings.PrinterName = printerName;
                // Suppress the Printing dialog
                printDoc.PrintController = new System.Drawing.Printing.StandardPrintController();
                printDoc.Print();
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
    }
}
