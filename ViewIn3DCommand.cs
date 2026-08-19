using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    [Transaction(TransactionMode.Manual)]
    public class ViewIn3DCommand : IExternalCommand
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
                ViewIn3DWindow win = new ViewIn3DWindow(uidoc);
                if (win.ShowDialog() == true)
                {
                    ICollection<ElementId> finalIds = win.PickedElementIds ?? selectedIds;
                    ViewIn3DEngine engine = new ViewIn3DEngine(uidoc);
                    engine.CreateOrUpdate3DSectionView(finalIds, win.PaddingMm, win.ShowSectionBox);
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
