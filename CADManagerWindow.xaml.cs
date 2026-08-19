using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public class CADItem : INotifyPropertyChanged
    {
        public ElementId Id { get; set; }
        public string Name { get; set; }
        public string ImportType { get; set; }
        public long ElementIdValue => Id.Value;

        private bool _isSelected = false;
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

    public partial class CADManagerWindow : Window
    {
        private readonly Document _doc;
        private List<CADItem> _cadList;

        public CADManagerWindow(Document doc)
        {
            InitializeComponent();
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));

            LoadCADFiles();
        }

        private void LoadCADFiles()
        {
            List<ImportInstance> imports = new FilteredElementCollector(_doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .ToList();

            _cadList = new List<CADItem>();
            foreach (ImportInstance imp in imports)
            {
                _cadList.Add(new CADItem
                {
                    Id = imp.Id,
                    Name = imp.Category != null ? imp.Category.Name : "CAD подложка",
                    ImportType = imp.IsLinked ? "Связанный DWG" : "Внедренный DWG",
                    IsSelected = false
                });
            }

            CADDataGrid.ItemsSource = _cadList;
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var toDelete = _cadList.Where(c => c.IsSelected).ToList();
            if (toDelete.Count == 0)
            {
                MessageBox.Show("Пожалуйста, выберите хотя бы один CAD чертеж для удаления.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int count = 0;
            using (Transaction t = new Transaction(_doc, "BIMBCC: Удаление CAD чертежей"))
            {
                t.Start();
                foreach (var item in toDelete)
                {
                    try { _doc.Delete(item.Id); count++; } catch { }
                }
                t.Commit();
            }

            MessageBox.Show($"Успешно удалено {count} CAD подложек.", "Успешно", MessageBoxButton.OK, MessageBoxImage.Information);
            LoadCADFiles();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
