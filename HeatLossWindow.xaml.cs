using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public partial class HeatLossWindow : Window
    {
        // ── Публичные свойства (читает Command) ───────────────────────────
        public FamilySymbol SelectedSymbol  { get; private set; }
        public double       TempOutside     { get; private set; }
        public double       TempInside      { get; private set; }

        public bool ProcessWalls   => WallsCheckBox.IsChecked   == true;
        public bool ProcessFloors  => FloorsCheckBox.IsChecked  == true;
        public bool ProcessDoors   => DoorsCheckBox.IsChecked   == true;
        public bool ProcessWindows => WindowsCheckBox.IsChecked == true;

        private readonly List<FamilySymbolItem> _items;

        // ── Конструктор ───────────────────────────────────────────────────
        public HeatLossWindow(List<FamilySymbol> symbols, int roomCount)
        {
            InitializeComponent();
            SetWindowIcon();

            // Инфо-строка
            InfoTextBlock.Text = $"Найдено помещений с объёмом: {roomCount}";

            // Заполнить ComboBox
            _items = symbols
                .Select(s => new FamilySymbolItem(s))
                .OrderBy(i => i.DisplayName)
                .ToList();

            FamilyComboBox.ItemsSource        = _items;
            FamilyComboBox.DisplayMemberPath  = "DisplayName";

            if (_items.Count > 0)
                FamilyComboBox.SelectedIndex = 0;
        }

        // ── Иконка окна ───────────────────────────────────────────────────
        private void SetWindowIcon()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream(
                    "BCCPlugIn.Resources.heat_loss_icon.png"))
                {
                    if (stream != null)
                    {
                        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                            stream,
                            System.Windows.Media.Imaging.BitmapCreateOptions.None,
                            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                        this.Icon = decoder.Frames[0];
                    }
                }
            }
            catch { /* Fallback — без иконки */ }
        }

        // ── Кнопки ────────────────────────────────────────────────────────
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            // Валидация: семейство
            var selectedItem = FamilyComboBox.SelectedItem as FamilySymbolItem;
            if (selectedItem == null)
            {
                MessageBox.Show(this,
                    "Пожалуйста, выберите семейство кубика.",
                    "Теплопотери", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Валидация: хотя бы одна конструкция
            if (!ProcessWalls && !ProcessFloors && !ProcessDoors && !ProcessWindows)
            {
                MessageBox.Show(this,
                    "Выберите хотя бы один тип обрабатываемых конструкций.",
                    "Теплопотери", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Валидация: температуры
            if (!double.TryParse(
                    TempOutsideTextBox.Text.Trim().Replace(',', '.'),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double tOut))
            {
                MessageBox.Show(this,
                    "Введите корректное значение температуры наружного воздуха (например: -28).",
                    "Теплопотери", MessageBoxButton.OK, MessageBoxImage.Warning);
                TempOutsideTextBox.Focus();
                return;
            }

            if (!double.TryParse(
                    TempInsideTextBox.Text.Trim().Replace(',', '.'),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double tIn))
            {
                MessageBox.Show(this,
                    "Введите корректное значение температуры внутреннего воздуха (например: 20).",
                    "Теплопотери", MessageBoxButton.OK, MessageBoxImage.Warning);
                TempInsideTextBox.Focus();
                return;
            }

            SelectedSymbol = selectedItem.Symbol;
            TempOutside    = tOut;
            TempInside     = tIn;

            DialogResult = true;
            Close();
        }
    }
}
