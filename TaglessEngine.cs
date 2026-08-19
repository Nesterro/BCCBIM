using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    public class TaglessEngine
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;

        public TaglessEngine(UIDocument uidoc)
        {
            _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
            _doc = _uidoc.Document;
        }

        public int SelectUntaggedElementsOnActiveView(List<ElementId> categoryIds)
        {
            View activeView = _uidoc.ActiveView;
            if (activeView == null) throw new Exception("Нет активного вида.");

            HashSet<ElementId> categoryIdSet = categoryIds != null ? new HashSet<ElementId>(categoryIds) : null;

            // Collect all independent tags on active view
            HashSet<ElementId> taggedElementIds = new HashSet<ElementId>();
            List<IndependentTag> tags = new FilteredElementCollector(_doc, activeView.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            foreach (IndependentTag tag in tags)
            {
                try
                {
                    ICollection<ElementId> taggedIds = tag.GetTaggedElementIds().Select(linkId => linkId.HostElementId).ToList();
                    foreach (ElementId id in taggedIds)
                    {
                        if (id != ElementId.InvalidElementId) taggedElementIds.Add(id);
                    }
                }
                catch { }
            }

            // Collect model elements on active view belonging to selected categories
            List<Element> visibleElements = new FilteredElementCollector(_doc, activeView.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.CategoryType == CategoryType.Model && !e.IsHidden(activeView))
                .Where(e => categoryIdSet == null || categoryIdSet.Contains(e.Category.Id))
                .ToList();

            List<ElementId> untaggedIds = visibleElements
                .Where(e => !taggedElementIds.Contains(e.Id))
                .Select(e => e.Id)
                .ToList();

            if (untaggedIds.Count > 0)
            {
                _uidoc.Selection.SetElementIds(untaggedIds);
            }

            return untaggedIds.Count;
        }
    }
}
