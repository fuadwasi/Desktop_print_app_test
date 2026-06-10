using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Drawing.Printing;
using System.Windows.Forms;
using PdfiumViewer;
using PrintDesktopClient.Models;
using PrintDesktopClient.Services;
using MessageBox = System.Windows.MessageBox;

namespace PrintDesktopClient
{
    public partial class PrintPreviewWindow : Window
    {
        private readonly byte[] _pdfBytes;
        private PdfDocument? _pdfDocument;
        private PrintPreviewControl _previewControl;
        private PrintDocument? _currentPrintDocument;
        
        // Flag to prevent updates while initializing
        private bool _isUpdating = true;

        public AdvancedPrintOptions SelectedOptions { get; private set; } = new();

        public PrintPreviewWindow(byte[] pdfBytes, List<string> availablePrinters, string defaultPrinter)
        {
            InitializeComponent();
            _pdfBytes = pdfBytes;
            
            try
            {
                string customIconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app_icon.ico");
                if (System.IO.File.Exists(customIconPath))
                {
                    this.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(customIconPath));
                }
            }
            catch { /* fallback */ }

            PrinterCombo.ItemsSource = availablePrinters;
            if (availablePrinters.Contains(defaultPrinter))
                PrinterCombo.SelectedItem = defaultPrinter;
            else if (availablePrinters.Count > 0)
                PrinterCombo.SelectedIndex = 0;

            _previewControl = new PrintPreviewControl();
            _previewControl.AutoZoom = true;
            PreviewHost.Child = _previewControl;

            this.Loaded += PrintPreviewWindow_Loaded;
            this.Closed += PrintPreviewWindow_Closed;
        }

        private void PrintPreviewWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var stream = new MemoryStream(_pdfBytes);
                _pdfDocument = PdfDocument.Load(stream);
                
                _isUpdating = false;
                UpdatePreview();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load preview: {ex.Message}", "Preview Error", MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
                Close();
            }
        }

        private void UpdatePreview()
        {
            if (_isUpdating || _pdfDocument == null) return;

            try
            {
                // Parse UI settings
                int.TryParse(MarginTop.Text, out int mt);
                int.TryParse(MarginBottom.Text, out int mb);
                int.TryParse(MarginLeft.Text, out int ml);
                int.TryParse(MarginRight.Text, out int mr);

                bool isCustomScale = false;
                var scaleMode = ScaleMode.ShrinkToMargin;
                if (ScaleCombo.SelectedItem is ComboBoxItem item)
                {
                    string tagStr = item.Tag?.ToString() ?? string.Empty;
                    if (tagStr == "CutMargin")
                    {
                        scaleMode = ScaleMode.ActualSize;
                    }
                    else if (tagStr == "Custom")
                    {
                        scaleMode = ScaleMode.Custom;
                        isCustomScale = true;
                    }
                }

                if (CustomScalePanel != null)
                {
                    CustomScalePanel.Visibility = isCustomScale ? Visibility.Visible : Visibility.Collapsed;
                }

                int customScaleVal = 100;
                if (isCustomScale && CustomScaleInput != null)
                {
                    if (int.TryParse(CustomScaleInput.Text, out int parsedVal))
                    {
                        customScaleVal = parsedVal;
                    }
                }

                string printerName = PrinterCombo.SelectedItem?.ToString() ?? string.Empty;
                bool landscape = RadioLandscape.IsChecked == true;

                var options = new AdvancedPrintOptions
                {
                    PrinterName = printerName,
                    Landscape = landscape,
                    ScaleMode = scaleMode,
                    CustomScalePercentage = customScaleVal,
                    MarginTop = mt,
                    MarginBottom = mb,
                    MarginLeft = ml,
                    MarginRight = mr
                };

                // Dispose old document
                _currentPrintDocument?.Dispose();

                // Create new PrintDocument with custom settings
                _currentPrintDocument = new CustomPdfPrintDocument(_pdfDocument, options);
                
                if (!string.IsNullOrEmpty(printerName))
                {
                    _currentPrintDocument.PrinterSettings.PrinterName = printerName;
                }

                // Assign to preview control (assign null first to force full repaint)
                _previewControl.Document = null;
                _previewControl.Document = _currentPrintDocument;
                
                // Keep the preview rendering without actually popping up print dialogs if something goes wrong
                _previewControl.InvalidatePreview();
            }
            catch (Exception ex)
            {
                // If invalid printer or settings, just ignore silently until corrected
            }
        }

        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            UpdatePreview();
        }

        private void Setting_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void ZoomCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_previewControl == null || ZoomCombo.SelectedItem is not ComboBoxItem item) return;

            string tag = item.Tag?.ToString() ?? "Auto";
            if (tag == "Auto")
            {
                _previewControl.AutoZoom = true;
            }
            else if (double.TryParse(tag, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double zoomLevel))
            {
                _previewControl.AutoZoom = false;
                _previewControl.Zoom = zoomLevel;
            }
        }

        private void PrintPreviewWindow_Closed(object sender, EventArgs e)
        {
            _currentPrintDocument?.Dispose();
            _pdfDocument?.Dispose();
            _previewControl?.Dispose();
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            if (PrinterCombo.SelectedItem == null)
            {
                MessageBox.Show("Please select a printer.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int.TryParse(MarginTop.Text, out int mt);
            int.TryParse(MarginBottom.Text, out int mb);
            int.TryParse(MarginLeft.Text, out int ml);
            int.TryParse(MarginRight.Text, out int mr);

            var scaleMode = ScaleMode.ShrinkToMargin;
            int customScalePercentage = 100;

            if (ScaleCombo.SelectedItem is ComboBoxItem item)
            {
                string tagStr = item.Tag?.ToString() ?? string.Empty;
                if (tagStr == "CutMargin")
                {
                    scaleMode = ScaleMode.ActualSize;
                }
                else if (tagStr == "Custom")
                {
                    scaleMode = ScaleMode.Custom;
                    if (CustomScaleInput != null && int.TryParse(CustomScaleInput.Text, out int csv))
                    {
                        customScalePercentage = csv;
                    }
                }
            }

            SelectedOptions = new AdvancedPrintOptions
            {
                PrinterName = PrinterCombo.SelectedItem.ToString() ?? string.Empty,
                Landscape = RadioLandscape.IsChecked == true,
                ScaleMode = scaleMode,
                CustomScalePercentage = customScalePercentage,
                MarginTop = mt,
                MarginBottom = mb,
                MarginLeft = ml,
                MarginRight = mr
            };

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
