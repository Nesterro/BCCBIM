using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    public class CopyCropEngine
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;

        public CopyCropEngine(UIDocument uidoc)
        {
            _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
            _doc = _uidoc.Document;
        }

        public int CopyCropToTargetViews(ICollection<ElementId> targetViewIds)
        {
            View sourceView = _uidoc.ActiveView;
            if (sourceView == null || !sourceView.CropBoxActive) throw new Exception("Активный вид не имеет активной границы обрезки.");

            BoundingBoxXYZ sourceCrop = sourceView.CropBox;
            int count = 0;

            using (Transaction t = new Transaction(_doc, "BIMBCC: Копирование обрезки вида"))
            {
                t.Start();
                foreach (ElementId id in targetViewIds)
                {
                    View v = _doc.GetElement(id) as View;
                    if (v != null && !v.IsTemplate && v.Id != sourceView.Id)
                    {
                        try
                        {
                            v.CropBoxActive = true;
                            v.CropBoxVisible = sourceView.CropBoxVisible;
                            v.CropBox = sourceCrop;
                            count++;
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
