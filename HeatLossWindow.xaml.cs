using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Microsoft.Win32;

namespace BCCPlugIn
{
    public partial class HeatLossWindow : Window
    {
        private readonly Document _doc;
        private readonly List<ElementId> _selectedSpaceIds;

        public string ScopeMode { get; private set; } = "All";
        public ElementId SelectedLevelId { get; private set; }
        public RevitLinkInstance SelectedLinkInstance { get; private set; }
        public FamilySymbol SelectedCubeSymbol { get; private set; }
        public string LinkedParamName { get; private set; } = "ADSK_Обозначение";
        public string TargetDesignationParamName { get; private set; } = "ADSK_Обозначение";
        public string TargetAreaParamName { get; private set; } = "ADSK_Площадь";
        public double OutdoorTemp { get; private set; } = -23.0;
        public bool DeleteExistingCubes { get; private set; } = true;
        public bool CreateSchedule { get; private set; } = true;
        public bool ExportCsv { get; private set; } = true;
        public string CsvExportPath { get; private set; }

        public HeatLossWindow(Document doc, List<ElementId> selectedSpaceIds = null)
        {
            InitializeComponent();
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _selectedSpaceIds = selectedSpaceIds ?? new List<ElementId>();

            InitializeData();
        }

        private void InitializeData()
        {
            HeatLossEngine engine = new HeatLossEngine(_doc);

            // Populate Levels
            var levels = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            LevelComboBox.ItemsSource = levels;
            LevelComboBox.DisplayMemberPath = "Name";
            if (levels.Count > 0) LevelComboBox.SelectedIndex = 0;

            // Selection radio state
            if (_selectedSpaceIds.Count > 0)
            {
                ScopeSelectionRadio.IsChecked = true;
                ScopeSelectionRadio.Content = $"Текущее выделение ({_selectedSpaceIds.Count} пространств)";
            }
            else
            {
                ScopeSelectionRadio.IsEnabled = false;
                ScopeSelectionRadio.Content = "Текущее выделение (0 пространств)";
            }

            // Populate Revit Link Instances
            var links = engine.GetRevitLinkInstances();
            var linkItems = new List<LinkDisplayItem>
            {
                new LinkDisplayItem { Name = "Все связанные модели", LinkInstance = null }
            };
            foreach (var link in links)
            {
                linkItems.Add(new LinkDisplayItem { Name = link.Name, LinkInstance = link });
            }
            LinkComboBox.ItemsSource = linkItems;
            LinkComboBox.DisplayMemberPath = "Name";
            LinkComboBox.SelectedIndex = 0;

            // Populate Cube Symbols
            var symbols = engine.GetAvailableCubeSymbols();
            var symbolDisplayItems = symbols.Select(s => new SymbolDisplayItem
            {
                Name = $"{s.FamilyName} : {s.Name}",
                Symbol = s
            }).ToList();

            CubeSymbolComboBox.ItemsSource = symbolDisplayItems;
            CubeSymbolComboBox.DisplayMemberPath = "Name";

            if (symbolDisplayItems.Count > 0)
            {
                var defaultCube = symbolDisplayItems.FirstOrDefault(s =>
                    s.Name.IndexOf("Кубик", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    s.Name.IndexOf("Маркер", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    s.Name.IndexOf("Cube", StringComparison.OrdinalIgnoreCase) >= 0);

                if (defaultCube != null)
                {
                    CubeSymbolComboBox.SelectedItem = defaultCube;
                }
                else
                {
                    CubeSymbolComboBox.SelectedIndex = 0;
                }
            }

            // Set default CSV export path
            string defaultFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (!string.IsNullOrEmpty(_doc.PathName))
            {
                try
                {
                    defaultFolder = Path.GetDirectoryName(_doc.PathName);
                }
                catch { }
            }
            CsvPathTextBox.Text = Path.Combine(defaultFolder, "Теплопотери_Ведомость.csv");
        }

        private void ScopeRadio_Changed(object sender, RoutedEventArgs e)
        {
            if (LevelComboBox == null) return;

            if (ScopeLevelRadio.IsChecked == true)
            {
                LevelComboBox.IsEnabled = true;
                ScopeMode = "Level";
            }
            else
            {
                LevelComboBox.IsEnabled = false;
                if (ScopeSelectionRadio.IsChecked == true)
                {
                    ScopeMode = "Selected";
                }
                else
                {
                    ScopeMode = "All";
                }
            }
        }

        private void ExportCsv_Changed(object sender, RoutedEventArgs e)
        {
            if (CsvPathPanel != null)
            {
                CsvPathPanel.IsEnabled = ExportCsvCheckBox.IsChecked == true;
            }
        }

        private void BrowseCsvPath_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dlg = new SaveFileDialog
            {
                Title = "Выберите место для сохранения ведомости теплопотерь",
                Filter = "CSV Файлы (*.csv)|*.csv|Текстовые файлы (*.txt)|*.txt|Все файлы (*.*)|*.*",
                FileName = "Теплопотери_Ведомость.csv"
            };

            if (dlg.ShowDialog() == true)
            {
                CsvPathTextBox.Text = dlg.FileName;
            }
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            var symbolItem = CubeSymbolComboBox.SelectedItem as SymbolDisplayItem;
            if (symbolItem == null || symbolItem.Symbol == null)
            {
                MessageBox.Show("Пожалуйста, выберите типоразмер кубика-маркера из категории Обобщенные модели.",
                    "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedCubeSymbol = symbolItem.Symbol;

            var linkItem = LinkComboBox.SelectedItem as LinkDisplayItem;
            SelectedLinkInstance = linkItem?.LinkInstance;

            if (ScopeLevelRadio.IsChecked == true)
            {
                var level = LevelComboBox.SelectedItem as Level;
                if (level == null)
                {
                    MessageBox.Show("Пожалуйста, выберите уровень.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                SelectedLevelId = level.Id;
            }

            string outTempText = OutdoorTempTextBox.Text?.Replace(',', '.').Trim();
            if (double.TryParse(outTempText, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedTemp))
            {
                OutdoorTemp = parsedTemp;
            }
            else
            {
                MessageBox.Show("Укажите корректную температуру наружного воздуха (например, -23).", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LinkedParamName = LinkedParamTextBox.Text?.Trim();
            TargetDesignationParamName = TargetDesigParamTextBox.Text?.Trim();
            TargetAreaParamName = TargetAreaParamTextBox.Text?.Trim();
            DeleteExistingCubes = DeleteExistingCheckBox.IsChecked == true;
            CreateSchedule = CreateScheduleCheckBox.IsChecked == true;
            ExportCsv = ExportCsvCheckBox.IsChecked == true;
            CsvExportPath = CsvPathTextBox.Text?.Trim();

            if (string.IsNullOrEmpty(LinkedParamName))
            {
                MessageBox.Show("Укажите название параметра в связанной модели.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (ExportCsv && string.IsNullOrEmpty(CsvExportPath))
            {
                MessageBox.Show("Укажите путь для сохранения файла ведомости CSV.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private class LinkDisplayItem
        {
            public string Name { get; set; }
            public RevitLinkInstance LinkInstance { get; set; }
        }

        private class SymbolDisplayItem
        {
            public string Name { get; set; }
            public FamilySymbol Symbol { get; set; }
        }
    }
}
