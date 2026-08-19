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

                PushButtonData levelingButtonData = new PushButtonData("BccLevelingButton", "Привязка к\nуровням", thisAssemblyPath, typeof(LevelingCommand).FullName)
                {
                    ToolTip = "Автоматическая привязка элементов к ближайшим уровням по высоте.",
                    LargeImage = getIcon("leveling_icon", false),
                    Image = getIcon("leveling_icon", true)
                };
                modelPanel.AddItem(levelingButtonData);

                PushButtonData heatLossButtonData = new PushButtonData("BccHeatLossButton", "Тепло-\nпотери", thisAssemblyPath, typeof(HeatLossCommand).FullName)
                {
                    ToolTip = "Расстановка кубиков теплопотерь по ограждающим конструкциям помещений и создание спецификации.",
                    LargeImage = getIcon("heat_loss_icon", false),
                    Image = getIcon("heat_loss_icon", true)
                };
                modelPanel.AddItem(heatLossButtonData);

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
                // 4. Panel "Виды"
                // ----------------------------------------------------
                string viewsPanelName = "Виды";
                RibbonPanel viewsPanel = application.GetRibbonPanels(tabName).FirstOrDefault(p => p.Name == viewsPanelName)
                                      ?? application.CreateRibbonPanel(tabName, viewsPanelName);

                PushButtonData filterCopyButtonData = new PushButtonData("BccFilterCopyButton", "Копирование\nфильтров", thisAssemblyPath, typeof(FilterCopyCommand).FullName)
                {
                    ToolTip = "Пакетное копирование фильтров видов и переопределений графики между видами и шаблонами видов.",
                    LargeImage = getIcon("filter_copy_icon", false),
                    Image = getIcon("filter_copy_icon", true)
                };
                viewsPanel.AddItem(filterCopyButtonData);

                PushButtonData viewIn3dButtonData = new PushButtonData("BccViewIn3DButton", "3D\nподрезка", thisAssemblyPath, typeof(ViewIn3DCommand).FullName)
                {
                    ToolTip = "Быстрое создание 3D-вида подрезкой по выделенным элементам или помещению.",
                    LargeImage = getIcon("view_in_3d_icon", false),
                    Image = getIcon("view_in_3d_icon", true)
                };
                viewsPanel.AddItem(viewIn3dButtonData);

                PushButtonData copyCropBoxButtonData = new PushButtonData("BccCopyCropBoxButton", "Копировать\nобрезку", thisAssemblyPath, typeof(CopyCropBoxCommand).FullName)
                {
                    ToolTip = "Копирование границ обрезки и секущего диапазона между видами.",
                    LargeImage = getIcon("copy_crop_box_icon", false),
                    Image = getIcon("copy_crop_box_icon", true)
                };
                viewsPanel.AddItem(copyCropBoxButtonData);

                // Add SumParameters, BaseLevel, WorldOrientation to Parameters panel
                PushButtonData sumParamsButtonData = new PushButtonData("BccSumParamsButton", "Сумма\nпараметров", thisAssemblyPath, typeof(SumParametersCommand).FullName)
                {
                    ToolTip = "Расчет суммы, среднего, мин/макс значений параметров для выбранных элементов.",
                    LargeImage = getIcon("sum_parameters_icon", false),
                    Image = getIcon("sum_parameters_icon", true)
                };
                paramPanel.AddItem(sumParamsButtonData);

                PushButtonData baseLevelButtonData = new PushButtonData("BccBaseLevelButton", "Базовый\nуровень", thisAssemblyPath, typeof(BaseLevelCommand).FullName)
                {
                    ToolTip = "Автоматический расчет и привязка базового уровня элементов к ближайшему нижележащему уровню.",
                    LargeImage = getIcon("base_level_icon", false),
                    Image = getIcon("base_level_icon", true)
                };
                paramPanel.AddItem(baseLevelButtonData);

                // ----------------------------------------------------
                // 4b. Panel "Аннотации"
                // ----------------------------------------------------
                string annotPanelName = "Аннотации";
                RibbonPanel annotPanel = application.GetRibbonPanels(tabName).FirstOrDefault(p => p.Name == annotPanelName)
                                      ?? application.CreateRibbonPanel(tabName, annotPanelName);

                PushButtonData fontReplacerButtonData = new PushButtonData("BccFontReplacerButton", "Заменить\nшрифт", thisAssemblyPath, typeof(FontReplacerCommand).FullName)
                {
                    ToolTip = "Пакетная замена шрифта во всех текстовых и размерных стилях проекта.",
                    LargeImage = getIcon("font_replacer_icon", false),
                    Image = getIcon("font_replacer_icon", true)
                };
                annotPanel.AddItem(fontReplacerButtonData);

                PushButtonData taglessButtonData = new PushButtonData("BccTaglessButton", "Без\nмарок", thisAssemblyPath, typeof(TaglessCommand).FullName)
                {
                    ToolTip = "Поиск и выделение немаркированных элементов на активном виде.",
                    LargeImage = getIcon("tagless_icon", false),
                    Image = getIcon("tagless_icon", true)
                };
                annotPanel.AddItem(taglessButtonData);

                // ----------------------------------------------------
                // 5. Panel "Сервер"
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

                // ----------------------------------------------------
                // 6. Panel "Инфо" (Версия установленного плагина)
                // ----------------------------------------------------
                string infoPanelName = "Инфо";
                RibbonPanel infoPanel = application.GetRibbonPanels(tabName).FirstOrDefault(p => p.Name == infoPanelName)
                                     ?? application.CreateRibbonPanel(tabName, infoPanelName);

                string currentVersion = GetPluginVersion();

                PushButtonData aboutButtonData = new PushButtonData("BccAboutButton", $"BIMBCC\n{currentVersion}", thisAssemblyPath, typeof(AboutCommand).FullName)
                {
                    ToolTip = $"BIMBCC PlugIn для Autodesk Revit\nТекущая установленная версия: {currentVersion}\n\nНажмите для просмотра информации о модулях и проверки обновлений на GitHub.",
                    LargeImage = getIcon("logo", false) ?? getIcon("rules_editor_icon", false),
                    Image = getIcon("logo", true) ?? getIcon("rules_editor_icon", true)
                };
                infoPanel.AddItem(aboutButtonData);

                // ----------------------------------------------------
                // 7. Custom Tab Logo Header (ModPlus style)
                // ----------------------------------------------------
                BitmapImage tabLogo = GetEmbeddedImage(assembly, "BCCPlugIn.Resources.logo.png");
                if (tabLogo != null)
                {
                    TrySetTabLogo(application, tabName, tabLogo);
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка инициализации BIMBCC PlugIn", ex.Message);
                return Result.Failed;
            }
        }

        public static string GetPluginVersion()
        {
            try
            {
                string asmLoc = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(asmLoc))
                {
                    string dir = Path.GetDirectoryName(asmLoc);
                    string vFile = Path.Combine(dir, "version.txt");
                    if (File.Exists(vFile))
                    {
                        string txt = File.ReadAllText(vFile).Trim();
                        if (!string.IsNullOrEmpty(txt)) return txt;
                    }
                }
            }
            catch { }

            try
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                if (v != null) return $"v{v.Major}.{v.Minor}.{v.Build}";
            }
            catch { }

            return "v3.15.0";
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private void TrySetTabLogo(UIControlledApplication application, string tabName, BitmapImage logoImage)
        {
            if (logoImage == null) return;

            EventHandler<Autodesk.Revit.UI.Events.IdlingEventArgs> handler = null;
            handler = (s, e) =>
            {
                try
                {
                    application.Idling -= handler;
                }
                catch { }
                ApplyTabLogoWpf(tabName, logoImage);
            };

            try
            {
                application.Idling += handler;
            }
            catch { }

            ApplyTabLogoWpf(tabName, logoImage);
        }

        private void ApplyTabLogoWpf(string tabName, BitmapImage logoImage)
        {
            try
            {
                Assembly adWinAsm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "AdWindows");
                if (adWinAsm == null) return;

                Type compMgrType = adWinAsm.GetType("Autodesk.Windows.ComponentManager");
                if (compMgrType == null) return;

                PropertyInfo ribbonProp = compMgrType.GetProperty("Ribbon", BindingFlags.Public | BindingFlags.Static);
                if (ribbonProp == null) return;

                object ribbonControl = ribbonProp.GetValue(null);
                if (ribbonControl == null) return;

                if (ribbonControl is System.Windows.Threading.DispatcherObject dispatcherObj)
                {
                    dispatcherObj.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            PropertyInfo tabsProp = ribbonControl.GetType().GetProperty("Tabs");
                            if (tabsProp == null) return;

                            var tabs = tabsProp.GetValue(ribbonControl) as System.Collections.IEnumerable;
                            if (tabs == null) return;

                            object targetTab = null;
                            foreach (var tab in tabs)
                            {
                                PropertyInfo titleProp = tab.GetType().GetProperty("Title");
                                PropertyInfo idProp = tab.GetType().GetProperty("Id");
                                string title = titleProp?.GetValue(tab)?.ToString();
                                string id = idProp?.GetValue(tab)?.ToString();

                                if (title == tabName || id == tabName || (title != null && title.Contains("BIMBCC")))
                                {
                                    targetTab = tab;
                                    break;
                                }
                            }

                            if (targetTab == null) return;

                            var tabButtons = FindVisualChildren(ribbonControl as System.Windows.DependencyObject, "RibbonTabButton");
                            foreach (var btn in tabButtons)
                            {
                                PropertyInfo dataContextProp = btn.GetType().GetProperty("DataContext");
                                PropertyInfo contentProp = btn.GetType().GetProperty("Content");

                                object dc = dataContextProp?.GetValue(btn);
                                object contentVal = contentProp?.GetValue(btn);

                                if (dc == targetTab || (contentVal != null && contentVal.ToString().Contains("BIMBCC")))
                                {
                                    var stackPanel = new System.Windows.Controls.StackPanel
                                    {
                                        Orientation = System.Windows.Controls.Orientation.Horizontal,
                                        VerticalAlignment = System.Windows.VerticalAlignment.Center,
                                        Margin = new System.Windows.Thickness(0)
                                    };

                                    var redDot = new System.Windows.Shapes.Ellipse
                                    {
                                        Width = 7,
                                        Height = 7,
                                        Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#C91414")),
                                        Margin = new System.Windows.Thickness(0, 0, 5, 0),
                                        VerticalAlignment = System.Windows.VerticalAlignment.Center,
                                        SnapsToDevicePixels = true
                                    };

                                    var textBlock = new System.Windows.Controls.TextBlock
                                    {
                                        Text = tabName,
                                        VerticalAlignment = System.Windows.VerticalAlignment.Center,
                                        FontWeight = System.Windows.FontWeights.SemiBold,
                                        FontSize = 11.5,
                                        Margin = new System.Windows.Thickness(0)
                                    };

                                    stackPanel.Children.Add(redDot);
                                    stackPanel.Children.Add(textBlock);

                                    contentProp?.SetValue(btn, stackPanel);
                                    break;
                                }
                            }
                        }
                        catch { }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            catch { }
        }

        private static System.Collections.Generic.List<System.Windows.DependencyObject> FindVisualChildren(System.Windows.DependencyObject depObj, string typeName)
        {
            var results = new System.Collections.Generic.List<System.Windows.DependencyObject>();
            if (depObj == null) return results;

            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj);
            for (int i = 0; i < count; i++)
            {
                System.Windows.DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
                if (child != null)
                {
                    if (child.GetType().Name == typeName)
                    {
                        results.Add(child);
                    }
                    results.AddRange(FindVisualChildren(child, typeName));
                }
            }
            return results;
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
