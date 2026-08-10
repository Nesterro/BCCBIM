using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Microsoft.Win32;

namespace BCCPlugIn
{
    public class XyzPointItem
    {
        public double X { get; set; } // in mm
        public double Y { get; set; } // in mm
        public double Z { get; set; } // in mm
        public string Mark { get; set; }
    }

    public partial class PlaceByXyzWindow : Window
    {
        private readonly Document _doc;
        private List<string[]> _parsedRows = new List<string[]>();
        private Dictionary<string, List<Family>> _categoryFamiliesMap = new Dictionary<string, List<Family>>();

        public FamilySymbol SelectedSymbol { get; private set; }
        public Level SelectedLevel { get; private set; }
        public Parameter TargetParameter { get; private set; }
        public List<XyzPointItem> PointsToPlace { get; private set; } = new List<XyzPointItem>();

        public double OffsetX { get; private set; }
        public double OffsetY { get; private set; }
        public double OffsetZ { get; private set; }

        public PlaceByXyzWindow(Document doc)
        {
            InitializeComponent();
            _doc = doc;

            PopulateLevels();
            PopulateCategoriesAndFamilies();
            SeparatorComboBox.SelectedIndex = 0; // Default semicolon
        }

        private void PopulateLevels()
        {
            var levels = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            LevelComboBox.ItemsSource = levels;
            LevelComboBox.DisplayMemberPath = "Name";
            if (levels.Count > 0) LevelComboBox.SelectedIndex = 0;
        }

        private void PopulateCategoriesAndFamilies()
        {
            var families = new FilteredElementCollector(_doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.IsEditable || f.GetFamilySymbolIds().Count > 0)
                .ToList();

            _categoryFamiliesMap.Clear();

            foreach (var family in families)
            {
                if (family.FamilyCategory == null) continue;
                string catName = family.FamilyCategory.Name;

                if (!_categoryFamiliesMap.ContainsKey(catName))
                {
                    _categoryFamiliesMap[catName] = new List<Family>();
                }
                _categoryFamiliesMap[catName].Add(family);
            }

            var categoryNames = _categoryFamiliesMap.Keys.OrderBy(k => k).ToList();
            CategoryComboBox.ItemsSource = categoryNames;
            if (categoryNames.Count > 0) CategoryComboBox.SelectedIndex = 0;
        }

        private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string selectedCat = CategoryComboBox.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedCat) || !_categoryFamiliesMap.ContainsKey(selectedCat))
            {
                FamilyComboBox.ItemsSource = null;
                return;
            }

            var families = _categoryFamiliesMap[selectedCat].OrderBy(f => f.Name).ToList();
            FamilyComboBox.ItemsSource = families;
            FamilyComboBox.DisplayMemberPath = "Name";
            if (families.Count > 0) FamilyComboBox.SelectedIndex = 0;
        }

        private void FamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Family family = FamilyComboBox.SelectedItem as Family;
            if (family == null)
            {
                SymbolComboBox.ItemsSource = null;
                TargetParamComboBox.ItemsSource = null;
                return;
            }

            var symbols = new List<FamilySymbol>();
            foreach (ElementId symbolId in family.GetFamilySymbolIds())
            {
                FamilySymbol symbol = _doc.GetElement(symbolId) as FamilySymbol;
                if (symbol != null) symbols.Add(symbol);
            }

            symbols = symbols.OrderBy(s => s.Name).ToList();
            SymbolComboBox.ItemsSource = symbols;
            SymbolComboBox.DisplayMemberPath = "Name";
            if (symbols.Count > 0) SymbolComboBox.SelectedIndex = 0;

            // Populate target parameters
            if (symbols.Count > 0)
            {
                var firstSymbol = symbols[0];
                var instanceParams = firstSymbol.Parameters.Cast<Parameter>()
                    .Where(p => !p.IsReadOnly && p.StorageType == StorageType.String)
                    .Select(p => p.Definition.Name)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();

                // Add common standard instance parameters
                if (!instanceParams.Contains("ADSK_Марка")) instanceParams.Insert(0, "ADSK_Марка");
                if (!instanceParams.Contains("Марка")) instanceParams.Insert(0, "Марка");
                if (!instanceParams.Contains("Комментарии")) instanceParams.Add("Комментарии");

                TargetParamComboBox.ItemsSource = instanceParams;
                TargetParamComboBox.SelectedIndex = 0;
            }
        }

        private void BrowseFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                Filter = "Файлы координат (*.csv;*.txt;*.xyz)|*.csv;*.txt;*.xyz|Все файлы (*.*)|*.*",
                Title = "Выберите файл с координатами"
            };

            if (dlg.ShowDialog() == true)
            {
                FilePathTextBox.Text = dlg.FileName;
                ParseFile(dlg.FileName);
            }
        }

        private void SeparatorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!string.IsNullOrEmpty(FilePathTextBox.Text) && File.Exists(FilePathTextBox.Text))
            {
                ParseFile(FilePathTextBox.Text);
            }
        }

        private void HasHeaderCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(FilePathTextBox.Text) && File.Exists(FilePathTextBox.Text))
            {
                ParseFile(FilePathTextBox.Text);
            }
        }

        private void ParseFile(string path)
        {
            try
            {
                _parsedRows.Clear();
                string sepTag = (SeparatorComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? ";";
                char[] sep = sepTag.ToCharArray();

                string[] lines = File.ReadAllLines(path);
                int startIdx = HasHeaderCheckBox.IsChecked == true ? 1 : 0;

                for (int i = startIdx; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    string[] parts = line.Split(sep, StringSplitOptions.None);
                    _parsedRows.Add(parts);
                }

                PopulateColumnMapping();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при чтении файла:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PopulateColumnMapping()
        {
            if (_parsedRows.Count == 0) return;

            int maxCols = _parsedRows.Max(r => r.Length);
            List<string> colOptions = new List<string>();
            for (int i = 0; i < maxCols; i++)
            {
                string sample = _parsedRows.FirstOrDefault(r => r.Length > i)?[i] ?? "";
                colOptions.Add($"Колонка {i + 1} (н-р: {sample})");
            }

            ColXComboBox.ItemsSource = colOptions;
            ColYComboBox.ItemsSource = colOptions;
            ColZComboBox.ItemsSource = colOptions;
            ColMarkComboBox.ItemsSource = colOptions;

            // Smart defaults
            if (maxCols >= 1) ColXComboBox.SelectedIndex = 0;
            if (maxCols >= 2) ColYComboBox.SelectedIndex = 1;
            if (maxCols >= 3) ColZComboBox.SelectedIndex = 2;
            if (maxCols >= 4) ColMarkComboBox.SelectedIndex = 3;
            else if (maxCols >= 1) ColMarkComboBox.SelectedIndex = 0;
        }

        private void Place_Click(object sender, RoutedEventArgs e)
        {
            SelectedSymbol = SymbolComboBox.SelectedItem as FamilySymbol;
            if (SelectedSymbol == null)
            {
                MessageBox.Show("Пожалуйста, выберите типоразмер семейства.", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedLevel = LevelComboBox.SelectedItem as Level;
            if (SelectedLevel == null)
            {
                MessageBox.Show("Пожалуйста, выберите уровень.", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_parsedRows.Count == 0)
            {
                MessageBox.Show("Файл координат пуст или не выбран.", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int colX = ColXComboBox.SelectedIndex;
            int colY = ColYComboBox.SelectedIndex;
            int colZ = ColZComboBox.SelectedIndex;
            int colMark = ColMarkComboBox.SelectedIndex;

            if (colX < 0 || colY < 0 || colZ < 0)
            {
                MessageBox.Show("Пожалуйста, сопоставьте колонки X, Y, Z.", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            double.TryParse(OffsetXTextBox.Text, out double offX);
            double.TryParse(OffsetYTextBox.Text, out double offY);
            double.TryParse(OffsetZTextBox.Text, out double offZ);

            OffsetX = offX;
            OffsetY = offY;
            OffsetZ = offZ;

            PointsToPlace.Clear();
            foreach (var row in _parsedRows)
            {
                if (row.Length <= Math.Max(colX, Math.Max(colY, colZ))) continue;

                string strX = row[colX].Replace(',', '.').Trim();
                string strY = row[colY].Replace(',', '.').Trim();
                string strZ = row[colZ].Replace(',', '.').Trim();
                string mark = colMark >= 0 && row.Length > colMark ? row[colMark].Trim() : "";

                if (double.TryParse(strX, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double valX) &&
                    double.TryParse(strY, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double valY) &&
                    double.TryParse(strZ, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double valZ))
                {
                    PointsToPlace.Add(new XyzPointItem
                    {
                        X = valX + OffsetX,
                        Y = valY + OffsetY,
                        Z = valZ + OffsetZ,
                        Mark = mark
                    });
                }
            }

            if (PointsToPlace.Count == 0)
            {
                MessageBox.Show("Не удалось прочитать ни одной валидной точки из выбранных колонок.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
