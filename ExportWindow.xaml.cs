using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public partial class ExportWindow : Window
    {
        public List<SheetItem> AllSheets { get; }
        private ICollectionView _sheetsView;

        public List<SheetSetWrapper> SheetSets { get; }

        public string SelectedFolderPath => FolderTextBox.Text;
        public bool CombineSheets => CombineCheckBox.IsChecked == true;
        public string CombinedFileName => FileNameTextBox.Text;
        public bool CropMargins => CropMarginsCheckBox.IsChecked == true;
        public bool FixQuotes => FixQuotesCheckBox.IsChecked == true;
        public string SelectedPrinterName => PrinterComboBox.SelectedItem as string;

        public ExportPaperFormat SelectedPaperFormat
        {
            get
            {
                switch (PaperFormatComboBox.SelectedIndex)
                {
                    case 1: return ExportPaperFormat.ISO_A0;
                    case 2: return ExportPaperFormat.ISO_A1;
                    case 3: return ExportPaperFormat.ISO_A2;
                    case 4: return ExportPaperFormat.ISO_A3;
                    case 5: return ExportPaperFormat.ISO_A4;
                    default: return ExportPaperFormat.Default;
                }
            }
        }

        public PageOrientationType SelectedPageOrientation
        {
            get
            {
                switch (PageOrientationComboBox.SelectedIndex)
                {
                    case 1: return PageOrientationType.Portrait;
                    case 2: return PageOrientationType.Landscape;
                    default: return PageOrientationType.Auto;
                }
            }
        }

        public ColorDepthType SelectedColorDepth
        {
            get
            {
                switch (ColorDepthComboBox.SelectedIndex)
                {
                    case 1: return ColorDepthType.GrayScale;
                    case 2: return ColorDepthType.BlackLine;
                    default: return ColorDepthType.Color;
                }
            }
        }

        public ZoomType SelectedZoomType
        {
            get
            {
                switch (ZoomTypeComboBox.SelectedIndex)
                {
                    case 1: return ZoomType.Zoom;
                    default: return ZoomType.FitToPage;
                }
            }
        }

        public bool AlwaysUseRaster
        {
            get => ProcessingTypeComboBox.SelectedIndex == 1;
        }

        public ExportWindow(List<ViewSheet> sheets, List<ViewSheetSet> sheetSets, string defaultFolder)
        {
            InitializeComponent();
            SetWindowIcon();

            AllSheets = sheets.Select(s => new SheetItem(s)).OrderBy(s => s.Number).ToList();
            SheetsListBox.ItemsSource = AllSheets;

            _sheetsView = CollectionViewSource.GetDefaultView(AllSheets);
            _sheetsView.Filter = FilterSheets;

            FolderTextBox.Text = defaultFolder;

            // Populate system printers
            PopulatePrinters();

            // Configure Sheet Sets Selection list
            SheetSets = new List<SheetSetWrapper>
            {
                new SheetSetWrapper("<Выбрать набор...>", null)
            };

            if (sheetSets != null)
            {
                foreach (var set in sheetSets)
                {
                    var ids = new HashSet<ElementId>(set.Views.Cast<Autodesk.Revit.DB.View>().Select(v => v.Id));
                    SheetSets.Add(new SheetSetWrapper(set.Name, ids));
                }
            }

            SheetSetComboBox.ItemsSource = SheetSets;
            SheetSetComboBox.DisplayMemberPath = "Name";
            SheetSetComboBox.SelectedIndex = 0;

            UpdateCount();
        }

        private void PopulatePrinters()
        {
            try
            {
                List<string> options = new List<string>
                {
                    "Встроенный экспорт Revit PDF (Как в DiRoots)"
                };

                var installedPrinters = System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>().ToList();
                options.AddRange(installedPrinters);

                PrinterComboBox.ItemsSource = options;
                PrinterComboBox.SelectedIndex = 0;
            }
            catch
            {
                // Fallback
            }
        }

        private void SetWindowIcon()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("BCCPlugIn.Resources.pdf_export_icon.png"))
                {
                    if (stream != null)
                    {
                        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                        this.Icon = decoder.Frames[0];
                    }
                }
            }
            catch
            {
                // Fallback if resource load fails
            }
        }

        private bool FilterSheets(object obj)
        {
            if (string.IsNullOrWhiteSpace(SearchTextBox.Text))
                return true;

            var item = obj as SheetItem;
            if (item == null) return false;

            string filter = SearchTextBox.Text.Trim();
            return item.Number.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   item.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _sheetsView?.Refresh();
        }

        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateCount();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _sheetsView.Cast<SheetItem>())
            {
                item.IsSelected = true;
            }
            UpdateCount();
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _sheetsView.Cast<SheetItem>())
            {
                item.IsSelected = false;
            }
            UpdateCount();
        }

        private void SheetSetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedSet = SheetSetComboBox.SelectedItem as SheetSetWrapper;
            if (selectedSet == null || selectedSet.SheetIds == null)
                return;

            foreach (var item in AllSheets)
            {
                item.IsSelected = selectedSet.SheetIds.Contains(item.Sheet.Id);
            }
            UpdateCount();
        }

        private void UpdateCount()
        {
            int selectedCount = AllSheets.Count(s => s.IsSelected);
            SelectedCountText.Text = $" (выбрано: {selectedCount} из {AllSheets.Count})";
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Выберите папку для сохранения PDF файлов";
                dialog.ShowNewFolderButton = true;
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    FolderTextBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SelectedPrinterName))
            {
                MessageBox.Show(this, "Пожалуйста, выберите принтер для печати.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!AllSheets.Any(s => s.IsSelected))
            {
                MessageBox.Show(this, "Пожалуйста, выберите хотя бы один лист для экспорта.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedFolderPath))
            {
                MessageBox.Show(this, "Пожалуйста, выберите папку для сохранения.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (CombineSheets && string.IsNullOrWhiteSpace(CombinedFileName))
            {
                MessageBox.Show(this, "Пожалуйста, введите имя для объединенного PDF файла.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }
    }

    public class SheetSetWrapper
    {
        public string Name { get; }
        public HashSet<ElementId> SheetIds { get; }

        public SheetSetWrapper(string name, HashSet<ElementId> ids)
        {
            Name = name;
            SheetIds = ids;
        }
    }

    public class SheetItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public string Number { get; }
        public string Name { get; }
        public ViewSheet Sheet { get; }

        public SheetItem(ViewSheet sheet)
        {
            Sheet = sheet;
            Number = sheet.SheetNumber;
            Name = sheet.Name;
            IsSelected = false;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
