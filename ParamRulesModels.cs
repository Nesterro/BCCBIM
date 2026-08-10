using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace BCCPlugIn
{
    // =========================================================================
    // LTOOLS ARCHITECTURE: ENUMS
    // =========================================================================
    public enum LToolsActionType
    {
        SetParamValue,  // Запись значения в параметр
        SetWorkset,     // Назначение рабочей наборы (Workset)
        ChangeType      // Смена типа элемента
    }

    public enum LToolsFilterType
    {
        ByCategories,               // По категориям
        ElementIsElementType,       // Типоразмер / Экземпляр
        ByParameterValue,           // По значению параметра экземпляра
        ByTypeParameterValue,       // По значению параметра типа
        ByInsulationParameterValue, // По параметру изоляции
        BySystemParameterValue,     // По параметру системы
        ByParameterExistence,       // По наличию параметра
        ByPhase,                    // По стадии / фазе
        ByHostCategories            // По категории основы
    }

    public enum LToolsMatchType
    {
        Equals,
        Contains,
        StartsWith,
        EndsWith,
        NotEquals,
        GreaterThan,
        LessThan,
        IsEmpty,
        IsNotEmpty
    }

    public enum LToolsFragmentType
    {
        Const,             // Текстовая константа
        Symbol,            // Специальный символ (Ø,°,×...)
        ParameterValue,    // Значение параметра
        Calculator,        // Калькулятор / Математическое выражение
        DuctThickness,     // Толщина стенки воздуховода (ГОСТ 24751-81)
        DuctTapThickness,  // Толщина врезки воздуховода
        MEPSize,           // Сечение / Размеры сетей
        SystemParameter,   // Имя системы (ОВ/ВК)
        WorksetName        // Имя рабочего набора
    }

    public enum LToolsParamGettingMode
    {
        Self,       // Сам элемент
        Type,       // Типоразмер
        Host,       // Основа
        HostType,   // Тип основы
        Parent,     // Родительский элемент
        OwnerView   // Вид-владелец
    }

    // =========================================================================
    // LTOOLS FILTERS MODEL
    // =========================================================================
    public class LToolsFilter
    {
        public LToolsFilterType Type { get; set; } = LToolsFilterType.ByParameterValue;
        public string ParameterName { get; set; } = "Маркировка типоразмера";
        public LToolsMatchType MatchType { get; set; } = LToolsMatchType.Equals;
        public string Value { get; set; } = "";

        public string Description
        {
            get
            {
                switch (Type)
                {
                    case LToolsFilterType.ByCategories: return $"Категория = {Value}";
                    case LToolsFilterType.ElementIsElementType: return $"Типоразмер = {Value}";
                    case LToolsFilterType.ByParameterExistence: return $"Параметр {ParameterName} существует";
                    case LToolsFilterType.ByPhase: return $"Стадия = {Value}";
                    case LToolsFilterType.ByHostCategories: return $"Основа = {Value}";
                    default: return $"{ParameterName} {MatchTypeStr} {Value}";
                }
            }
        }

        private string MatchTypeStr
        {
            get
            {
                switch (MatchType)
                {
                    case LToolsMatchType.Equals: return "=";
                    case LToolsMatchType.Contains: return "содержит";
                    case LToolsMatchType.StartsWith: return "начинается с";
                    case LToolsMatchType.EndsWith: return "заканчивается на";
                    case LToolsMatchType.NotEquals: return "!=";
                    case LToolsMatchType.GreaterThan: return ">";
                    case LToolsMatchType.LessThan: return "<";
                    case LToolsMatchType.IsEmpty: return "пусто";
                    case LToolsMatchType.IsNotEmpty: return "не пусто";
                    default: return "=";
                }
            }
        }
    }

    // =========================================================================
    // LTOOLS FRAGMENTS MODEL
    // =========================================================================
    public class LToolsFragment
    {
        public LToolsFragmentType Type { get; set; } = LToolsFragmentType.Const;
        public string Value { get; set; } = "";
        public LToolsParamGettingMode ParamMode { get; set; } = LToolsParamGettingMode.Self;

        public string DisplayTypeName
        {
            get
            {
                switch (Type)
                {
                    case LToolsFragmentType.ParameterValue: return "Значение параметра";
                    case LToolsFragmentType.Const: return "Константа";
                    case LToolsFragmentType.Symbol: return "Символ";
                    case LToolsFragmentType.Calculator: return "Калькулятор";
                    case LToolsFragmentType.DuctThickness: return "Толщина ГОСТ 24751";
                    case LToolsFragmentType.SystemParameter: return "Имя системы";
                    case LToolsFragmentType.MEPSize: return "Размер сечения";
                    default: return "Текст";
                }
            }
        }

        public string Description => Type == LToolsFragmentType.ParameterValue ? $"{{{Value}}}" : Value;
    }

    // =========================================================================
    // LTOOLS ACTION MODEL
    // =========================================================================
    public class LToolsAction
    {
        public LToolsActionType ActionType { get; set; } = LToolsActionType.SetParamValue;
        public string TargetParameterName { get; set; } = "ADSK_Наименование";
        public string WorksetName { get; set; } = "";
        public List<LToolsFragment> Fragments { get; set; } = new List<LToolsFragment>();
    }

    // =========================================================================
    // LTOOLS RULE MODEL
    // =========================================================================
    public class LToolsRule
    {
        public int Index { get; set; } = 1;
        public bool Enable { get; set; } = true;
        public string Comment { get; set; } = "Новое правило LTools";
        public string CategoryName { get; set; } = "OST_DuctCurves";
        public LToolsAction Action { get; set; } = new LToolsAction();
        public List<LToolsFilter> Filters { get; set; } = new List<LToolsFilter>();

        public string DisplayCategory
        {
            get
            {
                var catFilter = Filters.FirstOrDefault(f => f.Type == LToolsFilterType.ByCategories);
                return catFilter != null ? catFilter.Value : CategoryName;
            }
        }

        public string FiltersSummary
        {
            get
            {
                var nonCatFilters = Filters.Where(f => f.Type != LToolsFilterType.ByCategories).Select(f => f.Description);
                return string.Join("; ", nonCatFilters);
            }
        }
    }

    // =========================================================================
    // LTOOLS XML SERIALIZER (1:1 LTools XML Format Support)
    // =========================================================================
    public static class LToolsXmlSerializer
    {
        public static List<LToolsRule> LoadFromXml(string filePath)
        {
            List<LToolsRule> rules = new List<LToolsRule>();
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return rules;

            XDocument doc = XDocument.Load(filePath);
            XElement root = doc.Root;
            if (root == null) return rules;

            var ruleElements = root.DescendantsAndSelf()
                .Where(e => string.Equals(e.Name.LocalName, "rule", StringComparison.OrdinalIgnoreCase));

            int indexCounter = 1;
            foreach (XElement rElem in ruleElements)
            {
                string comment = rElem.Element("comment")?.Value
                              ?? rElem.Attribute("comment")?.Value
                              ?? rElem.Attribute("name")?.Value
                              ?? $"Правило {indexCounter}";

                bool enable = true;
                XAttribute enableAttr = rElem.Attribute("enable") ?? rElem.Attribute("IsEnabled");
                if (enableAttr != null && string.Equals(enableAttr.Value, "false", StringComparison.OrdinalIgnoreCase))
                {
                    enable = false;
                }

                LToolsRule rule = new LToolsRule
                {
                    Index = indexCounter++,
                    Enable = enable,
                    Comment = comment
                };

                // Action
                XElement actionElem = rElem.Element("action");
                if (actionElem != null)
                {
                    string targetParam = actionElem.Element("paramId")?.Element("name")?.Value
                                      ?? actionElem.Attribute("targetParam")?.Value
                                      ?? "ADSK_Наименование";
                    rule.Action.TargetParameterName = targetParam.Trim();

                    // Fragments
                    var fragNodes = actionElem.Element("fragments")?.Elements("fragment");
                    if (fragNodes != null)
                    {
                        foreach (var fragElem in fragNodes)
                        {
                            string fTypeStr = fragElem.Attribute("type")?.Value ?? "Const";
                            LToolsFragment frag = ParseFragment(fTypeStr, fragElem);
                            if (frag != null) rule.Action.Fragments.Add(frag);
                        }
                    }
                }

                if (rule.Action.Fragments.Count == 0)
                {
                    rule.Action.Fragments.Add(new LToolsFragment { Type = LToolsFragmentType.Const, Value = "" });
                }

                // Filters
                var filterNodes = rElem.Element("filters")?.Elements("filter");
                if (filterNodes != null)
                {
                    foreach (var fElem in filterNodes)
                    {
                        string fTypeStr = fElem.Attribute("filterType")?.Value ?? "ByParameterValue";
                        LToolsFilter filter = ParseFilter(fTypeStr, fElem);
                        if (filter != null) rule.Filters.Add(filter);
                    }
                }

                // Ensure category filter exists
                var catFilter = rule.Filters.FirstOrDefault(f => f.Type == LToolsFilterType.ByCategories);
                if (catFilter != null)
                {
                    rule.CategoryName = catFilter.Value;
                }
                else
                {
                    rule.Filters.Insert(0, new LToolsFilter { Type = LToolsFilterType.ByCategories, Value = rule.CategoryName });
                }

                rules.Add(rule);
            }

            return rules;
        }

        public static void SaveToXml(string filePath, List<LToolsRule> rules)
        {
            XElement root = new XElement("rules",
                rules.Select((r, idx) => new XElement("rule",
                    new XAttribute("index", idx + 1),
                    new XAttribute("enable", r.Enable.ToString().ToLower()),
                    new XElement("comment", r.Comment),
                    new XElement("action",
                        new XAttribute("actionType", r.Action.ActionType.ToString()),
                        new XElement("paramId", new XElement("name", r.Action.TargetParameterName)),
                        new XElement("fragments",
                            r.Action.Fragments.Select(f => new XElement("fragment",
                                new XAttribute("type", f.Type.ToString()),
                                f.Type == LToolsFragmentType.ParameterValue
                                    ? new XElement("paramId", new XElement("name", f.Value))
                                    : new XElement("const", EscapeXmlText(f.Value))
                            ))
                        )
                    ),
                    new XElement("filters",
                        r.Filters.Select(f => CreateFilterXml(f))
                    )
                ))
            );

            XDocument doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
            doc.Save(filePath);
        }

        private static LToolsFragment ParseFragment(string fTypeStr, XElement fragElem)
        {
            fTypeStr = fTypeStr.ToLowerInvariant();
            if (fTypeStr.Contains("parametervalue") || fTypeStr.Contains("param"))
            {
                string pName = fragElem.Element("paramId")?.Element("name")?.Value ?? fragElem.Value;
                return new LToolsFragment { Type = LToolsFragmentType.ParameterValue, Value = pName?.Trim() ?? "" };
            }
            if (fTypeStr.Contains("symbol"))
            {
                string sym = fragElem.Element("symbol")?.Value ?? fragElem.Value;
                return new LToolsFragment { Type = LToolsFragmentType.Symbol, Value = sym?.Trim() ?? "Ø" };
            }
            if (fTypeStr.Contains("ductthickness") || fTypeStr.Contains("thickness"))
            {
                return new LToolsFragment { Type = LToolsFragmentType.DuctThickness, Value = "ГОСТ 24751-81" };
            }
            if (fTypeStr.Contains("calculator") || fTypeStr.Contains("calc"))
            {
                string arg1 = fragElem.Element("arg1")?.Element("fragment")?.Element("paramId")?.Element("name")?.Value ?? "Параметр1";
                string opArg = fragElem.Element("operations")?.Element("operation")?.Element("argument")?.Element("fragment")?.Element("paramId")?.Element("name")?.Value ?? "Параметр2";
                return new LToolsFragment { Type = LToolsFragmentType.Calculator, Value = $"{arg1} - {opArg}" };
            }

            string constVal = fragElem.Element("const")?.Value ?? fragElem.Value;
            return new LToolsFragment { Type = LToolsFragmentType.Const, Value = UnescapeXmlText(constVal) };
        }

        private static LToolsFilter ParseFilter(string fTypeStr, XElement fElem)
        {
            if (fTypeStr.Equals("ByCategories", StringComparison.OrdinalIgnoreCase))
            {
                string cat = fElem.Element("categories")?.Element("category")?.Value ?? "OST_DuctCurves";
                return new LToolsFilter { Type = LToolsFilterType.ByCategories, Value = cat.Trim() };
            }
            if (fTypeStr.Equals("ElementIsElementType", StringComparison.OrdinalIgnoreCase))
            {
                return new LToolsFilter { Type = LToolsFilterType.ElementIsElementType, ParameterName = "Типоразмер", MatchType = LToolsMatchType.NotEquals, Value = "Типоразмер" };
            }

            LToolsFilterType fType = MapFilterType(fTypeStr);
            string pName = fElem.Element("paramId")?.Element("name")?.Value ?? fElem.Element("paramName")?.Value ?? "";
            string matchTypeStr = fElem.Element("matchType")?.Value ?? "Equals";
            string valStr = UnescapeXmlText(fElem.Element("value")?.Value ?? "");

            return new LToolsFilter
            {
                Type = fType,
                ParameterName = pName.Trim(),
                MatchType = MapMatchType(matchTypeStr),
                Value = valStr
            };
        }

        private static XElement CreateFilterXml(LToolsFilter f)
        {
            XElement elem = new XElement("filter", new XAttribute("filterType", f.Type.ToString()));
            if (f.Type == LToolsFilterType.ByCategories)
            {
                elem.Add(new XElement("categories", new XElement("category", f.Value)));
            }
            else
            {
                elem.Add(new XElement("paramId", new XElement("name", f.ParameterName)));
                elem.Add(new XElement("matchType", f.MatchType.ToString()));
                elem.Add(new XElement("value", EscapeXmlText(f.Value)));
            }
            return elem;
        }

        private static LToolsFilterType MapFilterType(string str)
        {
            if (str.Equals("ByTypeParameterValue", StringComparison.OrdinalIgnoreCase)) return LToolsFilterType.ByTypeParameterValue;
            if (str.Equals("ByInsulationParameterValue", StringComparison.OrdinalIgnoreCase)) return LToolsFilterType.ByInsulationParameterValue;
            if (str.Equals("BySystemParameterValue", StringComparison.OrdinalIgnoreCase)) return LToolsFilterType.BySystemParameterValue;
            if (str.Equals("ByParameterExistence", StringComparison.OrdinalIgnoreCase)) return LToolsFilterType.ByParameterExistence;
            if (str.Equals("ByPhase", StringComparison.OrdinalIgnoreCase)) return LToolsFilterType.ByPhase;
            if (str.Equals("ByHostCategories", StringComparison.OrdinalIgnoreCase)) return LToolsFilterType.ByHostCategories;
            return LToolsFilterType.ByParameterValue;
        }

        private static LToolsMatchType MapMatchType(string str)
        {
            if (str.Equals("Contains", StringComparison.OrdinalIgnoreCase)) return LToolsMatchType.Contains;
            if (str.Equals("StartsWith", StringComparison.OrdinalIgnoreCase)) return LToolsMatchType.StartsWith;
            if (str.Equals("EndsWith", StringComparison.OrdinalIgnoreCase)) return LToolsMatchType.EndsWith;
            if (str.Equals("NotEquals", StringComparison.OrdinalIgnoreCase)) return LToolsMatchType.NotEquals;
            if (str.Equals("GreaterThan", StringComparison.OrdinalIgnoreCase)) return LToolsMatchType.GreaterThan;
            if (str.Equals("LessThan", StringComparison.OrdinalIgnoreCase)) return LToolsMatchType.LessThan;
            if (str.Equals("IsEmpty", StringComparison.OrdinalIgnoreCase)) return LToolsMatchType.IsEmpty;
            if (str.Equals("IsNotEmpty", StringComparison.OrdinalIgnoreCase)) return LToolsMatchType.IsNotEmpty;
            return LToolsMatchType.Equals;
        }

        private static string UnescapeXmlText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("_x0020_", " ");
        }

        private static string EscapeXmlText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text == " ") return "_x0020_";
            return text;
        }
    }
}
