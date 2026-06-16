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

        /// <summary>
        /// Opens the preview window. Pass a <paramref name="profile"/> to pre-populate
        /// the margin mode and margin values from the active profile settings.
        /// </summary>
        public PrintPreviewWindow(byte[] pdfBytes, List<string> availablePrinters, string defaultPrinter,
                                  ProfileSettings? profile = null)
        {
            InitializeComponent();
            _pdfBytes = pdfBytes;
            
            try
            {
                string customIconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app_icon.ico");
                if (System.IO.File.Exists(customIconPath))
                    this.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(customIconPath));
            }
            catch { /* fallback */ }

            PrinterCombo.ItemsSource = availablePrinters;
            if (availablePrinters.Contains(defaultPrinter))
                PrinterCombo.SelectedItem = defaultPrinter;
            else if (availablePrinters.Count > 0)
                PrinterCombo.SelectedIndex = 0;

            _previewControl = new PrintPreviewControl { AutoZoom = true };
            PreviewHost.Child = _previewControl;

            // Pre-populate MarginMode ComboBox + margin fields from profile
            InitMarginModeFromProfile(profile);

            this.Loaded += PrintPreviewWindow_Loaded;
            this.Closed += PrintPreviewWindow_Closed;
        }

        // ── Initialisation ────────────────────────────────────────────────────

        private void InitMarginModeFromProfile(ProfileSettings? profile)
        {
            int modeIndex = 0;
            int mt = 0, mb = 0, ml = 0, mr = 0;

            if (profile != null)
            {
                switch (profile.MarginMode)
                {
                    case MarginMode.Narrow:
                        modeIndex = 1;
                        mt = mb = ml = mr = 50;
                        break;
                    case MarginMode.Custom:
                        modeIndex = 2;
                        mt = profile.MarginTop;
                        mb = profile.MarginBottom;
                        ml = profile.MarginLeft;
                        mr = profile.MarginRight;
                        break;
                }
            }

            MarginModeCombo.SelectedIndex = modeIndex;
            ApplyMarginModeUI(modeIndex, mt, mb, ml, mr);
        }

        private void PrintPreviewWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _pdfDocument = PdfDocument.Load(new MemoryStream(_pdfBytes));
                _isUpdating = false;
                UpdatePreview();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load preview: {ex.Message}", "Preview Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
                Close();
            }
        }

        // ── Margin Mode helpers ───────────────────────────────────────────────

        private MarginMode GetSelectedMarginMode() =>
            MarginModeCombo.SelectedIndex switch
            {
                1 => MarginMode.Narrow,
                2 => MarginMode.Custom,
                _ => MarginMode.Default,
            };

        /// <summary>
        /// Updates read-only state, hint text, and field values based on the
        /// selected mode index.  Call after changing the ComboBox selection.
        /// </summary>
        private void ApplyMarginModeUI(int modeIndex, int mt = -1, int mb = -1, int ml = -1, int mr = -1)
        {
            bool isCustom = modeIndex == 2;
            bool isNarrow = modeIndex == 1;

            // Set read-only state
            MarginTop.IsReadOnly    = !isCustom;
            MarginBottom.IsReadOnly = !isCustom;
            MarginLeft.IsReadOnly   = !isCustom;
            MarginRight.IsReadOnly  = !isCustom;

            // Set hint text
            if (MarginHintText != null)
                MarginHintText.Text = isCustom  ? "Enter values in hundredths of an inch (100 = 1\")." 
                                    : isNarrow  ? "Narrow: 50 (≈ 0.5\") applied on all sides."
                                    :             "Default: no margins — content anchored top-left.";

            // Fill auto-values when switching mode (skip if caller passed -1)
            if (mt >= 0) MarginTop.Text    = mt.ToString();
            if (mb >= 0) MarginBottom.Text = mb.ToString();
            if (ml >= 0) MarginLeft.Text   = ml.ToString();
            if (mr >= 0) MarginRight.Text  = mr.ToString();

            if (isNarrow && mt < 0) { MarginTop.Text = MarginBottom.Text = MarginLeft.Text = MarginRight.Text = "50"; }
            if (!isCustom && !isNarrow && mt < 0) { MarginTop.Text = MarginBottom.Text = MarginLeft.Text = MarginRight.Text = "0"; }
        }

        /// <summary>Resolves effective margin ints from current mode + field values.</summary>
        private (int top, int bottom, int left, int right) ResolveMargins()
        {
            switch (GetSelectedMarginMode())
            {
                case MarginMode.Narrow:
                    return (50, 50, 50, 50);

                case MarginMode.Custom:
                    int.TryParse(MarginTop.Text,    out int mt);
                    int.TryParse(MarginBottom.Text, out int mb);
                    int.TryParse(MarginLeft.Text,   out int ml);
                    int.TryParse(MarginRight.Text,  out int mr);
                    return (mt, mb, ml, mr);

                default: // Default — zero margins
                    return (0, 0, 0, 0);
            }
        }

        // ── Preview Rendering ─────────────────────────────────────────────────

        private void UpdatePreview()
        {
            if (_isUpdating || _pdfDocument == null) return;

            try
            {
                var (mt, mb, ml, mr) = ResolveMargins();

                bool isCustomScale = false;
                var scaleMode = ScaleMode.ShrinkToMargin;
                if (ScaleCombo.SelectedItem is ComboBoxItem item)
                {
                    string tagStr = item.Tag?.ToString() ?? string.Empty;
                    if (tagStr == "CutMargin")       scaleMode = ScaleMode.ActualSize;
                    else if (tagStr == "Custom")    { scaleMode = ScaleMode.Custom; isCustomScale = true; }
                }

                if (CustomScalePanel != null)
                    CustomScalePanel.Visibility = isCustomScale ? Visibility.Visible : Visibility.Collapsed;

                int customScaleVal = 100;
                if (isCustomScale && CustomScaleInput != null)
                    int.TryParse(CustomScaleInput.Text, out customScaleVal);

                string printerName = PrinterCombo.SelectedItem?.ToString() ?? string.Empty;

                var options = new AdvancedPrintOptions
                {
                    PrinterName = printerName,
                    Landscape   = RadioLandscape.IsChecked == true,
                    ScaleMode   = scaleMode,
                    CustomScalePercentage = customScaleVal,
                    MarginTop    = mt,
                    MarginBottom = mb,
                    MarginLeft   = ml,
                    MarginRight  = mr
                };

                _currentPrintDocument?.Dispose();
                _currentPrintDocument = new CustomPdfPrintDocument(_pdfDocument, options);

                if (!string.IsNullOrEmpty(printerName))
                    _currentPrintDocument.PrinterSettings.PrinterName = printerName;

                _previewControl.Document = null;
                _previewControl.Document = _currentPrintDocument;
                _previewControl.InvalidatePreview();
            }
            catch
            {
                // Silently ignore until settings are corrected
            }
        }

        // ── Event Handlers ────────────────────────────────────────────────────

        private void MarginModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdating) return;
            ApplyMarginModeUI(MarginModeCombo.SelectedIndex);
            UpdatePreview();
        }

        private void Setting_Changed(object sender, RoutedEventArgs e) => UpdatePreview();

        private void Setting_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

        private void ZoomCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_previewControl == null || ZoomCombo.SelectedItem is not ComboBoxItem item) return;

            string tag = item.Tag?.ToString() ?? "Auto";
            if (tag == "Auto")
            {
                _previewControl.AutoZoom = true;
            }
            else if (double.TryParse(tag, System.Globalization.NumberStyles.Any,
                                     System.Globalization.CultureInfo.InvariantCulture, out double zoom))
            {
                _previewControl.AutoZoom = false;
                _previewControl.Zoom = zoom;
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
                MessageBox.Show("Please select a printer.", "Validation",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var (mt, mb, ml, mr) = ResolveMargins();

            var scaleMode = ScaleMode.ShrinkToMargin;
            int customScalePercentage = 100;

            if (ScaleCombo.SelectedItem is ComboBoxItem item)
            {
                string tagStr = item.Tag?.ToString() ?? string.Empty;
                if (tagStr == "CutMargin") scaleMode = ScaleMode.ActualSize;
                else if (tagStr == "Custom")
                {
                    scaleMode = ScaleMode.Custom;
                    if (CustomScaleInput != null && int.TryParse(CustomScaleInput.Text, out int csv))
                        customScalePercentage = csv;
                }
            }

            SelectedOptions = new AdvancedPrintOptions
            {
                PrinterName           = PrinterCombo.SelectedItem.ToString() ?? string.Empty,
                Landscape             = RadioLandscape.IsChecked == true,
                ScaleMode             = scaleMode,
                CustomScalePercentage = customScalePercentage,
                MarginTop             = mt,
                MarginBottom          = mb,
                MarginLeft            = ml,
                MarginRight           = mr
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
