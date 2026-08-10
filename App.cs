using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // Create Ribbon Tab
                string tabName = "BIMBCC";
                try
                {
                    application.CreateRibbonTab(tabName);
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    // Tab already exists
                }

                string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;
                Assembly assembly = Assembly.GetExecutingAssembly();

                // Icon helper function for 32x32 (Ribbon) and 16x16 (Quick Access Toolbar)
                Func<string, bool, BitmapImage> getIcon = (name, isSmall) =>
                    GetEmbeddedImage(assembly, $"BCCPlugIn.Resources.{name}{(isSmall ? "_small" : "")}.png");

                // ----------------------------------------------------
                // 1. Panel "Экспорт"
                // ----------------------------------------------------
                string exportPanelName = "Экспорт";
                RibbonPanel exportPanel = application.GetRibbonPanels(tabName).FirstOrDefault(p => p.Name == exportPanelName)
                                       ?? application.CreateRibbonPanel(tabName, exportPanelName);

                PushButtonData pdfButtonData = new PushButtonData("BccExportPdfButton", "Экспорт\nPDF", thisAssemblyPath, typeof(ExportPdfCommand).FullName)
                {
                    ToolTip = "Экспорт выбранных листов проекта Revit в PDF формат.",
                    LargeImage = getIcon("pdf_export_icon", false),
                    Image = getIcon("pdf_export_icon", true)
                };
                exportPanel.AddItem(pdfButtonData);

                PushButtonData schedulePackButtonData = new PushButtonData("BccSchedulePackButton", "Пакет\nспецификаций", thisAssemblyPath, typeof(SchedulePackCommand).FullName)
                {
                    ToolTip = "Пакетный экспорт спецификаций проекта в CSV / TXT формат.",
                    LargeImage = getIcon("schedule_pack_icon", false),
                    Image = getIcon("schedule_pack_icon", true)
                };
                exportPanel.AddItem(schedulePackButtonData);

                // ----------------------------------------------------
                // 2. Panel "Моделирование"
                // ----------------------------------------------------
                string modelPanelName = "Моделирование";
                RibbonPanel modelPanel = application.GetRibbonPanels(tabName).FirstOrDefault(p => p.Name == modelPanelName)
                                      ?? application.CreateRibbonPanel(tabName, modelPanelName);

                PushButtonData hangersButtonData = new PushButtonData("BccHangersButton", "Расстановка\nкрепежа", thisAssemblyPath, typeof(HangersCommand).FullName)
                {
                    ToolTip = "Автоматическая расстановка крепежа для инженерных сетей с заданным шагом.",
                    LargeImage = getIcon("hangers_icon", false),
                    Image = getIcon("hangers_icon", true)
                };
                modelPanel.AddItem(hangersButtonData);

                PushButtonData openingsButtonData = new PushButtonData("BccOpeningsButton", "Задание на\nотверстия", thisAssemblyPath, typeof(OpeningsCommand).FullName)
                {
                    ToolTip = "Автоматическая расстановка отверстий в стенах и перекрытиях на пересечении с инженерными сетями.",
                    LargeImage = getIcon("openings_icon", false),
                    Image = getIcon("openings_icon", true)
                };
                modelPanel.AddItem(openingsButtonData);

                PushButtonData placeByXyzButtonData = new PushButtonData("BccPlaceByXyzButton", "Импорт\nXYZ", thisAssemblyPath, typeof(PlaceByXyzCommand).FullName)
                {
                    ToolTip = "Расстановка семейств по координатам XYZ из файла CSV/TXT (AutoCAD GEO).",
                    LargeImage = getIcon("place_xyz_icon", false),
                    Image = getIcon("place_xyz_icon", true)
                };
                modelPanel.AddItem(placeByXyzButtonData);

                PushButtonData heatLossButtonData = new PushButtonData("BccHeatLossButton", "Теплопотери", thisAssemblyPath, typeof(HeatLossCommand).FullName)
                {
                    ToolTip = "Автоматический расчёт и расстановка кубиков-маркеров ограждающих конструкций в пространствах на основе связанных АР моделей.",
                    LargeImage = getIcon("heat_loss_icon", false),
                    Image = getIcon("heat_loss_icon", true)
                };
                modelPanel.AddItem(heatLossButtonData);

                PushButtonData levelingButtonData = new PushButtonData("BccLevelingButton", "Привязка к\nуровням", thisAssemblyPath, typeof(LevelingCommand).FullName)
                {
                    ToolTip = "Автоматическая привязка элементов к ближайшим уровням по высоте.",
                    LargeImage = getIcon("leveling_icon", false),
                    Image = getIcon("leveling_icon", true)
                };
                modelPanel.AddItem(levelingButtonData);

                // ----------------------------------------------------
                // 3. Panel "Параметры"
                // ----------------------------------------------------
                string paramPanelName = "Параметры";
                RibbonPanel paramPanel = application.GetRibbonPanels(tabName).FirstOrDefault(p => p.Name == paramPanelName)
                                      ?? application.CreateRibbonPanel(tabName, paramPanelName);

                PushButtonData paramRulesButtonData = new PushButtonData("BccParamRulesButton", "Редактор\nправил", thisAssemblyPath, typeof(ParamRulesCommand).FullName)
                {
                    ToolTip = "Редактор правил LTools: фильтрация элементов, целевой параметр и конструктор значений по формулам.",
                    LargeImage = getIcon("rules_editor_icon", false),
                    Image = getIcon("rules_editor_icon", true)
                };
                paramPanel.AddItem(paramRulesButtonData);

                PushButtonData batchParamsButtonData = new PushButtonData("BccBatchParamsButton", "Пакетные\nпараметры", thisAssemblyPath, typeof(BatchParamsCommand).FullName)
                {
                    ToolTip = "Пакетное добавление общих параметров из файла ФОП в категории проекта Revit (аналог ModPlus и DiRoots).",
                    LargeImage = getIcon("batch_params_icon", false),
                    Image = getIcon("batch_params_icon", true)
                };
                paramPanel.AddItem(batchParamsButtonData);

                // ----------------------------------------------------
                // 4. Panel "Сервер"
                // ----------------------------------------------------
                string serverPanelName = "Сервер";
                RibbonPanel serverPanel = application.GetRibbonPanels(tabName).FirstOrDefault(p => p.Name == serverPanelName)
                                       ?? application.CreateRibbonPanel(tabName, serverPanelName);

                PushButtonData serverButtonData = new PushButtonData("BccRevitServerButton", "Скачать\nмодели", thisAssemblyPath, typeof(RevitServerCommand).FullName)
                {
                    ToolTip = "Скачивание моделей с Revit Server в локальную папку.",
                    LargeImage = getIcon("revit_server_icon", false),
                    Image = getIcon("revit_server_icon", true)
                };
                serverPanel.AddItem(serverButtonData);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка инициализации BIMBCC PlugIn", ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private BitmapImage GetEmbeddedImage(Assembly assembly, string resourcePath)
        {
            try
            {
                using (Stream stream = assembly.GetManifestResourceStream(resourcePath))
                {
                    if (stream == null) return null;

                    MemoryStream ms = new MemoryStream();
                    stream.CopyTo(ms);
                    ms.Position = 0;

                    BitmapImage image = new BitmapImage();
                    image.BeginInit();
                    image.StreamSource = ms;
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
