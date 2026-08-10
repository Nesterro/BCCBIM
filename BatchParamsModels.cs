using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    [DataContract]
    public class SharedParamItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _name = "";
        private string _groupName = "";
        private string _dataType = "TEXT";
        private string _description = "";
        private BuiltInParameterGroup _bindingGroup = BuiltInParameterGroup.PG_DATA;
        private bool _isInstance = true;
        private string _defaultValue = "";
        private string _formula = "";
        private List<string> _categoryNames = new List<string>();

        [DataMember]
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        [DataMember]
        public Guid Guid { get; set; } = Guid.NewGuid();

        [DataMember]
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        [DataMember]
        public string GroupName
        {
            get => _groupName;
            set { _groupName = value; OnPropertyChanged(); }
        }

        [DataMember]
        public string DataType
        {
            get => _dataType;
            set
            {
                _dataType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FriendlyDataType));
            }
        }

        public string FriendlyDataType
        {
            get
            {
                if (string.IsNullOrEmpty(_dataType)) return "Текст";
                string raw = _dataType.ToLowerInvariant();
                if (raw.Contains("string") || raw.Contains("text")) return "Текст";
                if (raw.Contains("length")) return "Длина";
                if (raw.Contains("area")) return "Площадь";
                if (raw.Contains("volume")) return "Объем";
                if (raw.Contains("number")) return "Число";
                if (raw.Contains("integer") || raw.Contains("int")) return "Целое";
                if (raw.Contains("boolean") || raw.Contains("bool") || raw.Contains("yesno")) return "Да/Нет";
                if (raw.Contains("angle")) return "Угол";
                if (raw.Contains("currency") || raw.Contains("cost")) return "Денежная сумма";
                if (raw.Contains("mass") || raw.Contains("weight")) return "Масса";
                if (raw.Contains("material")) return "Материал";
                if (raw.Contains("url")) return "URL / Ссылка";
                if (raw.Contains("image")) return "Изображение";
                if (raw.Contains("multiline")) return "Многострочный текст";

                int colonIdx = _dataType.LastIndexOf(':');
                if (colonIdx >= 0 && colonIdx < _dataType.Length - 1)
                {
                    return _dataType.Substring(colonIdx + 1);
                }
                return _dataType;
            }
        }

        [DataMember]
        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        [DataMember]
        public BuiltInParameterGroup BindingGroup
        {
            get => _bindingGroup;
            set
            {
                _bindingGroup = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BindingGroupName));
            }
        }

        public string BindingGroupName
        {
            get => GetGroupName(_bindingGroup);
            set
            {
                _bindingGroup = ParseGroupName(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(BindingGroup));
            }
        }

        [DataMember]
        public bool IsInstance
        {
            get => _isInstance;
            set
            {
                _isInstance = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BindingTypeName));
            }
        }

        public string BindingTypeName
        {
            get => _isInstance ? "Экземпляр" : "Типоразмер";
            set
            {
                _isInstance = string.Equals(value, "Экземпляр", StringComparison.OrdinalIgnoreCase);
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsInstance));
            }
        }

        [DataMember]
        public string DefaultValue
        {
            get => _defaultValue;
            set { _defaultValue = value; OnPropertyChanged(); }
        }

        [DataMember]
        public string Formula
        {
            get => _formula;
            set { _formula = value; OnPropertyChanged(); }
        }

        [DataMember]
        public List<string> CategoryNames
        {
            get => _categoryNames;
            set { _categoryNames = value; OnPropertyChanged(); }
        }

        public bool IsCustomParam { get; set; } = false;

        public ExternalDefinition Definition { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static string GetGroupName(BuiltInParameterGroup g)
        {
            switch (g)
            {
                case BuiltInParameterGroup.PG_DATA: return "Данные";
                case BuiltInParameterGroup.PG_TEXT: return "Текст";
                case BuiltInParameterGroup.PG_IDENTITY_DATA: return "Идентификация";
                case BuiltInParameterGroup.PG_GEOMETRY: return "Размеры";
                case BuiltInParameterGroup.PG_MECHANICAL: return "Механические";
                case BuiltInParameterGroup.PG_ELECTRICAL: return "Электросети";
                case BuiltInParameterGroup.PG_PLUMBING: return "Сантехника";
                case BuiltInParameterGroup.PG_MATERIALS: return "Материалы";
                default: return "Прочее";
            }
        }

        private static BuiltInParameterGroup ParseGroupName(string name)
        {
            if (string.IsNullOrEmpty(name)) return BuiltInParameterGroup.PG_DATA;
            if (name.Contains("Текст")) return BuiltInParameterGroup.PG_TEXT;
            if (name.Contains("Идентификация")) return BuiltInParameterGroup.PG_IDENTITY_DATA;
            if (name.Contains("Размеры")) return BuiltInParameterGroup.PG_GEOMETRY;
            if (name.Contains("Механические")) return BuiltInParameterGroup.PG_MECHANICAL;
            if (name.Contains("Электросети")) return BuiltInParameterGroup.PG_ELECTRICAL;
            if (name.Contains("Сантехника")) return BuiltInParameterGroup.PG_PLUMBING;
            if (name.Contains("Материалы")) return BuiltInParameterGroup.PG_MATERIALS;
            if (name.Contains("Прочее")) return BuiltInParameterGroup.INVALID;
            return BuiltInParameterGroup.PG_DATA;
        }
    }

    public class CategoryItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _name;

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public BuiltInCategory BuiltInCat { get; set; }
        public Category Category { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class BindingGroupItem
    {
        public string Name { get; set; }
        public BuiltInParameterGroup BuiltInGroup { get; set; }
    }

    [DataContract]
    public class BatchParamsPreset
    {
        [DataMember]
        public string Name { get; set; } = "Новый пресет";

        [DataMember]
        public List<SharedParamItem> Items { get; set; } = new List<SharedParamItem>();

        public static void SavePreset(string filePath, BatchParamsPreset preset)
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(BatchParamsPreset));
                ser.WriteObject(fs, preset);
            }
        }

        public static BatchParamsPreset LoadPreset(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(BatchParamsPreset));
                return ser.ReadObject(fs) as BatchParamsPreset;
            }
        }

        public static void ExportToCsv(string filePath, List<SharedParamItem> items)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Имя параметра;GUID;Группа ФОП;Тип данных;Группирование;Экземпляр/Типоразмер;Значение/Формула");

            foreach (var p in items)
            {
                string isInstStr = p.BindingTypeName;
                string val = !string.IsNullOrEmpty(p.Formula) ? p.Formula : p.DefaultValue;
                sb.AppendLine($"\"{p.Name}\";\"{p.Guid}\";\"{p.GroupName}\";\"{p.DataType}\";\"{p.BindingGroupName}\";\"{isInstStr}\";\"{val}\"");
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        public static List<SharedParamItem> ImportFromCsv(string filePath)
        {
            List<SharedParamItem> items = new List<SharedParamItem>();
            if (!File.Exists(filePath)) return items;

            string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                string[] parts = line.Split(';');
                if (parts.Length >= 4)
                {
                    string name = parts[0].Trim('"');
                    Guid guid = Guid.TryParse(parts[1].Trim('"'), out Guid g) ? g : Guid.NewGuid();
                    string group = parts[2].Trim('"');
                    string type = parts[3].Trim('"');

                    string bindingStr = parts.Length >= 6 ? parts[5].Trim('"') : "Экземпляр";
                    bool isInst = !bindingStr.Equals("Типоразмер", StringComparison.OrdinalIgnoreCase);
                    string defVal = parts.Length >= 7 ? parts[6].Trim('"') : "";

                    items.Add(new SharedParamItem
                    {
                        IsSelected = true,
                        Name = name,
                        Guid = guid,
                        GroupName = group,
                        DataType = type,
                        IsInstance = isInst,
                        DefaultValue = defVal,
                        IsCustomParam = true
                    });
                }
            }

            return items;
        }
    }
}
