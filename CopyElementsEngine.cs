using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public class CopyElementsEngine
    {
        private readonly Document _doc;

        public CopyElementsEngine(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public int CopyElementsToLevels(ICollection<ElementId> elementIds, List<Level> targetLevels)
        {
            if (elementIds == null || elementIds.Count == 0) throw new Exception("Пожалуйста, выделите элементы для копирования.");
            if (targetLevels == null || targetLevels.Count == 0) return 0;

            int count = 0;
            using (Transaction t = new Transaction(_doc, "BIMBCC: Копирование элементов по уровням"))
            {
                t.Start();
                foreach (Level targetLevel in targetLevels)
                {
                    double zOffset = targetLevel.Elevation;
                    XYZ translation = new XYZ(0, 0, zOffset);
                    try
                    {
                        ICollection<ElementId> newIds = ElementTransformUtils.CopyElements(_doc, elementIds, _doc, Transform.CreateTranslation(translation), new CopyPasteOptions());
                        count += newIds.Count;
                    }
                    catch { }
                }
                t.Commit();
            }

            return count;
        }
    }
}
