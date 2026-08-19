using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public class BaseLevelEngine
    {
        private readonly Document _doc;

        public BaseLevelEngine(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public int AssignBaseLevels(ICollection<ElementId> elementIds)
        {
            List<Level> levels = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            if (levels.Count == 0) throw new Exception("В проекте не найдено ни одного Уровня.");

            List<Element> targets;
            if (elementIds != null && elementIds.Count > 0)
            {
                targets = elementIds.Select(id => _doc.GetElement(id)).Where(e => e != null && e.Category != null).ToList();
            }
            else
            {
                targets = new FilteredElementCollector(_doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null && e.Location != null)
                    .ToList();
            }

            int count = 0;
            using (Transaction t = new Transaction(_doc, "BIMBCC: Базовый уровень"))
            {
                t.Start();
                foreach (Element elem in targets)
                {
                    BoundingBoxXYZ bbox = elem.get_BoundingBox(null);
                    if (bbox == null) continue;

                    double minZ = bbox.Min.Z;
                    Level closestLevel = levels.Where(l => l.Elevation <= minZ + 0.5).LastOrDefault() ?? levels.First();

                    Parameter pLevel = elem.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM)
                                    ?? elem.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);

                    if (pLevel != null && !pLevel.IsReadOnly)
                    {
                        pLevel.Set(closestLevel.Id);
                        count++;
                    }
                }
                t.Commit();
            }

            return count;
        }
    }
}
