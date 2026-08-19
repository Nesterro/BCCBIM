using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    [Transaction(TransactionMode.Manual)]
    public class PlanDimensionsCommand : IExternalCommand
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
                PlanDimensionsWindow win = new PlanDimensionsWindow();
                if (win.ShowDialog() == true)
                {
                    PlanDimensionsEngine engine = new PlanDimensionsEngine(uidoc);
                    int count = engine.CreatePlanDimensions(win.DimWalls, win.DimOpenings, win.DimGrids);
                    TaskDialog.Show("Размеры на плане", $"Успешно создано {count} цепочек размеров на плане.");
                }
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
