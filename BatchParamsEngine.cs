using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public static class BatchParamsEngine
    {
        public static List<BindingGroupItem> GetBindingGroups()
        {
            return new List<BindingGroupItem>
            {
                new BindingGroupItem { Name = "Данные", BuiltInGroup = BuiltInParameterGroup.PG_DATA },
                new BindingGroupItem { Name = "Текст", BuiltInGroup = BuiltInParameterGroup.PG_TEXT },
                new BindingGroupItem { Name = "Идентификация", BuiltInGroup = BuiltInParameterGroup.PG_IDENTITY_DATA },
                new BindingGroupItem { Name = "Размеры", BuiltInGroup = BuiltInParameterGroup.PG_GEOMETRY },
                new BindingGroupItem { Name = "Механические", BuiltInGroup = BuiltInParameterGroup.PG_MECHANICAL },
                new BindingGroupItem { Name = "Электросети", BuiltInGroup = BuiltInParameterGroup.PG_ELECTRICAL },
                new BindingGroupItem { Name = "Сантехника", BuiltInGroup = BuiltInParameterGroup.PG_PLUMBING },
                new BindingGroupItem { Name = "Материалы", BuiltInGroup = BuiltInParameterGroup.PG_MATERIALS },
                new BindingGroupItem { Name = "Прочее", BuiltInGroup = BuiltInParameterGroup.INVALID }
            };
        }

        public static List<CategoryItem> GetDocumentCategories(Document doc)
        {
            List<CategoryItem> categories = new List<CategoryItem>();
            if (doc == null || doc.IsFamilyDocument) return categories;

            Categories docCats = doc.Settings.Categories;
            foreach (Category cat in docCats)
            {
                if (cat.AllowsBoundParameters && cat.CategoryType == CategoryType.Model)
                {
                    categories.Add(new CategoryItem
                    {
                        Name = cat.Name,
                        BuiltInCat = cat.BuiltInCategory,
                        Category = cat,
                        IsSelected = false
                    });
                }
            }

            return categories.OrderBy(c => c.Name).ToList();
        }

        public static (int addedCount, int updatedCount) ExecuteBatchAddParameters(
            Document doc,
            string fopPath,
            List<SharedParamItem> paramsToAdd,
            List<CategoryItem> categories,
            Action<int, string> progressCallback)
        {
            if (doc == null || paramsToAdd == null || paramsToAdd.Count == 0) return (0, 0);

            int addedCount = 0;
            int updatedCount = 0;

            DefinitionFile defFile = null;
            if (!string.IsNullOrEmpty(fopPath) && File.Exists(fopPath))
            {
                try
                {
                    doc.Application.SharedParametersFilename = fopPath;
                    defFile = doc.Application.OpenSharedParameterFile();
                }
                catch { }
            }

            using (Transaction trans = new Transaction(doc, "BIMBCC Пакетное добавление параметров"))
            {
                trans.Start();

                int total = paramsToAdd.Count;

                if (doc.IsFamilyDocument)
                {
                    // ----------------------------------------------------
                    // FAMILY EDITOR (.rfa) BINDING
                    // ----------------------------------------------------
                    FamilyManager famMgr = doc.FamilyManager;

                    for (int i = 0; i < total; i++)
                    {
                        var item = paramsToAdd[i];
                        bool isInstance = item.IsInstance;
                        BuiltInParameterGroup group = item.BindingGroup;

                        double pct = ((double)(i + 1) / total) * 100;
                        progressCallback?.Invoke((int)pct, $"Добавление в семейство ({i + 1}/{total}): {item.Name}...");

                        ExternalDefinition extDef = GetExternalDefinition(item, defFile);
                        if (extDef == null) continue;

                        try
                        {
                            FamilyParameter existing = famMgr.get_Parameter(extDef.GUID);
                            if (existing == null)
                            {
                                existing = famMgr.get_Parameter(extDef.Name);
                            }

                            if (existing == null)
                            {
                                existing = famMgr.AddParameter(extDef, group, isInstance);
                                addedCount++;
                            }
                            else
                            {
                                famMgr.ReplaceParameter(existing, extDef, group, isInstance);
                                updatedCount++;
                            }

                            // Apply formula / default value if provided
                            string formulaToApply = !string.IsNullOrWhiteSpace(item.Formula) ? item.Formula : item.DefaultValue;
                            if (existing != null && !string.IsNullOrWhiteSpace(formulaToApply))
                            {
                                formulaToApply = formulaToApply.Trim();
                                string dt = item.DataType != null ? item.DataType.ToLowerInvariant() : "";
                                bool isText = dt.Contains("string") || dt.Contains("text") || dt.Contains("multiline");

                                if (isText)
                                {
                                    if (!formulaToApply.StartsWith("\""))
                                    {
                                        formulaToApply = "\"" + formulaToApply + "\"";
                                    }
                                }
                                else
                                {
                                    // For numbers, lengths, integers, angles: use '.' for decimal separator and DO NOT enclose in quotes
                                    formulaToApply = formulaToApply.Replace(',', '.');
                                    if (formulaToApply.StartsWith("\"") && formulaToApply.EndsWith("\"") && formulaToApply.Length > 2)
                                    {
                                        formulaToApply = formulaToApply.Substring(1, formulaToApply.Length - 2);
                                    }
                                }

                                try
                                {
                                    famMgr.SetFormula(existing, formulaToApply);
                                }
                                catch
                                {
                                    // Fallback: set direct value if formula assignment fails
                                    try
                                    {
                                        if (isText)
                                        {
                                            famMgr.Set(existing, item.DefaultValue ?? "");
                                        }
                                        else if (double.TryParse(formulaToApply, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dVal))
                                        {
                                            famMgr.Set(existing, dVal);
                                        }
                                        else if (int.TryParse(formulaToApply, out int iVal))
                                        {
                                            famMgr.Set(existing, iVal);
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch { }
                    }
                }
                else
                {
                    // ----------------------------------------------------
                    // PROJECT (.rvt) BINDING
                    // ----------------------------------------------------
                    if (categories == null || categories.Count == 0) return (0, 0);

                    CategorySet catSet = doc.Application.Create.NewCategorySet();
                    foreach (var catItem in categories)
                    {
                        if (catItem.Category != null)
                        {
                            catSet.Insert(catItem.Category);
                        }
                    }

                    for (int i = 0; i < total; i++)
                    {
                        var item = paramsToAdd[i];
                        bool isInstance = item.IsInstance;
                        BuiltInParameterGroup group = item.BindingGroup;

                        double pct = ((double)(i + 1) / total) * 100;
                        progressCallback?.Invoke((int)pct, $"Добавление в проект ({i + 1}/{total}): {item.Name}...");

                        ExternalDefinition extDef = GetExternalDefinition(item, defFile);
                        if (extDef == null) continue;

                        ElementBinding binding = isInstance
                            ? (ElementBinding)doc.Application.Create.NewInstanceBinding(catSet)
                            : (ElementBinding)doc.Application.Create.NewTypeBinding(catSet);

                        Binding existingBinding = doc.ParameterBindings.get_Item(extDef);
                        if (existingBinding != null)
                        {
                            bool success = doc.ParameterBindings.ReInsert(extDef, binding, group);
                            if (success) updatedCount++;
                        }
                        else
                        {
                            bool success = doc.ParameterBindings.Insert(extDef, binding, group);
                            if (success) addedCount++;
                        }
                    }
                }

                trans.Commit();
            }

            return (addedCount, updatedCount);
        }

        private static ExternalDefinition GetExternalDefinition(SharedParamItem item, DefinitionFile defFile)
        {
            if (item.Definition != null) return item.Definition;

            if (defFile != null)
            {
                foreach (DefinitionGroup group in defFile.Groups)
                {
                    foreach (Definition def in group.Definitions)
                    {
                        if (def is ExternalDefinition extDef)
                        {
                            if (extDef.GUID == item.Guid || string.Equals(extDef.Name, item.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                return extDef;
                            }
                        }
                    }
                }
            }

            return null;
        }

        public static List<SharedParamItem> GetExistingDocumentParameters(Document doc)
        {
            List<SharedParamItem> list = new List<SharedParamItem>();
            if (doc == null) return list;

            if (doc.IsFamilyDocument)
            {
                FamilyManager famMgr = doc.FamilyManager;
                foreach (FamilyParameter fp in famMgr.Parameters)
                {
                    if (fp == null) continue;
                    list.Add(new SharedParamItem
                    {
                        Name = fp.Definition?.Name ?? "",
                        Guid = fp.IsShared ? fp.GUID : Guid.Empty,
                        GroupName = "Параметры семейства",
                        IsInstance = fp.IsInstance,
                        IsSelected = true
                    });
                }
            }
            else
            {
                BindingMap bindMap = doc.ParameterBindings;
                DefinitionBindingMapIterator it = bindMap.ForwardIterator();
                while (it.MoveNext())
                {
                    Definition def = it.Key;
                    Binding binding = it.Current as Binding;

                    if (def != null)
                    {
                        bool isInstance = (binding is InstanceBinding);
                        Guid pGuid = Guid.Empty;
                        if (def is ExternalDefinition extDef)
                        {
                            pGuid = extDef.GUID;
                        }

                        list.Add(new SharedParamItem
                        {
                            Name = def.Name,
                            Guid = pGuid,
                            GroupName = "Параметры проекта",
                            Definition = def as ExternalDefinition,
                            IsInstance = isInstance,
                            IsSelected = true
                        });
                    }
                }
            }

            return list.OrderBy(p => p.Name).ToList();
        }

        public static (int deletedCount, List<string> failedParams) ExecuteBatchDeleteParameters(
            Document doc,
            List<SharedParamItem> paramsToDelete,
            Action<int, string> progressCallback)
        {
            if (doc == null || paramsToDelete == null || paramsToDelete.Count == 0)
                return (0, new List<string>());

            int deletedCount = 0;
            List<string> failedParams = new List<string>();

            int total = paramsToDelete.Count;

            using (Transaction trans = new Transaction(doc, "BIMBCC Пакетное удаление параметров"))
            {
                trans.Start();

                if (doc.IsFamilyDocument)
                {
                    FamilyManager famMgr = doc.FamilyManager;
                    for (int i = 0; i < total; i++)
                    {
                        var item = paramsToDelete[i];
                        double pct = ((double)(i + 1) / total) * 100;
                        progressCallback?.Invoke((int)pct, $"Удаление из семейства ({i + 1}/{total}): {item.Name}...");

                        FamilyParameter fp = famMgr.get_Parameter(item.Name);
                        if (fp != null)
                        {
                            try
                            {
                                famMgr.RemoveParameter(fp);
                                deletedCount++;
                            }
                            catch
                            {
                                failedParams.Add(item.Name);
                            }
                        }
                    }
                }
                else
                {
                    BindingMap bindMap = doc.ParameterBindings;

                    // 1. Поиск совпадений в BindingMap
                    List<Definition> defsToRemove = new List<Definition>();
                    DefinitionBindingMapIterator it = bindMap.ForwardIterator();
                    while (it.MoveNext())
                    {
                        Definition def = it.Key;
                        if (def != null && paramsToDelete.Any(p => string.Equals(p.Name, def.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            defsToRemove.Add(def);
                        }
                    }

                    for (int i = 0; i < defsToRemove.Count; i++)
                    {
                        Definition def = defsToRemove[i];
                        double pct = ((double)(i + 1) / total) * 100;
                        progressCallback?.Invoke((int)pct, $"Удаление привязки ({i + 1}/{total}): {def.Name}...");

                        try
                        {
                            bool removed = bindMap.Remove(def);
                            if (removed) deletedCount++;
                        }
                        catch
                        {
                            failedParams.Add(def.Name);
                        }
                    }

                    // 2. Удаление объектов SharedParameterElement из документа
                    try
                    {
                        FilteredElementCollector collector = new FilteredElementCollector(doc)
                            .OfClass(typeof(SharedParameterElement));

                        foreach (SharedParameterElement spe in collector.Cast<SharedParameterElement>())
                        {
                            if (paramsToDelete.Any(p => string.Equals(p.Name, spe.Name, StringComparison.OrdinalIgnoreCase) ||
                                                       (p.Guid != Guid.Empty && spe.GuidValue == p.Guid)))
                            {
                                try
                                {
                                    doc.Delete(spe.Id);
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }

                trans.Commit();
            }

            return (deletedCount, failedParams);
        }
    }
}
