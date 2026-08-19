using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    [Transaction(TransactionMode.Manual)]
    public class BaseLevelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null || uidoc.Document == null)
            {
                message = "Нет активного документа Revit.";
                return Result.Failed;
            }

            try
            {
                ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
                BaseLevelEngine engine = new BaseLevelEngine(uidoc.Document);
                int count = engine.AssignBaseLevels(selectedIds);

                TaskDialog.Show("Успешно", $"Базовый уровень определен и присвоен для {count} элементов.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
