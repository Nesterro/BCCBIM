using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public static class SharedParamParser
    {
        public static List<SharedParamItem> ParseSharedParameterFile(Document doc, string filePath)
        {
            List<SharedParamItem> items = new List<SharedParamItem>();
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return items;

            // 1. Try Revit API DefinitionFile
            DefinitionFile defFile = null;
            string originalFile = doc.Application.SharedParametersFilename;
            try
            {
                doc.Application.SharedParametersFilename = filePath;
                defFile = doc.Application.OpenSharedParameterFile();
            }
            catch { }
            finally
            {
                if (!string.IsNullOrEmpty(originalFile))
                {
                    try { doc.Application.SharedParametersFilename = originalFile; } catch { }
                }
            }

            if (defFile != null)
            {
                foreach (DefinitionGroup group in defFile.Groups)
                {
                    foreach (Definition def in group.Definitions)
                    {
                        if (def is ExternalDefinition extDef)
                        {
                            items.Add(new SharedParamItem
                            {
                                IsSelected = false,
                                Guid = extDef.GUID,
                                Name = extDef.Name,
                                GroupName = group.Name,
                                DataType = GetDataTypeDisplayName(extDef),
                                Description = extDef.Description ?? "",
                                Definition = extDef
                            });
                        }
                    }
                }
            }

            // 2. Fallback text parser if defFile was null or incomplete
            if (items.Count == 0 && File.Exists(filePath))
            {
                items = ParseTxtFileDirectly(filePath);
            }

            return items;
        }

        private static List<SharedParamItem> ParseTxtFileDirectly(string filePath)
        {
            List<SharedParamItem> result = new List<SharedParamItem>();
            Dictionary<int, string> groups = new Dictionary<int, string>();

            string[] lines = File.ReadAllLines(filePath);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith("#") || string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith("GROUP", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length >= 3 && int.TryParse(parts[1], out int id))
                    {
                        groups[id] = parts[2];
                    }
                }
                else if (line.StartsWith("PARAM", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length >= 7)
                    {
                        if (Guid.TryParse(parts[1], out Guid guid))
                        {
                            string name = parts[2];
                            string type = parts[3];
                            int groupNum = int.TryParse(parts[5], out int gId) ? gId : 0;
                            string groupName = groups.ContainsKey(groupNum) ? groups[groupNum] : "Общие";
                            string desc = parts.Length >= 8 ? parts[7] : "";

                            result.Add(new SharedParamItem
                            {
                                IsSelected = false,
                                Guid = guid,
                                Name = name,
                                GroupName = groupName,
                                DataType = type,
                                Description = desc,
                                Definition = null
                            });
                        }
                    }
                }
            }

            return result;
        }

        private static string GetDataTypeDisplayName(ExternalDefinition def)
        {
            try
            {
                ForgeTypeId typeId = def.GetDataType();
                if (typeId != null && !string.IsNullOrEmpty(typeId.TypeId)) return typeId.TypeId;
            }
            catch { }
            return "TEXT";
        }
    }
}
