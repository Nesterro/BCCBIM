using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace BCCPlugIn
{
    public class LineDimensionsEngine
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;

        public LineDimensionsEngine(UIDocument uidoc)
        {
            _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
            _doc = _uidoc.Document;
        }

        public void PlaceDimensionsAlongLine()
        {
            Reference refLine = _uidoc.Selection.PickObject(ObjectType.Element, "Выберите опорную линию для простановки размеров");
            Element lineElem = _doc.GetElement(refLine.ElementId);
            if (lineElem == null) return;

            LocationCurve lc = lineElem.Location as LocationCurve;
            if (lc == null) throw new Exception("Элемент не имеет опорной линии.");

            Curve lineCurve = lc.Curve;
            XYZ p1 = lineCurve.GetEndPoint(0);
            XYZ p2 = lineCurve.GetEndPoint(1);
            Line dimLine = Line.CreateUnbound(p1, (p2 - p1).Normalize());

            List<Wall> intersectedWalls = new FilteredElementCollector(_doc, _uidoc.ActiveView.Id)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .ToList();

            ReferenceArray refArray = new ReferenceArray();
            foreach (Wall w in intersectedWalls)
            {
                refArray.Append(new Reference(w));
            }

            if (refArray.Size >= 2)
            {
                using (Transaction t = new Transaction(_doc, "BIMBCC: Размеры вдоль линии"))
                {
                    t.Start();
                    _doc.Create.NewDimension(_uidoc.ActiveView, dimLine, refArray);
                    t.Commit();
                }
            }
        }
    }
}
