using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public class LinkItem : INotifyPropertyChanged
    {
        public RevitLinkType LinkType { get; set; }
        public string Name => LinkType != null ? LinkType.Name : "Связь";
        public string Status => LinkType != null && RevitLinkType.IsLoaded(LinkType.Document, LinkType.Id) ? "Загружена" : "Выгружена";

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

    public partial class LinksManagerWindow : Window
    {
        private readonly Document _doc;
        private List<LinkItem> _linkList;

        public LinksManagerWindow(Document doc)
        {
            InitializeComponent();
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));

            LoadLinks();
        }

        private void LoadLinks()
        {
            List<RevitLinkType> links = new FilteredElementCollector(_doc)
                .OfClass(typeof(RevitLinkType))
                .Cast<RevitLinkType>()
                .ToList();

            _linkList = links.Select(l => new LinkItem { LinkType = l, IsSelected = false }).ToList();
            LinksDataGrid.ItemsSource = _linkList;
        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = _linkList.Where(l => l.IsSelected).ToList();
            foreach (var item in selected)
            {
                try { item.LinkType.Reload(); } catch { }
            }
            LoadLinks();
        }

        private void UnloadButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = _linkList.Where(l => l.IsSelected).ToList();
            foreach (var item in selected)
            {
                try { item.LinkType.Unload(null); } catch { }
            }
            LoadLinks();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
