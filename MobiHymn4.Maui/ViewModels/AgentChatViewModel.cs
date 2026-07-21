using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using MobiHymn4.Models;
using MobiHymn4.Utils;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using MvvmHelpers;

namespace MobiHymn4.ViewModels
{
    public class AgentChatMessage : ObservableObject
    {
        string displayText = string.Empty;
        bool isTyping;
        bool isSuggestionsExpanded;
        bool showSuggestionsList;
        bool richFormatReady;
        FormattedString formattedDisplay = new();

        public string Role { get; set; }
        public bool IsWelcome { get; set; }
        public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);
        public bool IsAssistant => !IsUser;

        /// <summary>Full reply text (markdown source for API history / after typing).</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>Visible bubble text while typing (plain).</summary>
        public string DisplayText
        {
            get => displayText;
            set
            {
                if (!SetProperty(ref displayText, value))
                    return;
                if (!richFormatReady)
                    RefreshFormatted(Application.Current?.RequestedTheme ?? AppTheme.Unspecified);
            }
        }

        public FormattedString FormattedDisplay
        {
            get => formattedDisplay;
            private set => SetProperty(ref formattedDisplay, value);
        }

        public bool IsTyping
        {
            get => isTyping;
            set
            {
                if (SetProperty(ref isTyping, value))
                    OnPropertyChanged(nameof(ShowTextBubble));
            }
        }

        public bool ShowTextBubble => !IsTyping;

        public ObservableCollection<ShortHymn> Suggestions { get; } = new();

        public bool HasSuggestions => Suggestions.Count > 0;

        public string SuggestionHeaderText =>
            Suggestions.Count == 1 ? "1 hymn" : $"{Suggestions.Count} hymns";

        public string SuggestionChevron => IsSuggestionsExpanded ? "▾" : "▸";

        public bool IsSuggestionsExpanded
        {
            get => isSuggestionsExpanded;
            set
            {
                if (!SetProperty(ref isSuggestionsExpanded, value))
                    return;
                ShowSuggestionsList = value && HasSuggestions;
                OnPropertyChanged(nameof(SuggestionChevron));
            }
        }

        public bool ShowSuggestionsList
        {
            get => showSuggestionsList;
            private set => SetProperty(ref showSuggestionsList, value);
        }

        public AgentChatMessage()
        {
            Suggestions.CollectionChanged += (_, __) =>
            {
                OnPropertyChanged(nameof(HasSuggestions));
                OnPropertyChanged(nameof(SuggestionHeaderText));
                ShowSuggestionsList = IsSuggestionsExpanded && HasSuggestions;
            };
            RefreshFormatted(AppTheme.Unspecified);
        }

        public void FinishRichFormat(AppTheme theme)
        {
            richFormatReady = true;
            displayText = ChatMarkdown.ToPlain(Text);
            OnPropertyChanged(nameof(DisplayText));
            RefreshFormatted(theme);
        }

        public void RefreshFormatted(AppTheme theme)
        {
            var color = ResolveTextColor(theme);
            FormattedDisplay = richFormatReady
                ? ChatMarkdown.ToFormatted(Text, color)
                : ChatMarkdown.ToFormatted(ChatMarkdown.ToPlain(displayText), color);
        }

        Color ResolveTextColor(AppTheme theme)
        {
            if (IsUser)
                return Application.Current?.Resources.TryGetValue("PrimaryText", out var primary) == true
                    && primary is Color pc
                    ? pc
                    : Colors.Black;

            var dark = theme == AppTheme.Dark
                || (theme == AppTheme.Unspecified && Application.Current?.RequestedTheme == AppTheme.Dark);
            if (dark)
                return Colors.White;

            return Application.Current?.Resources.TryGetValue("PrimaryText", out var pt) == true
                && pt is Color c
                ? c
                : Colors.Black;
        }

        public void SetSuggestions(IEnumerable<ShortHymn> items, bool expanded)
        {
            Suggestions.Clear();
            foreach (var item in items ?? Enumerable.Empty<ShortHymn>())
                Suggestions.Add(item);
            IsSuggestionsExpanded = expanded && Suggestions.Count > 0;
        }
    }

    public class AgentChatViewModel : BaseViewModel
    {
        readonly Globals globalInstance = Globals.Instance;
        readonly HashSet<string> suggestedNumbers = new(StringComparer.OrdinalIgnoreCase);
        CancellationTokenSource typeCts;
        string sessionId;
        string draft = string.Empty;

        public ObservableCollection<AgentChatMessage> Messages { get; } = new();

        public string Draft
        {
            get => draft;
            set
            {
                if (SetProperty(ref draft, value))
                    ((Command)SendCommand)?.ChangeCanExecute();
            }
        }

        public ICommand SendCommand { get; }
        public ICommand OpenHymnCommand { get; }
        public ICommand ToggleSuggestionsCommand { get; }

        public AgentChatViewModel()
        {
            Title = "Selah";
            SendCommand = new Command(async () => await SendAsync(), () => !IsBusy && !string.IsNullOrWhiteSpace(Draft));
            OpenHymnCommand = new Command<ShortHymn>(async hymn => await OpenHymnAsync(hymn));
            ToggleSuggestionsCommand = new Command<AgentChatMessage>(ToggleSuggestions);
            Messages.Add(new AgentChatMessage
            {
                Role = "assistant",
                IsWelcome = true,
                Text = "Hi, I'm Selah! — I can help you pick hymns for your gathering.\n\nTell me a bit about the service: theme, season, mood, key, or a Bible idea, and we’ll narrow it together."
            });
            Messages[^1].FinishRichFormat(Application.Current?.RequestedTheme ?? AppTheme.Unspecified);        }

        void ToggleSuggestions(AgentChatMessage message)
        {
            if (message == null || !message.HasSuggestions)
                return;
            message.IsSuggestionsExpanded = !message.IsSuggestionsExpanded;
        }

        void CollapseAllSuggestionLists()
        {
            foreach (var message in Messages)
            {
                if (message.HasSuggestions)
                    message.IsSuggestionsExpanded = false;
            }
        }

        async Task SendAsync()
        {
            var text = (Draft ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text) || IsBusy)
                return;

            if (!HttpHelper.IsConnected())
            {
                Globals.ShowToastPopup(
                    Application.Current?.UserAppTheme == AppTheme.Light ? "no-internet-light" : "no-internet-dark",
                    "Connect to chat with the hymn assistant.");
                return;
            }

            Draft = string.Empty;
            for (var i = Messages.Count - 1; i >= 0; i--)
            {
                if (Messages[i].IsWelcome)
                    Messages.RemoveAt(i);
            }

            // Stop any in-progress typewriter and keep prior hymn lists collapsed.
            typeCts?.Cancel();
            var theme = Application.Current?.RequestedTheme ?? AppTheme.Unspecified;
            foreach (var message in Messages.Where(m => m.IsAssistant && !string.IsNullOrEmpty(m.Text)))
            {
                if (message.DisplayText != ChatMarkdown.ToPlain(message.Text))
                    message.DisplayText = ChatMarkdown.ToPlain(message.Text);
                message.FinishRichFormat(theme);
            }
            CollapseAllSuggestionLists();

            Messages.Add(new AgentChatMessage
            {
                Role = "user",
                Text = text,
                DisplayText = text
            });
            Messages[^1].FinishRichFormat(theme);            IsBusy = true;
            ((Command)SendCommand).ChangeCanExecute();

            var typing = new AgentChatMessage { Role = "assistant", IsTyping = true };
            Messages.Add(typing);

            try
            {
                var payload = Messages
                    .Where(m => !m.IsWelcome && !m.IsTyping && m.Role is "user" or "assistant")
                    .Select(m => new { role = m.Role, content = m.Text })
                    .ToList();

                var http = new HttpHelper();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var response = await http.ChatAgentAsync(
                    payload,
                    cts.Token,
                    sessionId,
                    limit: globalInstance.AgentChatLimit,
                    excludeNumbers: suggestedNumbers).ConfigureAwait(false);
                sessionId = response.SessionId;

                var suggestions = new List<ShortHymn>();
                foreach (var suggestion in response.Suggestions ?? Enumerable.Empty<AgentSearchResult>())
                {
                    if (string.IsNullOrWhiteSpace(suggestion?.Number))
                        continue;
                    var number = suggestion.Number.Trim();
                    suggestedNumbers.Add(number);
                    suggestions.Add(new ShortHymn
                    {
                        Number = number,
                        Line = string.IsNullOrWhiteSpace(suggestion.Title)
                            ? (suggestion.FirstLine ?? $"Hymn #{suggestion.Number}")
                            : suggestion.Title,
                        Reason = suggestion.Reason
                    });
                }

                var reply = string.IsNullOrWhiteSpace(response.Reply)
                    ? "Here are some hymns that may fit."
                    : response.Reply;

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    typing.IsTyping = false;
                    Messages.Remove(typing);
                });

                var bubble = new AgentChatMessage
                {
                    Role = "assistant",
                    Text = reply,
                    DisplayText = string.Empty
                };
                await MainThread.InvokeOnMainThreadAsync(() => Messages.Add(bubble));

                typeCts?.Cancel();
                typeCts = new CancellationTokenSource();
                try
                {
                    await TypeOutAsync(bubble, ChatMarkdown.ToPlain(reply), typeCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                        bubble.DisplayText = ChatMarkdown.ToPlain(reply));
                }

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    bubble.FinishRichFormat(Application.Current?.RequestedTheme ?? AppTheme.Unspecified);
                    bubble.SetSuggestions(suggestions, expanded: true);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Agent chat failed: {ex}");
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    typing.IsTyping = false;
                    Messages.Remove(typing);
                    var err = new AgentChatMessage
                    {
                        Role = "assistant",
                        Text = "Sorry — I couldn’t reach the assistant. Try AI search on the Search page, or try again later."
                    };
                    Messages.Add(err);
                    err.FinishRichFormat(Application.Current?.RequestedTheme ?? AppTheme.Unspecified);
                });
            }
            finally
            {
                IsBusy = false;
                await MainThread.InvokeOnMainThreadAsync(() => ((Command)SendCommand).ChangeCanExecute());
            }
        }

        static async Task TypeOutAsync(AgentChatMessage bubble, string text, CancellationToken token)
        {
            if (string.IsNullOrEmpty(text))
            {
                bubble.DisplayText = string.Empty;
                return;
            }

            // Keep typewriter snappy: reveal a few characters per frame.
            const int charsPerTick = 3;
            const int delayMs = 8;

            for (var i = 1; i <= text.Length; i += charsPerTick)
            {
                token.ThrowIfCancellationRequested();
                var end = Math.Min(i + charsPerTick - 1, text.Length);
                var slice = text[..end];
                await MainThread.InvokeOnMainThreadAsync(() => bubble.DisplayText = slice);
                await Task.Delay(delayMs, token).ConfigureAwait(false);
            }

            await MainThread.InvokeOnMainThreadAsync(() => bubble.DisplayText = text);
        }

        async Task OpenHymnAsync(ShortHymn shortHymn)
        {
            if (shortHymn == null || string.IsNullOrWhiteSpace(shortHymn.Number))
                return;

            var hymn = globalInstance.HymnList?
                .FirstOrDefault(h => string.Equals(h?.Number, shortHymn.Number, StringComparison.OrdinalIgnoreCase));
            if (hymn == null)
            {
                Globals.ShowToastPopup(
                    Application.Current?.UserAppTheme == AppTheme.Light ? "search-light" : "search-dark",
                    $"Hymn #{shortHymn.Number} isn’t on this device yet. Sync the catalog first.");
                return;
            }

            globalInstance.ActiveHymn = hymn;
            await Shell.Current.GoToAsync($"//{Routes.READ}");
        }
    }
}
