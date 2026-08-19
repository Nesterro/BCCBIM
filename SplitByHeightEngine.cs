using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace BCCPlugIn
{
    public class SplitByHeightEngine
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;

        public SplitByHeightEngine(UIDocument uidoc)
        {
            _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
            _doc = _uidoc.Document;
        }

        public void SplitElementAtHeight(double heightMm = 1500)
        {
            Reference r = _uidoc.Selection.PickObject(ObjectType.Element, "Выберите стену или колонну для разделения по высоте");
            Element elem = _doc.GetElement(r.ElementId);
            if (!(elem is Wall)) throw new Exception("Разделение по высоте поддерживается для стен.");

            Wall wall = elem as Wall;
            LocationCurve lc = wall.Location as LocationCurve;
            if (lc == null) return;

            double splitOffsetFt = heightMm / 304.8;

            using (Transaction t = new Transaction(_doc, "BIMBCC: Разделение по высоте"))
            {
                t.Start();
                ElementId newWallId = ElementTransformUtils.CopyElement(_doc, wall.Id, new XYZ(0, 0, splitOffsetFt)).First();
                Wall newWall = _doc.GetElement(newWallId) as Wall;

                Parameter pHeight1 = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                if (pHeight1 != null && !pHeight1.IsReadOnly) pHeight1.Set(splitOffsetFt);

                t.Commit();
            }
        }
    }
}
