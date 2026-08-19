using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace BCCPlugIn
{
    public class MEPClashBypassEngine
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;

        public MEPClashBypassEngine(UIDocument uidoc)
        {
            _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
            _doc = _uidoc.Document;
        }

        public void BypassObstruction()
        {
            Reference refMep = _uidoc.Selection.PickObject(ObjectType.Element, "Выберите элемент инженерной сети (воздуховод / трубу)");
            Reference refTarget = _uidoc.Selection.PickObject(ObjectType.Element, "Выберите преграду для обхода (стену, балку, пересекающую сеть)");

            Element mepElem = _doc.GetElement(refMep.ElementId);
            Element targetElem = _doc.GetElement(refTarget.ElementId);

            if (mepElem == null || targetElem == null) return;

            LocationCurve locCurve = mepElem.Location as LocationCurve;
            if (locCurve == null) throw new Exception("Выбранный элемент MEP не имеет центральной линии.");

            Curve curve = locCurve.Curve;
            BoundingBoxXYZ targetBBox = targetElem.get_BoundingBox(null);
            if (targetBBox == null) throw new Exception("Не удалось рассчитать габариты преграды.");

            double offsetFeet = (targetBBox.Max.Z - targetBBox.Min.Z) + 0.5;

            using (Transaction t = new Transaction(_doc, "BIMBCC: Обход пересечения MEP"))
            {
                t.Start();
                XYZ midPoint = curve.Evaluate(0.5, true);
                XYZ newPos = new XYZ(midPoint.X, midPoint.Y, midPoint.Z + offsetFeet);

                ElementTransformUtils.MoveElement(_doc, mepElem.Id, new XYZ(0, 0, offsetFeet));
                t.Commit();
            }
        }
    }
}
