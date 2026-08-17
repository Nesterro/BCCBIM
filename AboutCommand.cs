using System;
using System.Diagnostics;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AboutCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                string version = App.GetPluginVersion();

                TaskDialog td = new TaskDialog("О плагине BIMBCC");
                td.MainInstruction = $"BIMBCC PlugIn {version}";
                td.MainContent =
                    "Комплексный плагин автоматизации проектирования для Autodesk Revit.\n\n" +
                    "📌 Установленные модули:\n" +
                    "• Экспорт PDF (пакетная печать листов)\n" +
                    "• Пакет спецификаций (экспорт в CSV/TXT)\n" +
                    "• Расстановка крепежа (инженерные сети)\n" +
                    "• Задание на отверстия (пересечения сетей и стен)\n" +
                    "• Импорт XYZ (расстановка по геодезическим координатам)\n" +
                    "• Привязка к уровням\n" +
                    "• Теплопотери (расстановка кубиков, расчёт Q и экспорт в Excel)\n" +
                    "• Редактор правил LTools и Пакетные параметры\n" +
                    "• Копирование фильтров видов\n" +
                    "• Скачивание моделей с Revit Server";

                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "🌐 Открыть страницу проекта на GitHub",
                    "Просмотр истории версий, документации и исходного кода");

                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "📦 Проверить наличие обновлений",
                    "Перейти к разделу релизов и свежих установочных файлов BIMBCC_Installer");

                td.CommonButtons = TaskDialogCommonButtons.Close;
                td.DefaultButton = TaskDialogResult.CommandLink1;

                TaskDialogResult res = td.Show();

                if (res == TaskDialogResult.CommandLink1)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("https://github.com/Nesterro/BCCBIM") { UseShellExecute = true });
                    }
                    catch { }
                }
                else if (res == TaskDialogResult.CommandLink2)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("https://github.com/Nesterro/BCCBIM/releases") { UseShellExecute = true });
                    }
                    catch { }
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
