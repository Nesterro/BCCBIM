using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public class SourceViewItem
    {
        public ElementId ViewId { get; set; }
        public string ViewName { get; set; }
        public ViewType ViewType { get; set; }
        public bool IsTemplate { get; set; }
        public int FilterCount { get; set; }

        public string DisplayName => IsTemplate ? $"[Шаблон] {ViewName} ({FilterCount} фильтр.)" : $"{ViewName} [{ViewType}] ({FilterCount} фильтр.)";

        public override string ToString() => DisplayName;
    }

    public class FilterItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        public ElementId FilterId { get; set; }
        public string FilterName { get; set; }
        public bool IsVisible { get; set; }
        public bool IsHalftone { get; set; }
        public OverrideGraphicSettings GraphicOverrides { get; set; }
        public string OverridesSummary { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class TargetViewItem : INotifyPropertyChanged
    {
        private bool _isSelected = false;

        public ElementId ViewId { get; set; }
        public string ViewName { get; set; }
        public ViewType ViewType { get; set; }
        public bool IsTemplate { get; set; }
        public bool HasTemplateApplied { get; set; }
        public ElementId TemplateId { get; set; }
        public string TemplateName { get; set; }

        public string ViewTypeGroup
        {
            get
            {
                if (IsTemplate) return "Шаблоны видов";
                switch (ViewType)
                {
                    case ViewType.FloorPlan: return "Планы этажей";
                    case ViewType.CeilingPlan: return "Планы потолков";
                    case ViewType.ThreeD: return "3D виды";
                    case ViewType.Section: return "Разрезы";
                    case ViewType.Elevation: return "Фасады";
                    case ViewType.EngineeringPlan: return "Планы несущих конструкций";
                    case ViewType.AreaPlan: return "Планы площадей";
                    case ViewType.DraftingView: return "Чертежные виды";
                    default: return "Прочие виды";
                }
            }
        }

        public string TemplateStatusText => HasTemplateApplied ? $"[Шаблон: {TemplateName}]" : "[Без шаблона]";

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class FilterCopyOptions
    {
        public bool OverwriteExistingFilters { get; set; } = true;
        public bool CopyToAppliedTemplates { get; set; } = true;
        public bool IncludeViewsWithTemplate { get; set; } = true;
        public bool CopyGraphicOverrides { get; set; } = true;
        public bool CopyVisibility { get; set; } = true;
        public bool CopyHalftone { get; set; } = true;
    }
}
