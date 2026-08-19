using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public partial class ExcelImportWindow : Window
    {
        private readonly Document _doc;
        public string SelectedExcelPath => ExcelPathTextBox.Text;

        public ExcelImportWindow(Document doc)
        {
            InitializeComponent();
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                Filter = "Таблицы Excel (*.xlsx;*.xls)|*.xlsx;*.xls|Все файлы (*.*)|*.*",
                Title = "Выберите файл Excel для импорта в Revit"
            };

            if (dlg.ShowDialog() == true)
            {
                ExcelPathTextBox.Text = dlg.FileName;
            }
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(SelectedExcelPath) || !File.Exists(SelectedExcelPath))
            {
                MessageBox.Show("Пожалуйста, выберите существующий файл Excel.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
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
