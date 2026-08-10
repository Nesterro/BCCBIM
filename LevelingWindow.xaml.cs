using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public enum LevelingScope
    {
        ActiveView,
        SelectedElements,
        Category
    }

    public partial class LevelingWindow : Window
    {
        private readonly Document _doc;

        public LevelingScope Scope { get; private set; }
        public Category SelectedCategory { get; private set; }
        public bool AutoFindNearest => AutoFindNearestCheckBox.IsChecked == true;
        public bool RecalculateOffset => RecalculateOffsetCheckBox.IsChecked == true;

        public LevelingWindow(Document doc, bool hasSelectedElements)
        {
            InitializeComponent();
            _doc = doc;

            SelectedElementsRadioButton.IsEnabled = hasSelectedElements;
            if (!hasSelectedElements && SelectedElementsRadioButton.IsChecked == true)
            {
                ActiveViewRadioButton.IsChecked = true;
            }

            PopulateCategories();
        }

        private void PopulateCategories()
        {
            var categories = _doc.Settings.Categories
                .Cast<Category>()
                .Where(c => c.CategoryType == CategoryType.Model && c.AllowsBoundParameters)
                .OrderBy(c => c.Name)
                .ToList();

            CategoryComboBox.ItemsSource = categories;
            CategoryComboBox.DisplayMemberPath = "Name";
            if (categories.Count > 0) CategoryComboBox.SelectedIndex = 0;
        }

        private void Execute_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedElementsRadioButton.IsChecked == true)
            {
                Scope = LevelingScope.SelectedElements;
            }
            else if (CategoryRadioButton.IsChecked == true)
            {
                Scope = LevelingScope.Category;
                SelectedCategory = CategoryComboBox.SelectedItem as Category;
                if (SelectedCategory == null)
                {
                    MessageBox.Show("Пожалуйста, выберите категорию элементов.", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else
            {
                Scope = LevelingScope.ActiveView;
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
