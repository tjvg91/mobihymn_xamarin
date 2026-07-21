using Microsoft.Maui.Controls;

namespace MobiHymn4.Utils
{
    public sealed class CatalogBadgeState : BindableObject
    {
        public static CatalogBadgeState Instance { get; } = new();

        private bool showSettingsBadge;
        public bool ShowSettingsBadge
        {
            get => showSettingsBadge;
            set
            {
                if (showSettingsBadge == value)
                    return;

                showSettingsBadge = value;
                OnPropertyChanged();
            }
        }

        private CatalogBadgeState()
        {
        }
    }
}
