using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System;

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

                // Set default printer temporarily or rely on default app behavior
                // Usually "Print" verb uses default printer. 
                // To print to a specific printer, we might need a library or winspool.drv
                
                var psi = new ProcessStartInfo
                {
                    FileName = filePath,
                    Verb = "PrintTo", // PrintTo allows specifying the printer as an argument in some apps
                    Arguments = $"\"{printerName}\"",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = true
                };

                // Fallback to "Print" if "PrintTo" isn't supported, but "Print" uses system default
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
    }
}
