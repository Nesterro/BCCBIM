using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace BCCPlugIn
{
    public class MEPAlignEngine
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;

        public MEPAlignEngine(UIDocument uidoc)
        {
            _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
            _doc = _uidoc.Document;
        }

        public void AlignMEPElements()
        {
            Reference refSource = _uidoc.Selection.PickObject(ObjectType.Element, "Выберите эталонный элемент MEP (целевая отметка)");
            Reference refTarget = _uidoc.Selection.PickObject(ObjectType.Element, "Выберите выравниваемый элемент MEP");

            Element srcElem = _doc.GetElement(refSource.ElementId);
            Element tgtElem = _doc.GetElement(refTarget.ElementId);

            LocationCurve srcCurve = srcElem.Location as LocationCurve;
            LocationCurve tgtCurve = tgtElem.Location as LocationCurve;

            if (srcCurve == null || tgtCurve == null) throw new Exception("Оба элемента должны быть линейными сетями (воздуховодами или трубами).");

            double targetZ = srcCurve.Curve.Evaluate(0.5, true).Z;
            double currentZ = tgtCurve.Curve.Evaluate(0.5, true).Z;
            double diffZ = targetZ - currentZ;

            using (Transaction t = new Transaction(_doc, "BIMBCC: Выравнивание MEP сетей"))
            {
                t.Start();
                ElementTransformUtils.MoveElement(_doc, tgtElem.Id, new XYZ(0, 0, diffZ));
                t.Commit();
            }
        }
    }
}
