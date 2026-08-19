using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace BCCPlugIn
{
    public class WallElevationsEngine
    {
        private readonly Document _doc;

        public WallElevationsEngine(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public int CreateInteriorElevations(ICollection<ElementId> selectedIds, bool n, bool e, bool s, bool w)
        {
            List<Room> rooms;
            if (selectedIds != null && selectedIds.Count > 0)
            {
                rooms = selectedIds.Select(id => _doc.GetElement(id)).OfType<Room>().Where(r => r.Area > 0).ToList();
            }
            else
            {
                rooms = new FilteredElementCollector(_doc)
                    .OfClass(typeof(SpatialElement))
                    .OfType<Room>()
                    .Where(r => r.Area > 0)
                    .ToList();
            }

            if (rooms.Count == 0) throw new Exception("Пожалуйста, выделите помещения для построения разверток.");

            ViewFamilyType vft = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.Elevation);

            if (vft == null) throw new Exception("Не найден тип вида Развертка/Фасад.");

            int count = 0;
            using (Transaction t = new Transaction(_doc, "BIMBCC: Развертки стен"))
            {
                t.Start();
                foreach (Room r in rooms)
                {
                    LocationPoint lp = r.Location as LocationPoint;
                    if (lp == null) continue;

                    ElevationMarker marker = ElevationMarker.CreateElevationMarker(_doc, vft.Id, new XYZ(lp.Point.X, lp.Point.Y, lp.Point.Z), 100);

                    if (n) { try { marker.CreateElevation(_doc, _doc.ActiveView.Id, 0); count++; } catch { } }
                    if (e) { try { marker.CreateElevation(_doc, _doc.ActiveView.Id, 1); count++; } catch { } }
                    if (s) { try { marker.CreateElevation(_doc, _doc.ActiveView.Id, 2); count++; } catch { } }
                    if (w) { try { marker.CreateElevation(_doc, _doc.ActiveView.Id, 3); count++; } catch { } }
                }
                t.Commit();
            }

            return count;
        }
    }
}
