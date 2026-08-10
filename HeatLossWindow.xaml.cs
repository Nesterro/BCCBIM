using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;

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
        public bool DeleteExistingCubes { get; private set; } = true;

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
                // Try to find default cube symbol containing "Кубик" or "Маркер"
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

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            // Validate symbol selection
            var symbolItem = CubeSymbolComboBox.SelectedItem as SymbolDisplayItem;
            if (symbolItem == null || symbolItem.Symbol == null)
            {
                MessageBox.Show("Пожалуйста, выберите типоразмер кубика-маркера из категории Обобощенные модели.",
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

            LinkedParamName = LinkedParamTextBox.Text?.Trim();
            TargetDesignationParamName = TargetDesigParamTextBox.Text?.Trim();
            TargetAreaParamName = TargetAreaParamTextBox.Text?.Trim();
            DeleteExistingCubes = DeleteExistingCheckBox.IsChecked == true;

            if (string.IsNullOrEmpty(LinkedParamName))
            {
                MessageBox.Show("Укажите название параметра в связанной модели.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
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
