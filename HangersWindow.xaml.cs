using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public partial class HangersWindow : Window
    {
        public FamilySymbol SelectedSymbol { get; private set; }
        public bool RunPipes => PipesCheckBox.IsChecked == true;
        public bool RunDucts => DuctsCheckBox.IsChecked == true;
        public bool RunTrays => TraysCheckBox.IsChecked == true;
        public double SpacingMm { get; private set; }

        public HangersWindow(List<FamilySymbol> hangerSymbols)
        {
            InitializeComponent();
            SetWindowIcon();

            var items = hangerSymbols.Select(s => new FamilySymbolItem(s)).OrderBy(i => i.DisplayName).ToList();
            HangerComboBox.ItemsSource = items;
            HangerComboBox.DisplayMemberPath = "DisplayName";
            if (items.Count > 0)
            {
                HangerComboBox.SelectedIndex = 0;
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
            var selectedItem = HangerComboBox.SelectedItem as FamilySymbolItem;
            if (selectedItem == null)
            {
                MessageBox.Show(this, "Пожалуйста, выберите семейство крепежа.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SelectedSymbol = selectedItem.Symbol;

            if (!RunPipes && !RunDucts && !RunTrays)
            {
                MessageBox.Show(this, "Пожалуйста, выберите хотя бы одну категорию сетей для расстановки.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!double.TryParse(SpacingTextBox.Text, out double spacing) || spacing <= 100)
            {
                MessageBox.Show(this, "Пожалуйста, введите корректный шаг расстановки (минимум 100 мм).", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SpacingMm = spacing;

            DialogResult = true;
            Close();
        }
    }

    public class FamilySymbolItem
    {
        public FamilySymbol Symbol { get; }
        public string DisplayName => $"{Symbol.Family.Name} : {Symbol.Name}";

        public FamilySymbolItem(FamilySymbol symbol)
        {
            Symbol = symbol;
        }
    }
}
