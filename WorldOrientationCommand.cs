using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    [Transaction(TransactionMode.Manual)]
    public class WorldOrientationCommand : IExternalCommand
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
                WorldOrientationEngine engine = new WorldOrientationEngine(uidoc.Document);
                int count = engine.CalculateAndWriteOrientation(selectedIds);

                TaskDialog.Show("Успешно", $"Ориентация стороны света рассчитана и записана для {count} наружных стен/ограждений.");
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
