using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace BCCPlugIn
{
    public class RoomFloorsEngine
    {
        private readonly Document _doc;

        public RoomFloorsEngine(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public int CreateRoomFloors(ICollection<ElementId> selectedIds, FloorType floorType)
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

            if (rooms.Count == 0) throw new Exception("Пожалуйста, выделите помещаемые помещения для создания полов.");

            int count = 0;
            SpatialElementBoundaryOptions opt = new SpatialElementBoundaryOptions();

            using (Transaction t = new Transaction(_doc, "BIMBCC: Создание полов в помещениях"))
            {
                t.Start();
                foreach (Room r in rooms)
                {
                    IList<IList<BoundarySegment>> boundarySegments = r.GetBoundarySegments(opt);
                    if (boundarySegments == null || boundarySegments.Count == 0) continue;

                    CurveLoop loop = new CurveLoop();
                    foreach (BoundarySegment seg in boundarySegments[0])
                    {
                        Curve c = seg.GetCurve();
                        if (c != null) loop.Append(c);
                    }

                    if (loop.Count() > 2)
                    {
                        try
                        {
                            Level roomLevel = _doc.GetElement(r.LevelId) as Level;
                            if (roomLevel != null)
                            {
                                List<CurveLoop> loops = new List<CurveLoop> { loop };
                                Floor floor = Floor.Create(_doc, loops, floorType.Id, roomLevel.Id);
                                count++;
                            }
                        }
                        catch { }
                    }
                }
                t.Commit();
            }

            return count;
        }
    }
}
