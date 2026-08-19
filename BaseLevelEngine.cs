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
            using (Transaction t = new Transaction(_doc, "BIMBCC: Расчет базового уровня"))
            {
                t.Start();
                foreach (Element elem in targets)
                {
                    BoundingBoxXYZ bbox = elem.get_BoundingBox(null);
                    if (bbox == null) continue;

                    double minZ = bbox.Min.Z;

                    // Find nearest level below element Z OR closest level
                    Level closestLevel = levels.Where(l => l.Elevation <= minZ + 0.1).LastOrDefault();
                    if (closestLevel == null)
                    {
                        closestLevel = levels.OrderBy(l => Math.Abs(l.Elevation - minZ)).FirstOrDefault();
                    }

                    if (closestLevel == null) continue;

                    bool setSuccess = false;

                    // Candidate level parameters
                    BuiltInParameter[] candidates = new BuiltInParameter[]
                    {
                        BuiltInParameter.WALL_BASE_CONSTRAINT,
                        BuiltInParameter.FAMILY_BASE_LEVEL_PARAM,
                        BuiltInParameter.FAMILY_LEVEL_PARAM,
                        BuiltInParameter.SCHEDULE_LEVEL_PARAM,
                        BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM,
                        BuiltInParameter.ROOM_LEVEL_ID,
                        BuiltInParameter.DPART_BASE_LEVEL
                    };

                    foreach (BuiltInParameter bip in candidates)
                    {
                        Parameter pLevel = elem.get_Parameter(bip);
                        if (pLevel != null && !pLevel.IsReadOnly)
                        {
                            try
                            {
                                pLevel.Set(closestLevel.Id);
                                setSuccess = true;
                                break;
                            }
                            catch { }
                        }
                    }

                    // Fallback to text lookup parameter "Базовый уровень" / "BCC_Базовый_уровень"
                    if (!setSuccess)
                    {
                        Parameter pCustom = elem.LookupParameter("BCC_Базовый_уровень") ?? elem.LookupParameter("Базовый уровень");
                        if (pCustom != null && !pCustom.IsReadOnly)
                        {
                            try
                            {
                                pCustom.Set(closestLevel.Name);
                                setSuccess = true;
                            }
                            catch { }
                        }
                    }

                    if (setSuccess) count++;
                }
                t.Commit();
            }

            return count;
        }
    }
}
