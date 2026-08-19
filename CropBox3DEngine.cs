using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    public class CropBox3DEngine
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;

        public CropBox3DEngine(UIDocument uidoc)
        {
            _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
            _doc = _uidoc.Document;
        }

        public void ToggleOrReset3DCropBox()
        {
            if (!(_uidoc.ActiveView is View3D view3d)) throw new Exception("Пожалуйста, перейдите на 3D вид.");

            using (Transaction t = new Transaction(_doc, "BIMBCC: 3D подрезка вида"))
            {
                t.Start();
                view3d.IsSectionBoxActive = !view3d.IsSectionBoxActive;
                t.Commit();
            }
        }
    }
}
