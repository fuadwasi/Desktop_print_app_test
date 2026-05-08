using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;

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
    }
}
