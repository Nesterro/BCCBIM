using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace BCCPlugIn
{
    public class RoomVolumeEngine
    {
        private readonly Document _doc;

        public RoomVolumeEngine(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public int CalculateRoomVolumes(ICollection<ElementId> selectedIds)
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

            int count = 0;
            using (Transaction t = new Transaction(_doc, "BIMBCC: Расчет объема помещений"))
            {
                t.Start();
                foreach (Room r in rooms)
                {
                    double volCuFt = r.Volume;
                    double volM3 = volCuFt * 0.0283168;

                    Parameter pCustom = r.LookupParameter("BCC_Объем_Помещения") ?? r.LookupParameter("Объем помещения");
                    if (pCustom != null && !pCustom.IsReadOnly)
                    {
                        if (pCustom.StorageType == StorageType.Double) pCustom.Set(volCuFt);
                        else pCustom.Set($"{volM3:F2} м³");
                        count++;
                    }
                }
                t.Commit();
            }

            return count;
        }
    }
}
