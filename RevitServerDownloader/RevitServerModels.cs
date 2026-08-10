using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace RevitServerDownloader
{
    // ==========================================
    // WCF JSON Data Contracts for REST API
    // ==========================================

    [DataContract]
    public class ServerProperties
    {
        [DataMember(Name = "ServerName")]
        public string ServerName { get; set; }

        [DataMember(Name = "ServerVersion")]
        public string ServerVersion { get; set; }
    }

    [DataContract]
    public class FolderContents
    {
        [DataMember(Name = "DriveGuid")]
        public string DriveGuid { get; set; }

        [DataMember(Name = "Folders")]
        public List<ServerFolder> Folders { get; set; }

        [DataMember(Name = "Models")]
        public List<ServerModel> Models { get; set; }
    }

    [DataContract]
    public class ServerFolder
    {
        [DataMember(Name = "Name")]
        public string Name { get; set; }

        [DataMember(Name = "Size")]
        public long Size { get; set; }

        [DataMember(Name = "FolderCount")]
        public int FolderCount { get; set; }

        [DataMember(Name = "ModelCount")]
        public int ModelCount { get; set; }
    }

    [DataContract]
    public class ServerModel
    {
        [DataMember(Name = "Name")]
        public string Name { get; set; }

        [DataMember(Name = "Size")]
        public long Size { get; set; }

        [DataMember(Name = "ModelGuid")]
        public string ModelGuid { get; set; }

        [DataMember(Name = "HistoryGuid")]
        public string HistoryGuid { get; set; }
    }

    // ==========================================
    // View Models for WPF Data Binding
    // ==========================================

    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public class FolderViewModel : ViewModelBase
    {
        private string _name;
        private string _serverRelativePath; // pipe-delimited, e.g. "FolderA|FolderB" or empty string for root
        private bool _isExpanded;
        private bool _isLoaded;
        private ObservableCollection<FolderViewModel> _subFolders;

        public FolderViewModel(string name, string serverRelativePath, bool hasSubfolders)
        {
            _name = name;
            _serverRelativePath = serverRelativePath;
            _subFolders = new ObservableCollection<FolderViewModel>();

            if (hasSubfolders)
            {
                // Add dummy subfolder for lazy loading expansion indicator
                _subFolders.Add(null);
            }
        }

        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        public string ServerRelativePath
        {
            get => _serverRelativePath;
            set => SetField(ref _serverRelativePath, value);
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetField(ref _isExpanded, value);
        }

        public bool IsLoaded
        {
            get => _isLoaded;
            set => SetField(ref _isLoaded, value);
        }

        public ObservableCollection<FolderViewModel> SubFolders
        {
            get => _subFolders;
            set => SetField(ref _subFolders, value);
        }
    }

    public class ModelFileViewModel : ViewModelBase
    {
        private string _name;
        private string _folderPath; // pipe-delimited path containing this model, e.g., "FolderA|FolderB"
        private long _sizeBytes;
        private bool _isSelected;

        public ModelFileViewModel(string name, string folderPath, long sizeBytes)
        {
            _name = name;
            _folderPath = folderPath;
            _sizeBytes = sizeBytes;
            _isSelected = false;
        }

        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        public string FolderPath
        {
            get => _folderPath;
            set => SetField(ref _folderPath, value);
        }

        public long SizeBytes
        {
            get => _sizeBytes;
            set => SetField(ref _sizeBytes, value);
        }

        public string SizeFormatted
        {
            get
            {
                if (_sizeBytes <= 0) return "0 Б";
                string[] units = { "Б", "КБ", "МБ", "ГБ" };
                double size = _sizeBytes;
                int unitIdx = 0;
                while (size >= 1024 && unitIdx < units.Length - 1)
                {
                    size /= 1024;
                    unitIdx++;
                }
                return $"{size:F1} {units[unitIdx]}";
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value);
        }
    }
}
