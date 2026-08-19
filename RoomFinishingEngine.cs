using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace BCCPlugIn
{
    public class RoomFinishingEngine
    {
        private readonly Document _doc;

        public RoomFinishingEngine(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public int CreateRoomWallFinishing(ICollection<ElementId> selectedIds, WallType wallType, double heightMm)
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

            if (rooms.Count == 0) throw new Exception("Пожалуйста, выделите помещаемые помещения для создания отделки.");

            double heightFt = heightMm / 304.8;
            int count = 0;

            SpatialElementBoundaryOptions opt = new SpatialElementBoundaryOptions();

            using (Transaction t = new Transaction(_doc, "BIMBCC: Создание отделки стен"))
            {
                t.Start();
                foreach (Room r in rooms)
                {
                    IList<IList<BoundarySegment>> boundarySegments = r.GetBoundarySegments(opt);
                    if (boundarySegments == null) continue;

                    foreach (IList<BoundarySegment> loop in boundarySegments)
                    {
                        foreach (BoundarySegment seg in loop)
                        {
                            Curve curve = seg.GetCurve();
                            if (curve == null) continue;

                            try
                            {
                                Wall wall = Wall.Create(_doc, curve, wallType.Id, r.LevelId, heightFt, 0, false, false);
                                count++;
                            }
                            catch { }
                        }
                    }
                }
                t.Commit();
            }

            return count;
        }
    }
}
