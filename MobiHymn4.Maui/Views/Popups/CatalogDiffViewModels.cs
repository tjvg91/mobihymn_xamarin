using System.Collections.Generic;
using System.Windows.Input;
using Microsoft.Maui.Graphics;
using MvvmHelpers;

namespace MobiHymn4.Views.Popups
{
    /// <summary>One card rendered in the diff list: an added/removed number group,
    /// a modified-hymns group, or an informational notice (e.g. "N more not shown").</summary>
    public class DiffSectionVm : BaseViewModel
    {
        public DiffSectionVm() =>
            ToggleCommand = new Command(() => IsExpanded = !IsExpanded);

        public ICommand ToggleCommand { get; }

        public Color AccentColor { get; set; } = Colors.Gray;
        public string KindLabel { get; set; }
        public bool HasKindLabel => !string.IsNullOrEmpty(KindLabel);
        public string Title { get; set; }
        public string CountBadge { get; set; }
        public bool HasCount => !string.IsNullOrEmpty(CountBadge);
        public string Detail { get; set; }
        public bool HasDetail => !string.IsNullOrEmpty(Detail);
        public string Note { get; set; }
        public bool HasNote => !string.IsNullOrEmpty(Note);
        public List<DiffExampleVm> Examples { get; set; } = new();
        public bool HasExamples => Examples.Count > 0;

        bool isExpanded;
        public bool IsExpanded
        {
            get => isExpanded;
            set
            {
                if (SetProperty(ref isExpanded, value))
                {
                    OnPropertyChanged(nameof(Chevron));
                    OnPropertyChanged(nameof(ShowHeaderDetail));
                    OnPropertyChanged(nameof(ShowBodyDetail));
                }
            }
        }

        public string Chevron => IsExpanded ? "\uf077" : "\uf078";
        public bool ShowHeaderDetail => !IsExpanded && HasDetail;
        public bool ShowBodyDetail => IsExpanded && HasDetail;
    }

    public class DiffExampleVm : BaseViewModel
    {
        public DiffExampleVm() =>
            ToggleCommand = new Command(() => IsExpanded = !IsExpanded);

        public ICommand ToggleCommand { get; }

        public string Header { get; set; }
        public bool HasHeader => !string.IsNullOrEmpty(Header);
        public string Preview { get; set; }
        public bool HasPreview => !string.IsNullOrEmpty(Preview);
        public bool ShowPreview => !IsExpanded && HasPreview;
        public List<DiffPropVm> Props { get; set; } = new();

        bool isExpanded;
        public bool IsExpanded
        {
            get => isExpanded;
            set
            {
                if (SetProperty(ref isExpanded, value))
                {
                    OnPropertyChanged(nameof(Chevron));
                    OnPropertyChanged(nameof(ShowPreview));
                }
            }
        }

        public string Chevron => IsExpanded ? "\uf077" : "\uf078";
    }

    public sealed class DiffPropVm
    {
        public string Name { get; set; }
        public bool ShowName { get; set; } = true;
        public string OldText { get; set; }
        public string NewText { get; set; }
        /// <summary>Old value was empty — this property is newly set on the server.</summary>
        public bool IsNew { get; set; }
        /// <summary>New value is empty — this property was removed on the server.</summary>
        public bool IsRemoved { get; set; }
        public bool IsChanged => !IsNew && !IsRemoved;
    }
}
