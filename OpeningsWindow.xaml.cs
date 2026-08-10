using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public partial class OpeningsWindow : Window
    {
        public FamilySymbol SelectedWallSymbol { get; private set; }
        public FamilySymbol SelectedFloorSymbol { get; private set; }
        public bool IntersectWalls => WallsCheckBox.IsChecked == true;
        public bool IntersectFloors => FloorsCheckBox.IsChecked == true;
        public double ClearanceMm { get; private set; }
        public string WidthParam => WidthParamTextBox.Text.Trim();
        public string HeightParam => HeightParamTextBox.Text.Trim();
        public string DepthParam => DepthParamTextBox.Text.Trim();

        public OpeningsWindow(List<FamilySymbol> gmSymbols)
        {
            InitializeComponent();
            SetWindowIcon();

            var wallItems = gmSymbols.Select(s => new FamilySymbolItem(s)).OrderBy(i => i.DisplayName).ToList();
            WallOpeningComboBox.ItemsSource = wallItems;
            WallOpeningComboBox.DisplayMemberPath = "DisplayName";
            if (wallItems.Count > 0)
            {
                WallOpeningComboBox.SelectedIndex = 0;
            }

            var floorItems = gmSymbols.Select(s => new FamilySymbolItem(s)).OrderBy(i => i.DisplayName).ToList();
            FloorOpeningComboBox.ItemsSource = floorItems;
            FloorOpeningComboBox.DisplayMemberPath = "DisplayName";
            if (floorItems.Count > 0)
            {
                FloorOpeningComboBox.SelectedIndex = 0;
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
                        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                        this.Icon = decoder.Frames[0];
                    }
                }
            }
            catch
            {
                // Fallback
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            if (IntersectWalls)
            {
                var wallItem = WallOpeningComboBox.SelectedItem as FamilySymbolItem;
                if (wallItem == null)
                {
                    MessageBox.Show(this, "Пожалуйста, выберите семейство отверстия в стене.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                SelectedWallSymbol = wallItem.Symbol;
            }

            if (IntersectFloors)
            {
                var floorItem = FloorOpeningComboBox.SelectedItem as FamilySymbolItem;
                if (floorItem == null)
                {
                    MessageBox.Show(this, "Пожалуйста, выберите семейство отверстия в перекрытии.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                SelectedFloorSymbol = floorItem.Symbol;
            }

            if (!IntersectWalls && !IntersectFloors)
            {
                MessageBox.Show(this, "Пожалуйста, выберите хотя бы один тип конструкций для пересечения.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!double.TryParse(ClearanceTextBox.Text, out double clearance) || clearance < 0)
            {
                MessageBox.Show(this, "Пожалуйста, введите корректный зазор на сторону (минимум 0 мм).", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ClearanceMm = clearance;

            if (string.IsNullOrWhiteSpace(WidthParam) || string.IsNullOrWhiteSpace(HeightParam) || string.IsNullOrWhiteSpace(DepthParam))
            {
                MessageBox.Show(this, "Пожалуйста, заполните имена параметров.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }
    }
}
