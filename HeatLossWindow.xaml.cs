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

        public bool ProcessExteriorWalls => ExteriorWallsCheckBox?.IsChecked == true;
        public bool ProcessInteriorWalls => InteriorWallsCheckBox?.IsChecked == true;
        public bool ProcessFloors        => FloorsCheckBox?.IsChecked        == true;
        public bool ProcessDoors         => DoorsCheckBox?.IsChecked         == true;
        public bool ProcessWindows       => WindowsCheckBox?.IsChecked       == true;

        public bool OnlyActiveView => ActiveViewRadioButton?.IsChecked == true;

        public ExteriorDetectionOptions ExteriorOptions
        {
            get
            {
                var options = new ExteriorDetectionOptions();
                if (ExteriorDetectAutoRadioButton?.IsChecked == true)
                {
                    options.Mode = ExteriorDetectionMode.Auto;
                    return options;
                }

                options.Mode = ExteriorDetectionMode.ByParameter;
                options.ParameterName = string.IsNullOrWhiteSpace(ExteriorParamNameTextBox?.Text) 
                    ? "MN_Наружный" 
                    : ExteriorParamNameTextBox.Text.Trim();

                int condIndex = ExteriorParamConditionComboBox?.SelectedIndex ?? 0;
                switch (condIndex)
                {
                    case 0: options.Condition = ParameterConditionType.CheckboxChecked; break;
                    case 1: options.Condition = ParameterConditionType.CheckboxUnchecked; break;
                    case 2: options.Condition = ParameterConditionType.TextEquals; break;
                    case 3: options.Condition = ParameterConditionType.TextContains; break;
                    case 4: options.Condition = ParameterConditionType.IsNotEmpty; break;
                    default: options.Condition = ParameterConditionType.CheckboxChecked; break;
                }

                options.TargetValue = ExteriorParamValueTextBox?.Text?.Trim() ?? "1";
                options.Classification = (ExteriorClassificationComboBox?.SelectedIndex == 1)
                    ? ExteriorClassificationResult.MatchesInterior
                    : ExteriorClassificationResult.MatchesExterior;

                options.FallbackToAuto = ExteriorFallbackCheckBox?.IsChecked == true;
                return options;
            }
        }

        private readonly List<FamilySymbolItem> _items;
        private readonly int _allSpacesCount;
        private readonly int _activeViewSpacesCount;

        // ── Конструктор ───────────────────────────────────────────────────
        public HeatLossWindow(List<FamilySymbol> symbols, int allSpacesCount, int activeViewSpacesCount)
        {
            InitializeComponent();
            SetWindowIcon();

            _allSpacesCount = allSpacesCount;
            _activeViewSpacesCount = activeViewSpacesCount;

            UpdateInfoText();

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

        private void Scope_Checked(object sender, RoutedEventArgs e)
        {
            UpdateInfoText();
        }

        private void ExteriorDetect_Checked(object sender, RoutedEventArgs e)
        {
            if (ExteriorParamSettingsPanel != null && ExteriorDetectByParamRadioButton != null)
            {
                ExteriorParamSettingsPanel.Visibility = (ExteriorDetectByParamRadioButton.IsChecked == true)
                    ? System.Windows.Visibility.Visible
                    : System.Windows.Visibility.Collapsed;
            }
        }

        private void ExteriorParamCondition_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (ExteriorParamValueRow == null || ExteriorParamConditionComboBox == null) return;
            int idx = ExteriorParamConditionComboBox.SelectedIndex;
            // idx == 2 (TextEquals), idx == 3 (TextContains)
            ExteriorParamValueRow.Visibility = (idx == 2 || idx == 3) 
                ? System.Windows.Visibility.Visible 
                : System.Windows.Visibility.Collapsed;
        }

        private void UpdateInfoText()
        {
            if (InfoTextBlock == null || ActiveViewRadioButton == null) return;

            if (OnlyActiveView)
            {
                InfoTextBlock.Text = $"Найдено пространств на текущем виде: {_activeViewSpacesCount}";
            }
            else
            {
                InfoTextBlock.Text = $"Найдено пространств во всём проекте: {_allSpacesCount}";
            }
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
            if (!ProcessExteriorWalls && !ProcessInteriorWalls && !ProcessFloors && !ProcessDoors && !ProcessWindows)
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
