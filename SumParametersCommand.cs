using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    [Transaction(TransactionMode.Manual)]
    public class SumParametersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null || uidoc.Document == null)
            {
                message = "Нет активного документа Revit.";
                return Result.Failed;
            }

            ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
            if (selectedIds == null || selectedIds.Count == 0)
            {
                TaskDialog.Show("Внимание", "Пожалуйста, выделите элементы в модели для расчета статистики параметров.");
                return Result.Succeeded;
            }

            SumParametersWindow win = new SumParametersWindow(uidoc.Document, selectedIds);
            win.ShowDialog();
            return Result.Succeeded;
        }
    }
}
