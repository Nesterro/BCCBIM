using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace BCCPlugIn
{
    public class MEPRotateEngine
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;

        public MEPRotateEngine(UIDocument uidoc)
        {
            _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
            _doc = _uidoc.Document;
        }

        public void RotateMEPElement(double angleDegrees = 90.0)
        {
            Reference r = _uidoc.Selection.PickObject(ObjectType.Element, "Выберите элемент MEP для вращения");
            Element elem = _doc.GetElement(r.ElementId);
            if (elem == null) return;

            LocationPoint locPoint = elem.Location as LocationPoint;
            LocationCurve locCurve = elem.Location as LocationCurve;

            XYZ point = locPoint != null ? locPoint.Point : (locCurve != null ? locCurve.Curve.Evaluate(0.5, true) : null);
            if (point == null) throw new Exception("Элемент не имеет опорной точки для вращения.");

            Line axis = Line.CreateUnbound(point, XYZ.BasisZ);
            double angleRad = angleDegrees * Math.PI / 180.0;

            using (Transaction t = new Transaction(_doc, "BIMBCC: Вращение MEP элемента"))
            {
                t.Start();
                ElementTransformUtils.RotateElement(_doc, elem.Id, axis, angleRad);
                t.Commit();
            }
        }
    }
}
