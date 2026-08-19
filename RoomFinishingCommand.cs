using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    [Transaction(TransactionMode.Manual)]
    public class RoomFinishingCommand : IExternalCommand
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
                RoomFinishingWindow win = new RoomFinishingWindow(uidoc.Document);
                if (win.ShowDialog() == true)
                {
                    RoomFinishingEngine engine = new RoomFinishingEngine(uidoc.Document);
                    int count = engine.CreateRoomWallFinishing(selectedIds, win.SelectedWallType, win.FinishHeightMm);
                    TaskDialog.Show("Отделка стен", $"Успешно создано {count} участков отделочных стен.");
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
