using System;
using System.Collections.Generic;
using System.Linq;
using MobiHymn2.Utils;
using Xamarin.Forms;

namespace MobiHymn2.ViewModels
{
	public class SettingsViewModel : MvvmHelpers.BaseViewModel
    {
        private Globals globalInstance = Globals.Instance;

        private bool isDarkMode;
        public bool IsDarkMode
        {
            get => isDarkMode;
            set
            {
                isDarkMode = value;
                SetProperty(ref isDarkMode, value, "IsDarkMode");
            }
        }

        private bool keepAwake;
        public bool KeepAwake
        {
            get => keepAwake;
            set
            {
                keepAwake = value;
                SetProperty(ref keepAwake, value, "KeepAwake");
            }
        }

        private bool isOrientationLocked;
        public bool IsOrientationLocked
        {
            get => isOrientationLocked;
            set
            {
                isOrientationLocked = value;
                SetProperty(ref isOrientationLocked, value, "IsOrientationLocked");
            }
        }

        public SettingsViewModel()
        {
            IsDarkMode = globalInstance.DarkMode;
            KeepAwake = globalInstance.KeepAwake;
            IsOrientationLocked = globalInstance.IsOrientationLocked;

            globalInstance.DarkModeChanged += GlobalInstance_DarkModeChanged;
            globalInstance.KeepAwakeChanged += GlobalInstance_KeepAwakeChanged;
            globalInstance.OrientationLockedChanged += GlobalInstance_OrientationLockedChanged;
        }

        private void GlobalInstance_OrientationLockedChanged(object sender, EventArgs e)
        {
            IsOrientationLocked = (bool)sender;
        }

        private void GlobalInstance_KeepAwakeChanged(object sender, EventArgs e)
        {
            KeepAwake = (bool)sender;
        }

        private void GlobalInstance_DarkModeChanged(object sender, EventArgs e)
        {
            IsDarkMode = (bool)sender;
        }
    }
}

