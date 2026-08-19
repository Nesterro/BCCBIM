using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace BCCPlugIn
{
    public partial class CleanerWindow : Window
    {
        private readonly Document _doc;

        public CleanerWindow(Document doc)
        {
            InitializeComponent();
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        private void PurgeButton_Click(object sender, RoutedEventArgs e)
        {
            bool purgeRooms = UnplacedRoomsCheckBox.IsChecked ?? true;
            bool purgeViews = UnusedViewsCheckBox.IsChecked ?? true;

            int deletedCount = 0;

            using (Transaction t = new Transaction(_doc, "BIMBCC: Очистка модели"))
            {
                t.Start();

                if (purgeRooms)
                {
                    List<Room> rooms = new FilteredElementCollector(_doc)
                        .OfClass(typeof(SpatialElement))
                        .OfType<Room>()
                        .Where(r => r.Area <= 0 || r.Location == null)
                        .ToList();

                    foreach (Room r in rooms)
                    {
                        try { _doc.Delete(r.Id); deletedCount++; } catch { }
                    }
                }

                if (purgeViews)
                {
                    List<View> draftViews = new FilteredElementCollector(_doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .Where(v => v.ViewType == ViewType.DraftingView && !v.IsTemplate)
                        .ToList();

                    // Check if drafting views are placed on sheets
                    List<Viewport> vps = new FilteredElementCollector(_doc)
                        .OfClass(typeof(Viewport))
                        .Cast<Viewport>()
                        .ToList();

                    HashSet<ElementId> placedViewIds = new HashSet<ElementId>(vps.Select(vp => vp.ViewId));

                    foreach (View v in draftViews)
                    {
                        if (!placedViewIds.Contains(v.Id))
                        {
                            try { _doc.Delete(v.Id); deletedCount++; } catch { }
                        }
                    }
                }

                t.Commit();
            }

            MessageBox.Show($"Очистка завершена! Удалено {deletedCount} неиспользуемых элементов.", "Успешно", MessageBoxButton.OK, MessageBoxImage.Information);
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
