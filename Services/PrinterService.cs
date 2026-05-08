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

namespace PrintDesktopClient.Services
{
    public class PrinterService
    {
        public List<string> GetAvailablePrinters()
        {
            try
            {
                var printServer = new PrintServer();
                return printServer.GetPrintQueues().Select(q => q.FullName).ToList();
            }
            catch
            {
                // Fallback to older method if PrintServer fails
                return System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>().ToList();
            }
        }

        public bool PrintFile(string filePath, string printerName)
        {
            try
            {
                if (string.IsNullOrEmpty(printerName)) return false;

                var psi = new ProcessStartInfo
                {
                    FileName = filePath,
                    Verb = "PrintTo",
                    Arguments = $"\"{printerName}\"",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = true
                };

                try 
                {
                    Process.Start(psi);
                } 
                catch 
                {
                    psi.Verb = "Print";
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

        public bool PrintText(string text, string printerName)
        {
            // We use the WPF Dispatcher to ensure the PrintDialog and FlowDocument 
            // are created on a UI-compatible thread if necessary, though usually 
            // this is called from the UI or a background thread handled by the ViewModel.
            bool success = false;
            
            // Use Application Dispatcher to ensure we are on the right thread for WPF UI objects
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(printerName)) return;

                    // 1. Create a FlowDocument (Our "Virtual Document")
                    var doc = new FlowDocument();
                    doc.PagePadding = new Thickness(50);
                    doc.ColumnWidth = double.PositiveInfinity; // Prevent multi-column layout
                    doc.FontFamily = new FontFamily("Segoe UI");

                    // Add Header
                    var header = new Paragraph(new Run("MQTT INCOMING MESSAGE"))
                    {
                        FontSize = 20,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.DarkSlateBlue,
                        Margin = new Thickness(0, 0, 0, 10)
                    };
                    doc.Blocks.Add(header);

                    // Add Timestamp
                    var meta = new Paragraph(new Run($"Received at: {DateTime.Now:F}"))
                    {
                        FontSize = 10,
                        FontStyle = FontStyles.Italic,
                        Foreground = Brushes.Gray,
                        Margin = new Thickness(0, 0, 0, 20)
                    };
                    doc.Blocks.Add(meta);

                    // Add Message Content
                    var content = new Paragraph(new Run(text))
                    {
                        FontSize = 13,
                        LineHeight = 1.5
                    };
                    doc.Blocks.Add(content);

                    // 2. Setup PrintDialog for Silent Printing
                    var printDialog = new PrintDialog();
                    
                    // Look up the specific printer queue
                    var printServer = new PrintServer();
                    var queue = printServer.GetPrintQueue(printerName);
                    
                    if (queue != null)
                    {
                        printDialog.PrintQueue = queue;
                        
                        // 3. Print the document silently
                        var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
                        printDialog.PrintDocument(paginator, $"MQTT_Print_{DateTime.Now.Ticks}");
                        success = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"WPF Printing failed: {ex.Message}");
                    success = false;
                }
            });

            return success;
        }
    }
}
