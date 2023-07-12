﻿
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

using MvvmHelpers;

using Xamarin.Forms;

using MobiHymn4.Utils;
using MobiHymn4.Views;
using System.Windows.Input;
using MobiHymn4.Models;
using Plugin.AudioRecorder;

namespace MobiHymn4.ViewModels
{
    public class ReadViewModel : BaseViewModel
    {
        private Globals globalInstance = Globals.Instance;
        private IPlayService playService;
        private string commandText = "Play";

        public event EventHandler OnBookmarked;

        public ReadViewModel()
        {
            Title = "Hymn #" + globalInstance.ActiveHymn?.Title;
            Lyrics = globalInstance.ActiveHymn?.Lyrics;
            BookmarkFont = globalInstance.IsBookmarked() ? "FAS" : "FAR";

            ActiveColor = globalInstance.ActiveReadTheme;
            ActiveColorText = globalInstance.ActiveThemeText;
            ActiveFontSize = globalInstance.ActiveFontSize;
            ActiveFont = globalInstance.ActiveFont;
            ActiveAlignment = globalInstance.ActiveAlignment;
            HymnInputType = globalInstance.HymnInputType;

            globalInstance.ActiveReadThemeChanged += Globals_ActiveReadThemeChanged;
            globalInstance.ActiveHymnChanged += Globals_ActiveHymnChanged;
            globalInstance.ActiveAlignmentChanged += Globals_ActiveAlignmentChanged;
            globalInstance.ActiveFontSizeChanged += Globals_ActiveFontSizeChanged;
            globalInstance.ActiveFontChanged += Globals_ActiveFontChanged;
            globalInstance.HymnInputTypeChanged += GlobalInstance_HymnInputTypeChanged;
            globalInstance.BookmarksChanged += GlobalInstance_BookmarksChanged;

            LetterSpacing = globalInstance.FontList.Find(f => f.Name == activeFont).CharacterSpacing;
        }

        public ReadViewModel(IPlayService playService) : this()
        {
            this.playService = playService;
            var file = "h592.mid";
            this.playService.Init(file);
        }

        private void GlobalInstance_BookmarksChanged(object sender, EventArgs e)
        {
            BookmarkFont = globalInstance.IsBookmarked() ? "FAS" : "FAR";
        }

        private void GlobalInstance_HymnInputTypeChanged(object sender, EventArgs e)
        {
            HymnInputType = (Utils.InputType)sender;
        }

        private void Globals_ActiveFontChanged(object sender, EventArgs e)
        {
            ActiveFont = (string)sender;
            LetterSpacing = globalInstance.FontList.Find(f => f.Name == ActiveFont).CharacterSpacing;
        }

        private void Globals_ActiveFontSizeChanged(object sender, EventArgs e)
        {
            ActiveFontSize = (double)sender;
        }

        private void Globals_ActiveAlignmentChanged(object sender, EventArgs e)
        {
            ActiveAlignment = (TextAlignment)sender;
        }

        private void Globals_ActiveHymnChanged(object sender, EventArgs e)
        {
            var activeHymn = (Models.Hymn)sender;
            Title = "Hymn #" + activeHymn.Title;
            Lyrics = activeHymn.Lyrics;
            BookmarkFont = globalInstance.IsBookmarked() ? "FAS" : "FAR";
        }

        private ICommand _playPauseCommand;
        public ICommand PlayPauseCommand
        {
            get
            {
                return _playPauseCommand ?? (_playPauseCommand = new Command(
                  (obj) =>
                  {
                      if (commandText == "Play")
                      {
                          playService.Play();
                          commandText = "Pause";
                      }
                      else
                      {
                          playService.Pause();
                          commandText = "Play";
                      }
                  }));
            }
        }

        private string lyrics;
        public string Lyrics
        {
            get { return lyrics; }
            set
            {
                lyrics = value;
                SetProperty(ref lyrics, value, nameof(Lyrics));
                OnPropertyChanged();
            }
        }

        private Utils.InputType hymnInputType;
        public Utils.InputType HymnInputType
        {
            get => hymnInputType;
            set
            {
                hymnInputType = value;
                SetProperty(ref hymnInputType, value, nameof(HymnInputType));
                OnPropertyChanged();
            }
        }

        private TextAlignment activeAlignment;
        public TextAlignment ActiveAlignment
        {
            get => activeAlignment;
            set
            {
                activeAlignment = value;
                SetProperty(ref activeAlignment, value, nameof(ActiveAlignment));
                OnPropertyChanged();
            }
        }

        private Color activeColor;
        public Color ActiveColor
        {
            get => activeColor;
            set
            {
                activeColor = value;
                SetProperty(ref activeColor, value, nameof(ActiveColor));
                OnPropertyChanged();
            }
        }

        private Color activeColorText;
        public Color ActiveColorText
        {
            get => activeColorText;
            set
            {
                activeColorText = value;
                SetProperty(ref activeColorText, value, nameof(ActiveColorText));
                OnPropertyChanged();
            }
        }

        private double activeFontSize;
        public double ActiveFontSize
        {
            get => activeFontSize;
            set
            {
                activeFontSize = value;
                SetProperty(ref activeFontSize, value, nameof(ActiveFontSize));

                TitleContentMargin = Constraint.Constant(value + 25);
                OnPropertyChanged();
            }
        }

        private Constraint titleContentMargin;
        public Constraint TitleContentMargin
        {
            get => titleContentMargin;
            set
            {
                titleContentMargin = value;
                SetProperty(ref titleContentMargin, value, nameof(TitleContentMargin));
                OnPropertyChanged();
            }
        }

        private double letterSpacing;
        public double LetterSpacing
        {
            get => letterSpacing;
            set
            {
                letterSpacing = value;
                SetProperty(ref letterSpacing, value, nameof(LetterSpacing));
                OnPropertyChanged();
            }
        }


        private string activeFont;
        public string ActiveFont
        {
            get => activeFont;
            set
            {
                activeFont = value;
                SetProperty(ref activeFont, value, nameof(ActiveFont));
                OnPropertyChanged();
            }
        }

        private string bookmarkFont = "FAR";
        public string BookmarkFont
        {
            get => bookmarkFont;
            set
            {
                bookmarkFont = value;
                SetProperty(ref bookmarkFont, value, nameof(BookmarkFont));
                OnPropertyChanged();

                if(OnBookmarked != null)
                    OnBookmarked(value, EventArgs.Empty);
            }
        }

        private ICommand fontSelected;
        public ICommand FontSelected
        {
            get
            {
                return fontSelected ?? (fontSelected = new Xamarin.Forms.Command<string>((fontName) =>
                {
                    globalInstance.ActiveFont = fontName;
                }));
            }
        }

        private bool isReadView = true;
        public bool IsReadView
        {
            get => isReadView;
            set
            {
                isReadView = value;
                SetProperty(ref isReadView, value, nameof(IsReadView));
                OnPropertyChanged();
            }
        }

        private bool isEditable = false;
        public bool IsEditable
        {
            get => isEditable;
            set
            {
                isEditable = value;
                SetProperty(ref isEditable, value, nameof(IsEditable));
                OnPropertyChanged();
            }
        }

        private void Globals_ActiveReadThemeChanged(object sender, EventArgs e)
        {
            ActiveColor = (Color)sender;
            ActiveColorText = globalInstance.ActiveThemeText;
        }
    }
}
