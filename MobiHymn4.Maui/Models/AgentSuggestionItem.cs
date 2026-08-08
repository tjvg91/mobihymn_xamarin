using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using MvvmHelpers;

namespace MobiHymn4.Models
{
    /// <summary>Selah suggestion row with live bookmark star state.</summary>
    public class AgentSuggestionItem : ObservableObject
    {
        bool isBookmarked;

        public string Number { get; set; }
        public string Line { get; set; }
        public string Reason { get; set; }

        public string NumberBadge => "#" + Number;
        public bool HasReason => !string.IsNullOrWhiteSpace(Reason);

        public bool IsBookmarked
        {
            get => isBookmarked;
            set
            {
                if (!SetProperty(ref isBookmarked, value))
                    return;
                OnPropertyChanged(nameof(StarFont));
                OnPropertyChanged(nameof(StarColor));
            }
        }

        /// <summary>FAR outline when empty; FAS filled when bookmarked.</summary>
        public string StarFont => IsBookmarked ? "FAS" : "FAR";

        public Color StarColor =>
            Application.Current?.Resources.TryGetValue("Primary", out var primary) == true
            && primary is Color c
                ? c
                : Color.FromArgb("#F5D200");

        public ShortHymn ToShortHymn() => new()
        {
            Number = Number,
            Line = Line ?? string.Empty
        };
    }
}
