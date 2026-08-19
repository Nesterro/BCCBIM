using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public partial class ColorizerWindow : Window
    {
        private readonly Document _doc;
        private readonly View _activeView;

        public string SelectedParameterName { get; private set; }
        public bool IsResetRequested { get; private set; }

        public ColorizerWindow(Document doc, View activeView)
        {
            InitializeComponent();
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _activeView = activeView;

            LoadParameters();
        }

        private void LoadParameters()
        {
            var elements = new FilteredElementCollector(_doc, _activeView.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.CategoryType == CategoryType.Model)
                .Take(100)
                .ToList();

            HashSet<string> paramNames = new HashSet<string>();
            foreach (Element elem in elements)
            {
                foreach (Parameter p in elem.Parameters)
                {
                    if (p != null && p.Definition != null) paramNames.Add(p.Definition.Name);
                }
            }

            ParameterComboBox.ItemsSource = paramNames.OrderBy(n => n).ToList();
            if (paramNames.Count > 0) ParameterComboBox.SelectedIndex = 0;
        }

        private void ColorizeButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedParameterName = ParameterComboBox.SelectedItem as string;
            if (string.IsNullOrEmpty(SelectedParameterName))
            {
                MessageBox.Show("Пожалуйста, выберите параметр.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
            Close();
        }

        private void ResetGraphicsButton_Click(object sender, RoutedEventArgs e)
        {
            IsResetRequested = true;
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
