using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    public class ContextFilterEngine
    {
        private readonly UIDocument _uidoc;

        public ContextFilterEngine(UIDocument uidoc)
        {
            _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
        }

        public int SelectMatchingContextElements()
        {
            ICollection<ElementId> selectedIds = _uidoc.Selection.GetElementIds();
            if (selectedIds == null || selectedIds.Count == 0)
            {
                throw new Exception("Пожалуйста, выделите один исходный элемент для контекстного фильтра.");
            }

            Element firstElem = _uidoc.Document.GetElement(selectedIds.First());
            if (firstElem == null) return 0;

            ElementId typeId = firstElem.GetTypeId();
            ElementId catId = firstElem.Category != null ? firstElem.Category.Id : ElementId.InvalidElementId;

            List<Element> candidates = new FilteredElementCollector(_uidoc.Document, _uidoc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.Id == catId)
                .ToList();

            List<ElementId> matchingIds;
            if (typeId != ElementId.InvalidElementId)
            {
                matchingIds = candidates.Where(e => e.GetTypeId() == typeId).Select(e => e.Id).ToList();
            }
            else
            {
                matchingIds = candidates.Select(e => e.Id).ToList();
            }

            _uidoc.Selection.SetElementIds(matchingIds);
            return matchingIds.Count;
        }
    }
}
