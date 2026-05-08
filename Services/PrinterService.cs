using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System;
using System.Drawing;

namespace PrintDesktopClient.Services
{
    public class PrinterService
    {
        public List<string> GetAvailablePrinters()
        {
            try
            {
                return PrinterSettings.InstalledPrinters.Cast<string>().ToList();
            }
            catch
            {
                return new List<string>();
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

                try {
                    Process.Start(psi);
                } catch {
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
            try
            {
                if (string.IsNullOrEmpty(printerName)) return false;

                using (var pd = new PrintDocument())
                {
                    pd.PrinterSettings.PrinterName = printerName;
                    pd.PrintPage += (sender, e) =>
                    {
                        using (var font = new Font("Arial", 12))
                        {
                            var brush = Brushes.Black;
                            var rect = e.MarginBounds;
                            
                            e.Graphics.DrawString(text, font, brush, rect);
                        }
                    };
                    pd.Print();
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Text printing failed: {ex.Message}");
                return false;
            }
        }
    }
}
