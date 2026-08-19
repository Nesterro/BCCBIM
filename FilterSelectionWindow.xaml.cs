using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public class FilterCategoryItem
    {
        public Category Category { get; set; }
        public string Name => Category != null ? Category.Name : "Все категории";
    }

    public partial class FilterSelectionWindow : Window
    {
        private readonly Document _doc;
        private readonly View _activeView;
        private List<FilterCategoryItem> _categories;

        public List<ElementId> MatchingElementIds { get; private set; }

        public FilterSelectionWindow(Document doc, View activeView)
        {
            InitializeComponent();
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _activeView = activeView;

            LoadCategories();
        }

        private void LoadCategories()
        {
            var cats = new FilteredElementCollector(_doc, _activeView.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.CategoryType == CategoryType.Model)
                .Select(e => e.Category)
                .GroupBy(c => c.Id)
                .Select(g => g.First())
                .OrderBy(c => c.Name)
                .Select(c => new FilterCategoryItem { Category = c })
                .ToList();

            _categories = new List<FilterCategoryItem> { new FilterCategoryItem { Category = null } };
            _categories.AddRange(cats);

            CategoryComboBox.ItemsSource = _categories;
            CategoryComboBox.DisplayMemberPath = "Name";
            CategoryComboBox.SelectedIndex = 0;
        }

        private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FilterCategoryItem selectedCat = CategoryComboBox.SelectedItem as FilterCategoryItem;
            FilteredElementCollector col = ActiveViewRadioButton.IsChecked == true
                ? new FilteredElementCollector(_doc, _activeView.Id)
                : new FilteredElementCollector(_doc);

            var query = col.WhereElementIsNotElementType().Where(elem => elem.Category != null);
            if (selectedCat != null && selectedCat.Category != null)
            {
                query = query.Where(elem => elem.Category.Id == selectedCat.Category.Id);
            }

            HashSet<string> paramNames = new HashSet<string>();
            foreach (Element elem in query.Take(100))
            {
                foreach (Parameter p in elem.Parameters)
                {
                    if (p != null && p.Definition != null) paramNames.Add(p.Definition.Name);
                }
            }

            ParameterComboBox.ItemsSource = paramNames.OrderBy(n => n).ToList();
            if (paramNames.Count > 0) ParameterComboBox.SelectedIndex = 0;
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            FilterCategoryItem selectedCat = CategoryComboBox.SelectedItem as FilterCategoryItem;
            string selectedParam = ParameterComboBox.SelectedItem as string;
            string op = (OperatorComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "=";
            string targetVal = ValueTextBox.Text.Trim();

            FilteredElementCollector col = ActiveViewRadioButton.IsChecked == true
                ? new FilteredElementCollector(_doc, _activeView.Id)
                : new FilteredElementCollector(_doc);

            var query = col.WhereElementIsNotElementType().Where(elem => elem.Category != null);
            if (selectedCat != null && selectedCat.Category != null)
            {
                query = query.Where(elem => elem.Category.Id == selectedCat.Category.Id);
            }

            MatchingElementIds = new List<ElementId>();

            foreach (Element elem in query)
            {
                if (string.IsNullOrEmpty(selectedParam))
                {
                    MatchingElementIds.Add(elem.Id);
                    continue;
                }

                Parameter p = elem.LookupParameter(selectedParam);
                if (p == null || !p.HasValue) continue;

                string valStr = p.AsValueString() ?? p.AsString() ?? p.AsDouble().ToString() ?? p.AsInteger().ToString();
                if (valStr == null) continue;

                bool isMatch = false;
                if (op.Contains("(=)")) isMatch = valStr.Equals(targetVal, StringComparison.OrdinalIgnoreCase);
                else if (op.Contains("(!=)")) isMatch = !valStr.Equals(targetVal, StringComparison.OrdinalIgnoreCase);
                else if (op.Contains("Содержит")) isMatch = valStr.IndexOf(targetVal, StringComparison.OrdinalIgnoreCase) >= 0;
                else if (op.Contains("(>)") && double.TryParse(valStr, out double v1) && double.TryParse(targetVal, out double v2)) isMatch = v1 > v2;
                else if (op.Contains("(<)") && double.TryParse(valStr, out double v3) && double.TryParse(targetVal, out double v4)) isMatch = v3 < v4;

                if (isMatch) MatchingElementIds.Add(elem.Id);
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
