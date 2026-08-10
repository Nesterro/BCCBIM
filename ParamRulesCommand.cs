using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BCCPlugIn
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ParamRulesCommand : IExternalCommand
    {
        private static bool _resolverRegistered = false;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            RegisterAssemblyResolver();

            try
            {
                string ltoolsDllPath = GetLToolsDllPath();
                if (!File.Exists(ltoolsDllPath))
                {
                    TaskDialog.Show("BIMBCC | Ошибка", $"Файл LTools.dll не найден по пути:\n{ltoolsDllPath}\n\nУбедитесь, что LTools установлен.");
                    return Result.Failed;
                }

                Assembly ltoolsAsm = Assembly.LoadFrom(ltoolsDllPath);
                Type rulerType = ltoolsAsm.GetType("SAV.ParamRules.FrmRuler");

                if (rulerType == null)
                {
                    TaskDialog.Show("BIMBCC | Ошибка", "Не удалось найти тип SAV.ParamRules.FrmRuler в LTools.dll.");
                    return Result.Failed;
                }

                // Launch native LTools FrmRuler Form directly inside Revit
                System.Windows.Forms.Form rulerForm = (System.Windows.Forms.Form)Activator.CreateInstance(rulerType);
                IWin32Window revitWindow = new RevitWindowHandler(commandData.Application.MainWindowHandle);
                rulerForm.ShowDialog(revitWindow);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("BIMBCC | Ошибка LTools", $"Ошибка при запуске редактора правил LTools:\n{ex.ToString()}");
                return Result.Failed;
            }
        }

        private static void RegisterAssemblyResolver()
        {
            if (_resolverRegistered) return;
            _resolverRegistered = true;

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    string folder = Path.GetDirectoryName(GetLToolsDllPath());
                    if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;

                    string asmName = new AssemblyName(args.Name).Name + ".dll";
                    string asmPath = Path.Combine(folder, asmName);

                    if (File.Exists(asmPath))
                    {
                        return Assembly.LoadFrom(asmPath);
                    }
                }
                catch { }

                return null;
            };
        }

        private static string GetLToolsDllPath()
        {
            // 1. AppData installation path
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"BIMBCC\PlugIn\LTools\LTools.dll"
            );
            if (File.Exists(appDataPath)) return appDataPath;

            // 2. Source path in LTools directory
            string sourcePath = @"C:\Users\user\Yandex.Disk\BCC\BCC PlugIn\Ltools\LTools\2024\LTools\LTools.dll";
            if (File.Exists(sourcePath)) return sourcePath;

            return appDataPath;
        }

        private class RevitWindowHandler : IWin32Window
        {
            public IntPtr Handle { get; }
            public RevitWindowHandler(IntPtr handle) { Handle = handle; }
        }
    }
}
