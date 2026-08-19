using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    public class PlanDimensionsEngine
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;

        public PlanDimensionsEngine(UIDocument uidoc)
        {
            _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
            _doc = _uidoc.Document;
        }

        public int CreatePlanDimensions(bool dimWalls, bool dimOpenings, bool dimGrids)
        {
            View activeView = _uidoc.ActiveView;
            if (!(activeView is ViewPlan)) throw new Exception("Пожалуйста, перейдите на План (ViewPlan) для автоматической простановки размеров.");

            List<Wall> walls = new FilteredElementCollector(_doc, activeView.Id)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .ToList();

            if (walls.Count == 0) return 0;

            int count = 0;
            using (Transaction t = new Transaction(_doc, "BIMBCC: Размеры на плане"))
            {
                t.Start();
                foreach (Wall w in walls)
                {
                    LocationCurve lc = w.Location as LocationCurve;
                    if (lc == null) continue;

                    Curve curve = lc.Curve;
                    XYZ p1 = curve.GetEndPoint(0);
                    XYZ p2 = curve.GetEndPoint(1);

                    XYZ dir = (p2 - p1).Normalize();
                    XYZ normal = new XYZ(-dir.Y, dir.X, 0);

                    Line dimLine = Line.CreateUnbound(p1 + normal * 2.0, dir);

                    ReferenceArray refArray = new ReferenceArray();
                    refArray.Append(new Reference(w));

                    try
                    {
                        Dimension dim = _doc.Create.NewDimension(activeView, dimLine, refArray);
                        if (dim != null) count++;
                    }
                    catch { }
                }
                t.Commit();
            }

            return count;
        }
    }
}
