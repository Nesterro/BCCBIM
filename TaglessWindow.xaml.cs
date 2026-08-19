using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public class CategorySelectionItem : INotifyPropertyChanged
    {
        public ElementId CategoryId { get; set; }
        public string CategoryName { get; set; }
        public int TotalCount { get; set; }

        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class TaglessWindow : Window
    {
        private readonly List<CategorySelectionItem> _categories;

        public List<ElementId> SelectedCategoryIds => _categories.Where(c => c.IsSelected).Select(c => c.CategoryId).ToList();

        public TaglessWindow(Document doc, View activeView)
        {
            InitializeComponent();

            List<Element> visibleElements = new FilteredElementCollector(doc, activeView.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.CategoryType == CategoryType.Model && !e.IsHidden(activeView))
                .ToList();

            _categories = visibleElements
                .GroupBy(e => e.Category.Id)
                .Select(g => new CategorySelectionItem
                {
                    CategoryId = g.Key,
                    CategoryName = g.First().Category.Name,
                    TotalCount = g.Count(),
                    IsSelected = true
                })
                .OrderBy(c => c.CategoryName)
                .ToList();

            CategoriesDataGrid.ItemsSource = _categories;
        }

        private void SelectAllCategories_Click(object sender, RoutedEventArgs e)
        {
            foreach (var c in _categories) c.IsSelected = true;
        }

        private void DeselectAllCategories_Click(object sender, RoutedEventArgs e)
        {
            foreach (var c in _categories) c.IsSelected = false;
        }

        private void SelectUntaggedButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedCategoryIds.Count == 0)
            {
                MessageBox.Show("Пожалуйста, выберите хотя бы одну категорию для проверки.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
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
