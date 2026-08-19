using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public class WorldOrientationEngine
    {
        private readonly Document _doc;

        public WorldOrientationEngine(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public int CalculateAndWriteOrientation(ICollection<ElementId> elementIds)
        {
            List<Wall> walls;
            if (elementIds != null && elementIds.Count > 0)
            {
                walls = elementIds.Select(id => _doc.GetElement(id)).OfType<Wall>().ToList();
            }
            else
            {
                walls = new FilteredElementCollector(_doc)
                    .OfClass(typeof(Wall))
                    .Cast<Wall>()
                    .Where(w => w.WallType.Function == WallFunction.Exterior)
                    .ToList();
            }

            if (walls.Count == 0) throw new Exception("В проекте или выделении не найдено наружных стен.");

            int count = 0;
            using (Transaction t = new Transaction(_doc, "BIMBCC: Запись стороны света"))
            {
                t.Start();
                foreach (Wall w in walls)
                {
                    XYZ normal = w.Orientation;
                    if (normal == null) continue;

                    double angleRad = Math.Atan2(normal.X, normal.Y);
                    double angleDeg = angleRad * (180.0 / Math.PI);
                    if (angleDeg < 0) angleDeg += 360.0;

                    string sector = GetDirectionSector(angleDeg);

                    Parameter p = w.LookupParameter("BCC_HL_Сторона_света") ?? w.LookupParameter("Сторона света");
                    if (p != null && !p.IsReadOnly)
                    {
                        p.Set(sector);
                        count++;
                    }
                }
                t.Commit();
            }

            return count;
        }

        private string GetDirectionSector(double deg)
        {
            if (deg >= 337.5 || deg < 22.5) return "Север";
            if (deg >= 22.5 && deg < 67.5) return "Северо-Восток";
            if (deg >= 67.5 && deg < 112.5) return "Восток";
            if (deg >= 112.5 && deg < 157.5) return "Юго-Восток";
            if (deg >= 157.5 && deg < 202.5) return "Юг";
            if (deg >= 202.5 && deg < 247.5) return "Юго-Запад";
            if (deg >= 247.5 && deg < 292.5) return "Запад";
            return "Северо-Запад";
        }
    }
}
