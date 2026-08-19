using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    [Transaction(TransactionMode.Manual)]
    public class MEPClashBypassCommand : IExternalCommand
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
                MEPClashBypassEngine engine = new MEPClashBypassEngine(uidoc);
                engine.BypassObstruction();
                TaskDialog.Show("Обход пересечений", "Участок сети успешно отстранен от пересечения.");
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
