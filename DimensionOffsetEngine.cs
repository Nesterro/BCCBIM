using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    public class DimensionOffsetEngine
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;

        public DimensionOffsetEngine(UIDocument uidoc)
        {
            _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
            _doc = _uidoc.Document;
        }

        public int AdjustDimensionOffsets()
        {
            View activeView = _uidoc.ActiveView;
            List<Dimension> dims = new FilteredElementCollector(_doc, activeView.Id)
                .OfClass(typeof(Dimension))
                .Cast<Dimension>()
                .ToList();

            int count = 0;
            using (Transaction t = new Transaction(_doc, "BIMBCC: Смещение размеров"))
            {
                t.Start();
                foreach (Dimension dim in dims)
                {
                    if (dim.Segments != null && dim.Segments.Size > 1)
                    {
                        int i = 0;
                        foreach (DimensionSegment seg in dim.Segments)
                        {
                            if (i % 2 == 1 && seg.TextPosition != null)
                            {
                                try
                                {
                                    seg.TextPosition = new XYZ(seg.TextPosition.X, seg.TextPosition.Y + 0.3, seg.TextPosition.Z);
                                    count++;
                                }
                                catch { }
                            }
                            i++;
                        }
                    }
                }
                t.Commit();
            }

            return count;
        }
    }
}
