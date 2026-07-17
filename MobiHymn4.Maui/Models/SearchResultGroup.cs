using System.Collections.ObjectModel;
using MvvmHelpers;

namespace MobiHymn4.Models
{
    public class SearchResultGroup : BaseViewModel
    {
        public string GroupValue { get; set; }
        public string Category { get; set; }
        public ObservableCollection<ShortHymn> Hymns { get; set; } = new();
        public string DisplayTitle => GroupValue;
        public bool ShowCategory =>
            !string.Equals(Category, "Metre", System.StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Category, "Key", System.StringComparison.OrdinalIgnoreCase);
        public string SubtitleText
        {
            get
            {
                var count = $"{Hymns.Count} hymn{(Hymns.Count == 1 ? string.Empty : "s")}";
                return ShowCategory ? $"{Category} · {count}" : count;
            }
        }

        private bool isExpanded;
        public bool IsExpanded
        {
            get => isExpanded;
            set
            {
                if (SetProperty(ref isExpanded, value))
                    OnPropertyChanged(nameof(Chevron));
            }
        }

        public string Chevron => IsExpanded ? "\uf077" : "\uf078";
    }
}
