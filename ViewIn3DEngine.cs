using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    public class ViewIn3DEngine
    {
        private readonly Document _doc;
        private readonly UIDocument _uidoc;

        public ViewIn3DEngine(UIDocument uidoc)
        {
            _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
            _doc = _uidoc.Document;
        }

        public View3D CreateOrUpdate3DSectionView(ICollection<ElementId> selectedIds, double paddingMm, bool showSectionBox)
        {
            BoundingBoxXYZ bbox = null;

            if (selectedIds != null && selectedIds.Count > 0)
            {
                bbox = GetBoundingBoxFromElements(selectedIds);
            }

            if (bbox == null && _uidoc.ActiveView is ViewPlan planView)
            {
                bbox = planView.CropBoxActive ? planView.CropBox : null;
            }

            if (bbox == null)
            {
                throw new Exception("Пожалуйста, выделите один или несколько элементов для 3D подрезки.");
            }

            // Convert mm to internal feet (1 ft = 304.8 mm)
            double paddingFeet = paddingMm / 304.8;
            BoundingBoxXYZ expandedBBox = new BoundingBoxXYZ
            {
                Min = new XYZ(bbox.Min.X - paddingFeet, bbox.Min.Y - paddingFeet, bbox.Min.Z - paddingFeet),
                Max = new XYZ(bbox.Max.X + paddingFeet, bbox.Max.Y + paddingFeet, bbox.Max.Z + paddingFeet)
            };

            View3D view3d = GetOrCreate3DSectionView();

            using (Transaction t = new Transaction(_doc, "BIMBCC: 3D подрезка"))
            {
                t.Start();
                view3d.IsSectionBoxActive = true;
                view3d.SetSectionBox(expandedBBox);

                // Set section box visibility
                Category sectionBoxCat = _doc.Settings.Categories.get_Item(BuiltInCategory.OST_SectionBox);
                if (sectionBoxCat != null && view3d.CanCategoryBeHidden(sectionBoxCat.Id))
                {
                    view3d.SetCategoryHidden(sectionBoxCat.Id, !showSectionBox);
                }

                t.Commit();
            }

            _uidoc.ActiveView = view3d;
            return view3d;
        }

        private BoundingBoxXYZ GetBoundingBoxFromElements(ICollection<ElementId> elementIds)
        {
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            bool found = false;

            foreach (ElementId id in elementIds)
            {
                Element elem = _doc.GetElement(id);
                if (elem == null) continue;

                BoundingBoxXYZ b = elem.get_BoundingBox(null);
                if (b != null)
                {
                    minX = Math.Min(minX, b.Min.X);
                    minY = Math.Min(minY, b.Min.Y);
                    minZ = Math.Min(minZ, b.Min.Z);
                    maxX = Math.Max(maxX, b.Max.X);
                    maxY = Math.Max(maxY, b.Max.Y);
                    maxZ = Math.Max(maxZ, b.Max.Z);
                    found = true;
                }
            }

            if (!found) return null;

            return new BoundingBoxXYZ
            {
                Min = new XYZ(minX, minY, minZ),
                Max = new XYZ(maxX, maxY, maxZ)
            };
        }

        private View3D GetOrCreate3DSectionView()
        {
            string viewName = "{3D - BIMBCC Подрезка}";

            View3D existing = new FilteredElementCollector(_doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && v.Name.Equals(viewName, StringComparison.OrdinalIgnoreCase));

            if (existing != null) return existing;

            ViewFamilyType vft = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);

            if (vft == null) throw new Exception("В документе не найден тип 3D вида.");

            using (Transaction t = new Transaction(_doc, "BIMBCC: Создание 3D вида"))
            {
                t.Start();
                View3D newView = View3D.CreateIsometric(_doc, vft.Id);
                try { newView.Name = viewName; } catch { }
                t.Commit();
                return newView;
            }
        }
    }
}
