using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FontAwesome;
using MobiHymn4.Models;
using MobiHymn4.Services;
using MobiHymn4.Utils;
using MobiHymn4.Views;
using MobiHymn4.Views.Popups;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Platform;

namespace MobiHymn4.Elements;

enum GroupDashboardView
{
    HymnLists,
    HymnListDetail,
    Members,
}

public partial class GroupDashboardPane : ContentView
{
    readonly IAuthService auth = ServiceHelper.Get<IAuthService>();
    readonly IProfileService profileService = ServiceHelper.Get<IProfileService>();
    readonly IGroupService groupService = ServiceHelper.Get<IGroupService>();
    readonly IBoardService boardService = ServiceHelper.Get<IBoardService>();
    readonly BoardContext boardContext = ServiceHelper.Get<BoardContext>();
    readonly IGroupDashboardService dashboardService = ServiceHelper.Get<IGroupDashboardService>();
    readonly IBoardNotificationService boardNotificationService = ServiceHelper.Get<IBoardNotificationService>();
    readonly BoardNavigationContext boardNavigation = ServiceHelper.Get<BoardNavigationContext>();

    readonly ObservableCollection<BoardHymnEntry> hymns = new();
    readonly ObservableCollection<BoardHymnEntry> displayHymns = new();
    readonly ObservableCollection<GroupHymnListSummary> hymnLists = new();
    bool hymnListsHasMore;
    bool hymnListsLoadingMore;
    readonly ObservableCollection<HymnSuggestion> hymnSuggestions = new();
    readonly ObservableCollection<GroupMemberDisplayItem> displayMembers = new();
    readonly ObservableCollection<GroupMemberRoleSection> memberSections = new();
    readonly List<GroupMember> groupMembers = new();
    readonly Dictionary<string, Border> memberFilterChipBorders = new(StringComparer.Ordinal);
    readonly Dictionary<string, Label> memberFilterChipLabels = new(StringComparer.Ordinal);
    const string MemberFilterAllKey = "all";
    IDisposable boardSubscription;
    WorshipGroup activeGroup;
    GroupHymnListSummary activeListSummary;
    string subscribedGroupId;
    string subscribedListId;
    bool viewingListDetail;
    GroupDashboardView currentView = GroupDashboardView.HymnLists;
    UserRole? memberRoleFilter;
    bool membersGroupByRole;
    string memberSearchQuery = string.Empty;
    bool canEdit;
    bool hasGroups;
    bool suppressHymnTextChanged;
    bool addHymnDropdownWasShown;
    bool addHymnPickedFromDropdown;
    CancellationTokenSource suggestionCts;
    CancellationTokenSource editSuggestionCts;
    BoardHymnEntry editingEntry;
    bool suppressEditTextChanged;
    BoardHymnEntry dragEntry;
    bool suppressBoardReorder;
    bool addHymnPanelExpanded;
    bool addSectionPanelExpanded;
    bool addFabMenuExpanded;
    bool hymnListLoading;
    bool keepAddHymnPanelAfterLoad;
    Dictionary<string, Hymn> hymnNumberLookup;
    int hymnNumberLookupCount;
    List<string> savedSectionNamesOrdered = new();
    bool savedSectionsAutoApply;
    SectionSortMode sectionSortMode = SectionSortMode.AddedOrder;
    HashSet<string> savedSectionNames = new(StringComparer.OrdinalIgnoreCase);
    readonly SemaphoreSlim loadBoardLock = new(1, 1);
    int loadBoardVersion;
    int boardLoadingCount;
    bool addFabAnimating;
    BoardSectionTemplate activeSectionTemplate;
    bool suppressBoardContextReload;
    DateTime newHymnBaselineUtc = DateTime.MaxValue;
    List<BoardDeletedHymnInfo> sessionDeletedHymns = new();
    const uint AnimMs = 220;
    const uint FabAnimMs = 220;
    const int SuggestionDebounceMs = 180;

    public GroupDashboardPane()
    {
        InitializeComponent();
        hymnList.ItemsSource = displayHymns;
        hymnListsCollection.ItemsSource = hymnLists;
        hymnSuggestionsList.ItemsSource = hymnSuggestions;
        membersCollection.ItemsSource = displayMembers;
        InitializeMemberFilters();
        dashboardService.IsOpenChanged += (_, _) => _ = SyncOpenStateAsync();
        dashboardService.UnreadListsChanged += (_, _) => MainThread.BeginInvokeOnMainThread(ApplyUnreadBadgesToLists);
        auth.AuthStateChanged += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshState);
        profileService.ProfileChanged += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshState);
        boardContext.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(() => _ = OnBoardContextChangedAsync());
        Globals.Instance.InitFinished += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshHymnFirstLines);
        Globals.Instance.SettingsLoaded += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshHymnFirstLines);
    }

    public async Task SyncOpenStateAsync()
    {
        if (dashboardService.IsOpen)
            await OpenAnimatedAsync();
        else
            await CloseAnimatedAsync();
    }

    async Task OpenAnimatedAsync()
    {
        IsVisible = true;
        InputTransparent = false;
        dimmer.InputTransparent = false;
        dimmer.Opacity = 0;
        RefreshState();
        var loadTask = LoadBoardAsync();
        await Task.WhenAll(
            dimmer.FadeTo(1, AnimMs, Easing.CubicOut),
            pane.TranslateTo(0, 0, AnimMs, Easing.CubicOut));
        await loadTask;
    }

    async Task CloseAnimatedAsync()
    {
        MarkActiveListViewed();
        await Task.WhenAll(
            dimmer.FadeTo(0, AnimMs, Easing.CubicIn),
            pane.TranslateTo(360, 0, AnimMs, Easing.CubicIn));
        dimmer.InputTransparent = true;
        IsVisible = false;
        InputTransparent = true;
        boardSubscription?.Dispose();
        boardSubscription = null;
        subscribedGroupId = null;
        subscribedListId = null;
        viewingListDetail = false;
        currentView = GroupDashboardView.HymnLists;
        memberRoleFilter = null;
        membersGroupByRole = false;
        ResetMemberSearch();
        groupMembers.Clear();
        activeListSummary = null;
        boardLoadingCount = 0;
        SetBoardLoading(false);
        addHymnPanelExpanded = false;
        addSectionPanelExpanded = false;
        addFabMenuExpanded = false;
        hymnListLoading = false;
        keepAddHymnPanelAfterLoad = false;
        SetAddFabMenuExpanded(false);
        SetAddHymnPanelExpanded(false, animate: false);
        SetAddSectionPanelExpanded(false, animate: false);
        SetAddPanelsLayout();
        findGroupPanel.IsVisible = false;
        ShowNoGroupsStatus(null);
        ClearHymnEntry();
        if (entSectionName != null)
            entSectionName.Text = string.Empty;
    }

    void RefreshState() => UpdateLayout();

    void UpdateLayout()
    {
        var signedIn = auth.IsSignedIn && auth.IsEmailVerified && profileService.HasCompleteProfile;
        signedOutPanel.IsVisible = !signedIn;

        if (!signedIn)
        {
            noGroupsPanel.IsVisible = false;
            headerRow.IsVisible = false;
            boardContent.IsVisible = false;
            hymnList.IsVisible = false;
            if (addFabStack != null)
                addFabStack.IsVisible = false;
            if (addRow != null)
                addRow.IsVisible = false;
            if (addSectionRow != null)
                addSectionRow.IsVisible = false;
            if (listsOverview != null)
                listsOverview.IsVisible = false;
            if (listDetail != null)
                listDetail.IsVisible = false;
            if (membersView != null)
                membersView.IsVisible = false;
            if (btnBoardOptions != null)
                btnBoardOptions.IsVisible = false;
            if (btnGroupMenu != null)
                btnGroupMenu.IsVisible = false;
            SetAddPanelsLayout();
            SetBoardLoading(false);
            return;
        }

        noGroupsPanel.IsVisible = !hasGroups;
        headerRow.IsVisible = hasGroups;
        boardContent.IsVisible = hasGroups;
        UpdateBoardViewMode();

        var boardReady = boardLoadingCount == 0;
        hymnList.IsVisible = hasGroups && boardReady && currentView == GroupDashboardView.HymnListDetail;

        var canLead = RolePermissions.HasLeadershipRole(profileService.CurrentProfile?.Roles);
        btnCreateGroup.IsVisible = !hasGroups && canLead;
        canEdit = hasGroups && canLead;
        if (btnCreateHymnList != null)
            btnCreateHymnList.IsVisible = canEdit && boardReady && currentView == GroupDashboardView.HymnLists;
        if (btnGroupMenu != null)
            btnGroupMenu.IsVisible = hasGroups && currentView != GroupDashboardView.HymnListDetail;
        if (btnBoardOptions != null)
            btnBoardOptions.IsVisible = canEdit && currentView == GroupDashboardView.HymnListDetail;
        ApplyHymnListManageFlags();
        UpdateAddPanelsExpansion();
        RefreshSectionPresentation();
    }

    void SetAddFabMenuExpanded(bool expanded, bool animate = false)
    {
        if (animate)
        {
            _ = SetAddFabMenuExpandedAsync(expanded);
            return;
        }

        addFabMenuExpanded = expanded;
        AbortFabMenuAnimations();
        ApplyFabMenuVisualState(expanded, visible: expanded);
        if (imgAddMenuFab != null)
            imgAddMenuFab.Glyph = expanded ? FontAwesomeIcons.Xmark : FontAwesomeIcons.Plus;
        if (btnAddMenu != null)
        {
            btnAddMenu.Scale = 1;
            btnAddMenu.Rotation = 0;
        }
    }

    async Task SetAddFabMenuExpandedAsync(bool expanded)
    {
        if (addFabAnimating || expanded == addFabMenuExpanded)
            return;

        addFabAnimating = true;
        addFabMenuExpanded = expanded;

        try
        {
            if (expanded)
                await ExpandFabMenuAsync();
            else
                await CollapseFabMenuAsync();
        }
        finally
        {
            addFabAnimating = false;
        }
    }

    void AbortFabMenuAnimations()
    {
        btnAddMenu?.AbortAnimation("ScaleTo");
        btnAddMenu?.AbortAnimation("RotateTo");
        fabAddHymnRow?.AbortAnimation("FadeTo");
        fabAddHymnRow?.AbortAnimation("ScaleTo");
        fabAddHymnRow?.AbortAnimation("TranslateTo");
        fabAddSectionRow?.AbortAnimation("FadeTo");
        fabAddSectionRow?.AbortAnimation("ScaleTo");
        fabAddSectionRow?.AbortAnimation("TranslateTo");
    }

    void ApplyFabMenuVisualState(bool expanded, bool visible)
    {
        if (fabAddHymnRow != null)
        {
            fabAddHymnRow.IsVisible = visible;
            fabAddHymnRow.Opacity = expanded ? 1 : 0;
            fabAddHymnRow.Scale = expanded ? 1 : 0.5;
            fabAddHymnRow.TranslationY = expanded ? 0 : 20;
        }

        if (fabAddSectionRow != null)
        {
            fabAddSectionRow.IsVisible = visible;
            fabAddSectionRow.Opacity = expanded ? 1 : 0;
            fabAddSectionRow.Scale = expanded ? 1 : 0.5;
            fabAddSectionRow.TranslationY = expanded ? 0 : 20;
        }
    }

    async Task ExpandFabMenuAsync()
    {
        if (btnAddMenu == null || fabAddHymnRow == null || fabAddSectionRow == null)
            return;

        ApplyFabMenuVisualState(expanded: true, visible: true);

        var iconSwap = AnimateFabMenuIconAsync(FontAwesomeIcons.Xmark);
        var children = Task.WhenAll(
            fabAddSectionRow.FadeTo(1, FabAnimMs, Easing.CubicOut),
            fabAddSectionRow.ScaleTo(1, FabAnimMs, Easing.CubicOut),
            fabAddSectionRow.TranslateTo(0, 0, FabAnimMs, Easing.CubicOut),
            fabAddHymnRow.FadeTo(1, FabAnimMs, Easing.CubicOut),
            fabAddHymnRow.ScaleTo(1, FabAnimMs, Easing.CubicOut),
            fabAddHymnRow.TranslateTo(0, 0, FabAnimMs, Easing.CubicOut));

        await Task.WhenAll(iconSwap, children);
    }

    async Task CollapseFabMenuAsync()
    {
        if (btnAddMenu == null || fabAddHymnRow == null || fabAddSectionRow == null)
            return;

        var iconSwap = AnimateFabMenuIconAsync(FontAwesomeIcons.Plus);
        var children = Task.WhenAll(
            fabAddSectionRow.FadeTo(0, FabAnimMs, Easing.CubicIn),
            fabAddSectionRow.ScaleTo(0.5, FabAnimMs, Easing.CubicIn),
            fabAddSectionRow.TranslateTo(0, 20, FabAnimMs, Easing.CubicIn),
            fabAddHymnRow.FadeTo(0, FabAnimMs, Easing.CubicIn),
            fabAddHymnRow.ScaleTo(0.5, FabAnimMs, Easing.CubicIn),
            fabAddHymnRow.TranslateTo(0, 20, FabAnimMs, Easing.CubicIn));

        await Task.WhenAll(iconSwap, children);
        ApplyFabMenuVisualState(expanded: false, visible: false);
    }

    async Task AnimateFabMenuIconAsync(string glyph)
    {
        if (btnAddMenu == null)
            return;

        await btnAddMenu.ScaleTo(0.82, FabAnimMs / 2, Easing.CubicIn);
        if (imgAddMenuFab != null)
            imgAddMenuFab.Glyph = glyph;
        await btnAddMenu.ScaleTo(1, FabAnimMs / 2, Easing.CubicOut);
    }

    void SetAddPanelsLayout()
    {
        var showFab = canEdit
            && currentView == GroupDashboardView.HymnListDetail
            && !addHymnPanelExpanded
            && !addSectionPanelExpanded;
        if (addFabStack != null)
            addFabStack.IsVisible = showFab;
    }

    void UpdateAddPanelsExpansion()
    {
        if (!canEdit)
        {
            SetAddHymnPanelExpanded(false);
            SetAddSectionPanelExpanded(false);
            return;
        }

        if (keepAddHymnPanelAfterLoad)
        {
            keepAddHymnPanelAfterLoad = false;
            SetAddHymnPanelExpanded(true, animate: false);
            return;
        }

        if (hymnListLoading)
        {
            SetAddPanelsLayout();
            return;
        }

        if (!hymns.Any(h => h.IsHymn) && !addSectionPanelExpanded)
            SetAddHymnPanelExpanded(true, animate: true);
        else
            SetAddPanelsLayout();
    }

    void SetAddHymnPanelExpanded(bool expanded, bool animate = false)
    {
        if (expanded && addSectionPanelExpanded)
        {
            addSectionPanelExpanded = false;
            if (entSectionName != null)
                entSectionName.Text = string.Empty;
            _ = AnimateAddPanelAsync(addSectionRow, false, animate);
        }

        addHymnPanelExpanded = expanded;
        if (expanded)
            SetAddFabMenuExpanded(false, animate: animate && addFabMenuExpanded);
        else if (!addSectionPanelExpanded)
            SetAddFabMenuExpanded(false);

        if (!expanded)
            ClearHymnEntry();
        SetAddPanelsLayout();
        _ = AnimateAddPanelAsync(addRow, expanded, animate);
    }

    void SetAddSectionPanelExpanded(bool expanded, bool animate = false)
    {
        if (expanded && addHymnPanelExpanded)
        {
            addHymnPanelExpanded = false;
            ClearHymnEntry();
            _ = AnimateAddPanelAsync(addRow, false, animate);
        }

        addSectionPanelExpanded = expanded;
        if (expanded)
            SetAddFabMenuExpanded(false, animate: animate && addFabMenuExpanded);
        else if (!addHymnPanelExpanded)
            SetAddFabMenuExpanded(false);

        if (!expanded && entSectionName != null)
            entSectionName.Text = string.Empty;
        SetAddPanelsLayout();
        _ = AnimateAddPanelAsync(addSectionRow, expanded, animate);
    }

    async Task AnimateAddPanelAsync(Border panel, bool expanded, bool animate)
    {
        if (panel == null)
            return;

        panel.AbortAnimation("FadeTo");
        panel.AbortAnimation("TranslateTo");

        if (expanded)
        {
            panel.IsVisible = true;
            if (!animate)
            {
                panel.Opacity = 1;
                panel.TranslationY = 0;
                return;
            }

            panel.Opacity = 0;
            panel.TranslationY = 28;
            await Task.WhenAll(
                panel.FadeTo(1, AnimMs, Easing.CubicOut),
                panel.TranslateTo(0, 0, AnimMs, Easing.CubicOut));
            return;
        }

        if (!panel.IsVisible)
            return;

        if (!animate)
        {
            panel.IsVisible = false;
            panel.Opacity = 1;
            panel.TranslationY = 0;
            return;
        }

        await Task.WhenAll(
            panel.FadeTo(0, AnimMs, Easing.CubicIn),
            panel.TranslateTo(0, 28, AnimMs, Easing.CubicIn));
        panel.IsVisible = false;
        panel.Opacity = 1;
        panel.TranslationY = 0;
    }

    async void AddMenuFab_Tapped(object sender, EventArgs e)
    {
        if (addFabAnimating)
            return;

        await SetAddFabMenuExpandedAsync(!addFabMenuExpanded);
    }

    async void AddHymnFab_Tapped(object sender, EventArgs e)
    {
        if (addFabAnimating)
            return;

        if (addFabMenuExpanded)
            await SetAddFabMenuExpandedAsync(false);
        SetAddHymnPanelExpanded(true, animate: true);
    }

    async void AddSectionFab_Tapped(object sender, EventArgs e)
    {
        if (addFabAnimating)
            return;

        if (addFabMenuExpanded)
            await SetAddFabMenuExpandedAsync(false);
        SetAddSectionPanelExpanded(true, animate: true);
    }

    void CloseAddHymnPanel_Tapped(object sender, EventArgs e) =>
        SetAddHymnPanelExpanded(false, animate: true);

    void CloseAddSectionPanel_Tapped(object sender, EventArgs e) =>
        SetAddSectionPanelExpanded(false, animate: true);

    void SetBoardLoading(bool loading)
    {
        if (loading)
            boardLoadingCount++;
        else if (boardLoadingCount > 0)
            boardLoadingCount--;

        var show = boardLoadingCount > 0;
        if (boardLoader != null)
            boardLoader.IsVisible = show;
        if (lblBoardLoader != null)
        {
            lblBoardLoader.Text = currentView == GroupDashboardView.HymnListDetail
                ? "Loading hymns..."
                : "Loading board...";
        }
        if (addFabStack != null)
            addFabStack.IsVisible = !show && canEdit && currentView == GroupDashboardView.HymnListDetail
                && !addHymnPanelExpanded && !addSectionPanelExpanded;
        if (addRow != null)
            addRow.IsVisible = !show && canEdit && addHymnPanelExpanded;
        if (addSectionRow != null)
            addSectionRow.IsVisible = !show && canEdit && addSectionPanelExpanded;
        if (hymnList != null)
            hymnList.IsVisible = !show && currentView == GroupDashboardView.HymnListDetail;
        if (listsOverview != null)
            listsOverview.IsVisible = !show && currentView == GroupDashboardView.HymnLists;
        if (listDetail != null)
            listDetail.IsVisible = !show && currentView == GroupDashboardView.HymnListDetail;
        if (membersView != null)
            membersView.IsVisible = !show && currentView == GroupDashboardView.Members;
        if (btnCreateHymnList != null)
            btnCreateHymnList.IsVisible = !show && canEdit && currentView == GroupDashboardView.HymnLists;
    }

    void UpdateBoardViewMode()
    {
        var loading = boardLoadingCount > 0;
        if (listsOverview != null)
            listsOverview.IsVisible = !loading && currentView == GroupDashboardView.HymnLists;
        if (listDetail != null)
            listDetail.IsVisible = !loading && currentView == GroupDashboardView.HymnListDetail;
        if (membersView != null)
            membersView.IsVisible = !loading && currentView == GroupDashboardView.Members;
        if (lblActiveListName != null)
            lblActiveListName.Text = activeListSummary?.Name ?? string.Empty;
    }

    async Task OnBoardContextChangedAsync()
    {
        if (suppressBoardContextReload)
            return;

        if (!auth.IsSignedIn || !profileService.HasCompleteProfile)
            return;

        if (activeGroup != null
            && hasGroups
            && string.Equals(activeGroup.Id, boardContext.ActiveGroupId, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(boardContext.ActiveListId))
            {
                if (currentView == GroupDashboardView.HymnListDetail)
                    ShowHymnListsView();
                return;
            }

            var summary = hymnLists.FirstOrDefault(l => l.Id == boardContext.ActiveListId);
            if (summary != null)
            {
                activeListSummary = summary;
                currentView = GroupDashboardView.HymnListDetail;
                viewingListDetail = true;
                UpdateBoardViewMode();
                UpdateLayout();
                await LoadActiveListAsync(showLoading: true);
                return;
            }
        }

        await LoadBoardAsync();
    }

    void SyncActiveListSummaryFromHymns()
    {
        if (activeListSummary == null || string.IsNullOrWhiteSpace(activeListSummary.Id))
            return;

        var hymnCount = hymns.Count(h => !h.IsSection && !h.IsDeleted);
        var entryCount = hymns.Count(h => !h.IsDeleted);
        activeListSummary.SetCounts(hymnCount, entryCount);

        if (!string.IsNullOrWhiteSpace(activeListSummary.Name)
            && string.IsNullOrWhiteSpace(hymnLists.FirstOrDefault(l => l.Id == activeListSummary.Id)?.Name))
        {
            var existing = hymnLists.FirstOrDefault(l => l.Id == activeListSummary.Id);
            if (existing != null)
                existing.Name = activeListSummary.Name;
        }

        var summary = hymnLists.FirstOrDefault(l => l.Id == activeListSummary.Id);
        if (summary != null && !ReferenceEquals(summary, activeListSummary))
            summary.SetCounts(hymnCount, entryCount);
        else if (summary == null)
            hymnLists.Insert(0, activeListSummary);
    }

    void ShowHymnListsView()
    {
        SyncActiveListSummaryFromHymns();
        currentView = GroupDashboardView.HymnLists;
        viewingListDetail = false;
        activeListSummary = null;
        ResetMemberSearch();
        boardSubscription?.Dispose();
        boardSubscription = null;
        subscribedListId = null;
        hymns.Clear();
        displayHymns.Clear();
        sessionDeletedHymns.Clear();

        // A stuck loader would hide the overview even after switching views.
        while (boardLoadingCount > 0)
            SetBoardLoading(false);

        UpdateBoardViewMode();
        UpdateLayout();
    }

    void ShowListsOverview() => ShowHymnListsView();

    void BackToLists_Tapped(object sender, TappedEventArgs e)
    {
        MarkActiveListViewed();

        suppressBoardContextReload = true;
        try
        {
            boardContext.ActiveListId = string.Empty;
        }
        finally
        {
            suppressBoardContextReload = false;
        }

        ShowListsOverview();
    }

    async Task OpenHymnListAsync(
        GroupHymnListSummary summary,
        bool applySavedSections = true,
        bool stripUnsavedSections = false)
    {
        if (summary == null || activeGroup == null)
            return;

        activeListSummary = summary;
        currentView = GroupDashboardView.HymnListDetail;
        viewingListDetail = true;

        CaptureNewHymnBaseline(summary.Id);
        CaptureDeletedHymnsForSession(summary.Id);

        // Clear the board-card badge as soon as the list is opened.
        summary.UnreadCount = 0;
        _ = boardNotificationService.MarkListReadAsync(activeGroup.Id, summary.Id);

        suppressBoardContextReload = true;
        try
        {
            boardContext.ActiveListId = summary.Id;
        }
        finally
        {
            suppressBoardContextReload = false;
        }

        await LoadActiveListAsync(
            showLoading: true,
            applySavedSections: applySavedSections,
            stripUnsavedSections: stripUnsavedSections);
        UpdateLayout();
    }

    static readonly TimeSpan BoardLoadTimeout = TimeSpan.FromSeconds(15);

    async Task LoadBoardAsync()
    {
        if (!auth.IsSignedIn || !profileService.HasCompleteProfile)
            return;

        SetBoardLoading(true);
        var version = Interlocked.Increment(ref loadBoardVersion);
        await loadBoardLock.WaitAsync();
        suppressBoardContextReload = true;
        var timedOut = false;
        using var loadCts = new CancellationTokenSource(BoardLoadTimeout);
        try
        {
            if (version != loadBoardVersion)
                return;

            var knownGroupId = boardContext.ActiveGroupId?.Trim();
            var groupsTask = groupService.GetMyGroupsAsync();

            // When we already know the active group, start board fetches in parallel with groups.
            Task<BoardListsPage> earlySummariesTask = null;
            Task<BoardSectionTemplate> earlyTemplateTask = null;
            if (!string.IsNullOrWhiteSpace(knownGroupId))
            {
                earlySummariesTask = boardService.ListHymnListsAsync(knownGroupId);
                earlyTemplateTask = boardService.GetSectionTemplateAsync(knownGroupId);
            }

            var groups = await groupsTask.WaitAsync(loadCts.Token);
            if (version != loadBoardVersion)
                return;

            hasGroups = groups.Count > 0;
            UpdateLayout();

            if (!hasGroups)
            {
                activeGroup = null;
                activeListSummary = null;
                subscribedGroupId = null;
                subscribedListId = null;
                viewingListDetail = false;
                currentView = GroupDashboardView.HymnLists;
                boardSubscription?.Dispose();
                boardSubscription = null;
                hymnLists.Clear();
                hymnListsHasMore = false;
                hymnListsLoadingMore = false;
                hymns.Clear();
                displayHymns.Clear();
                return;
            }

            activeGroup = groups.FirstOrDefault(g => g.Id == boardContext.ActiveGroupId) ?? groups[0];
            if (!string.Equals(boardContext.ActiveGroupId, activeGroup.Id, StringComparison.Ordinal))
                boardContext.ActiveGroupId = activeGroup.Id;

            lblGroupName.Text = activeGroup.Name;

            var pendingListId = boardContext.ActiveListId;
            var openingListDetail = !string.IsNullOrWhiteSpace(pendingListId);

            var summariesTask = string.Equals(knownGroupId, activeGroup.Id, StringComparison.Ordinal)
                && earlySummariesTask != null
                ? earlySummariesTask
                : boardService.ListHymnListsAsync(activeGroup.Id);
            var templateTask = string.Equals(knownGroupId, activeGroup.Id, StringComparison.Ordinal)
                && earlyTemplateTask != null
                ? earlyTemplateTask
                : boardService.GetSectionTemplateAsync(activeGroup.Id);

            if (openingListDetail)
            {
                await templateTask.WaitAsync(loadCts.Token);
                if (version != loadBoardVersion)
                    return;

                activeSectionTemplate = templateTask.Result;
                ApplySavedSectionTemplate(activeSectionTemplate);

                activeListSummary = new GroupHymnListSummary { Id = pendingListId };
                currentView = GroupDashboardView.HymnListDetail;
                viewingListDetail = true;

                CaptureNewHymnBaseline(pendingListId);
                CaptureDeletedHymnsForSession(pendingListId);
                _ = boardNotificationService.MarkListReadAsync(activeGroup.Id, pendingListId);

                UpdateBoardViewMode();
                UpdateLayout();

                await LoadActiveListAsync(version, showLoading: false).WaitAsync(loadCts.Token);
                _ = ApplySummariesWhenReadyAsync(summariesTask, version);
            }
            else
            {
                await Task.WhenAll(summariesTask, templateTask).WaitAsync(loadCts.Token);
                if (version != loadBoardVersion)
                    return;

                var page = await summariesTask;
                activeSectionTemplate = await templateTask;

                hymnLists.Clear();
                hymnListsHasMore = page.HasMore;
                AddHymnListSummaries(page.Items);
                ApplySavedSectionTemplate(activeSectionTemplate);
                ShowListsOverview();
            }

            if (!openingListDetail)
            {
                UpdateBoardViewMode();
                UpdateLayout();
            }

            if (currentView == GroupDashboardView.Members)
                await LoadMembersAsync().WaitAsync(loadCts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            Debug.WriteLine("LoadBoardAsync timed out.");
            if (version == loadBoardVersion)
            {
                ShowListsOverview();
                UpdateBoardViewMode();
                UpdateLayout();
            }
        }
        catch (TimeoutException)
        {
            timedOut = true;
            Debug.WriteLine("LoadBoardAsync timed out.");
            if (version == loadBoardVersion)
            {
                ShowListsOverview();
                UpdateBoardViewMode();
                UpdateLayout();
            }
        }
        finally
        {
            suppressBoardContextReload = false;
            loadBoardLock.Release();
            SetBoardLoading(false);
            if (timedOut && version == loadBoardVersion)
                MainThread.BeginInvokeOnMainThread(() =>
                    Globals.ShowToastPopup("error", "Board is taking too long. Try again.", 120));
        }
    }

    async Task ApplySummariesWhenReadyAsync(Task<BoardListsPage> summariesTask, int version)
    {
        try
        {
            var page = await summariesTask;
            if (version != loadBoardVersion)
                return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (version != loadBoardVersion)
                    return;

                hymnLists.Clear();
                hymnListsHasMore = page.HasMore;
                AddHymnListSummaries(page.Items);

                if (activeListSummary != null)
                {
                    var selected = hymnLists.FirstOrDefault(l => l.Id == activeListSummary.Id);
                    if (selected != null)
                    {
                        if (activeListSummary.HymnCount > selected.HymnCount)
                            selected.SetCounts(activeListSummary.HymnCount, activeListSummary.EntryCount);
                        activeListSummary = selected;
                    }
                    else if (activeListSummary.HymnCount > 0)
                    {
                        hymnLists.Insert(0, activeListSummary);
                    }
                    else
                    {
                        boardContext.ActiveListId = string.Empty;
                        ShowHymnListsView();
                        return;
                    }

                    if (lblActiveListName != null)
                        lblActiveListName.Text = activeListSummary.Name ?? string.Empty;
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ApplySummariesWhenReadyAsync failed: {ex.Message}");
        }
    }

    async void HymnLists_RemainingItemsThresholdReached(object sender, EventArgs e) =>
        await LoadMoreHymnListsAsync();

    async Task LoadMoreHymnListsAsync()
    {
        if (!hymnListsHasMore
            || hymnListsLoadingMore
            || activeGroup == null
            || hymnLists.Count == 0
            || currentView != GroupDashboardView.HymnLists)
            return;

        hymnListsLoadingMore = true;
        try
        {
            var cursor = hymnLists[hymnLists.Count - 1];
            var page = await boardService.ListHymnListsAsync(activeGroup.Id, cursor);
            hymnListsHasMore = page.HasMore;

            var existingIds = new HashSet<string>(hymnLists.Select(l => l.Id), StringComparer.Ordinal);
            AddHymnListSummaries(page.Items.Where(item => existingIds.Add(item.Id)));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadMoreHymnListsAsync failed: {ex.Message}");
        }
        finally
        {
            hymnListsLoadingMore = false;
        }
    }

    async Task LoadActiveListAsync(
        int? expectedVersion = null,
        bool showLoading = true,
        bool applySavedSections = true,
        bool stripUnsavedSections = false)
    {
        if (activeGroup == null || activeListSummary == null)
            return;

        var groupId = activeGroup.Id;
        var listId = activeListSummary.Id;

        hymnListLoading = true;
        keepAddHymnPanelAfterLoad = false;
        if (addHymnPanelExpanded)
            SetAddHymnPanelExpanded(false, animate: false);

        if (showLoading)
            SetBoardLoading(true);
        try
        {
            GroupHymnList current;
            var needsSubscription = boardSubscription == null
                || !string.Equals(subscribedGroupId, groupId, StringComparison.Ordinal)
                || !string.Equals(subscribedListId, listId, StringComparison.Ordinal);

            if (needsSubscription)
            {
                hymns.Clear();
                displayHymns.Clear();
                boardSubscription?.Dispose();
                subscribedGroupId = groupId;
                subscribedListId = listId;

                // Live updates keep whatever is stored; strip only applies to the first paint after create.
                var (subscription, initial) = await boardService.SubscribeHymnListWithInitialAsync(
                    groupId,
                    listId,
                    list => MainThread.BeginInvokeOnMainThread(() =>
                        _ = ApplyHymnListAsync(list, applySavedSections: applySavedSections)));
                boardSubscription = subscription;
                current = initial;
            }
            else if (hymns.Count > 0)
            {
                if (activeListSummary != null && lblActiveListName != null
                    && string.IsNullOrWhiteSpace(lblActiveListName.Text)
                    && !string.IsNullOrWhiteSpace(activeListSummary.Name))
                {
                    lblActiveListName.Text = activeListSummary.Name;
                }

                return;
            }
            else
            {
                current = await boardService.GetHymnListAsync(groupId, listId);
            }

            if (expectedVersion.HasValue && expectedVersion.Value != loadBoardVersion)
                return;

            if (activeListSummary != null && current != null)
            {
                activeListSummary.Name = current.Name;
                activeListSummary.CreatedAt = current.CreatedAt;
                activeListSummary.CreatedBy = current.CreatedBy;
                if (lblActiveListName != null)
                    lblActiveListName.Text = current.Name ?? string.Empty;
            }

            await ApplyHymnListAsync(
                current,
                applySavedSections: applySavedSections,
                stripUnsavedSections: stripUnsavedSections);

            if (showLoading)
                SetBoardLoading(false);

            if (applySavedSections)
                _ = EnsureSavedSectionsInBackgroundAsync(groupId, listId, current, expectedVersion);
        }
        finally
        {
            hymnListLoading = false;
            UpdateAddPanelsExpansion();
            if (showLoading)
                SetBoardLoading(false);
        }
    }

    async Task EnsureSavedSectionsInBackgroundAsync(
        string groupId,
        string listId,
        GroupHymnList current,
        int? expectedVersion)
    {
        try
        {
            var merged = await boardService.EnsureSavedSectionsOnListAsync(
                groupId,
                listId,
                persistIfChanged: canEdit,
                template: activeSectionTemplate,
                list: current);

            if (expectedVersion.HasValue && expectedVersion.Value != loadBoardVersion)
                return;

            if (merged != null)
                MainThread.BeginInvokeOnMainThread(() => _ = ApplyHymnListAsync(merged));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"EnsureSavedSectionsInBackgroundAsync failed: {ex.Message}");
        }
    }

    async Task ApplyHymnListAsync(GroupHymnList list, bool applySavedSections = true, bool stripUnsavedSections = false)
    {
        if (list == null)
        {
            CommitHymnListState(null);
            return;
        }

        if (stripUnsavedSections)
            StripUnsavedSections(list);

        if (applySavedSections)
            list = InjectSavedSections(list);

        var ordered = list.Hymns?.OrderBy(h => h.SortOrder).ToList() ?? new List<BoardHymnEntry>();
        var collapsedSectionIds = hymns
            .Where(h => h.IsSection && h.IsCollapsed)
            .Select(h => h.Id)
            .ToHashSet(StringComparer.Ordinal);
        var lookup = GetHymnNumberLookup();

        if (ordered.Count > 0)
        {
            await Task.Run(() =>
            {
                foreach (var hymn in ordered)
                {
                    if (hymn.IsSection && collapsedSectionIds.Contains(hymn.Id))
                        hymn.IsCollapsed = true;
                    if (!hymn.IsSection)
                        hymn.FirstLine = ResolveFirstLine(hymn.HymnNumber, lookup);
                }
            });
        }

        list.Hymns = ordered;
        ApplyNewHymnFlags(ordered);
        ApplyDeletedHymnIndicators(ordered);
        CommitHymnListState(list);
        RefreshHymnFirstLines();
    }

    void ApplyNewHymnFlags(IList<BoardHymnEntry> entries)
    {
        if (entries == null || entries.Count == 0 || activeGroup == null || activeListSummary == null)
            return;

        var uid = auth.CurrentUserId;
        var baseline = newHymnBaselineUtc;

        foreach (var entry in entries)
        {
            if (entry.IsSection || entry.IsDeleted)
            {
                entry.IsNew = false;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(uid)
                && string.Equals(entry.AddedBy, uid, StringComparison.Ordinal))
            {
                entry.IsNew = false;
                continue;
            }

            var updated = entry.UpdatedAt.Kind == DateTimeKind.Utc
                ? entry.UpdatedAt
                : entry.UpdatedAt.ToUniversalTime();
            entry.IsNew = updated > baseline;
        }
    }

    void CaptureDeletedHymnsForSession(string listId)
    {
        sessionDeletedHymns.Clear();
        if (activeGroup == null || string.IsNullOrWhiteSpace(listId))
            return;

        var deleted = dashboardService.GetListDeletedHymns(activeGroup.Id, listId);
        if (deleted == null || deleted.Count == 0)
            return;

        sessionDeletedHymns.AddRange(deleted);
    }

    void ApplyDeletedHymnIndicators(List<BoardHymnEntry> ordered)
    {
        if (ordered == null || sessionDeletedHymns.Count == 0)
            return;

        var liveIds = new HashSet<string>(
            ordered.Where(h => !h.IsSection).Select(h => h.Id),
            StringComparer.Ordinal);
        var lookup = GetHymnNumberLookup();

        foreach (var deleted in sessionDeletedHymns
                     .OrderBy(d => d.SortOrder)
                     .ThenBy(d => d.DeletedAtUtc))
        {
            if (deleted == null || string.IsNullOrWhiteSpace(deleted.Id) || liveIds.Contains(deleted.Id))
                continue;

            var entry = new BoardHymnEntry
            {
                Id = deleted.Id,
                HymnNumber = deleted.HymnNumber ?? string.Empty,
                Notes = deleted.Notes ?? string.Empty,
                SortOrder = deleted.SortOrder,
                AddedByName = deleted.DeletedByName ?? string.Empty,
                UpdatedAt = deleted.DeletedAtUtc == default ? DateTime.UtcNow : deleted.DeletedAtUtc,
                IsDeleted = true,
                IsNew = false,
                ShowDragHandle = false,
            };
            entry.FirstLine = ResolveFirstLine(entry.HymnNumber, lookup);

            var insertAt = ordered.FindIndex(h =>
                !h.IsSection && !h.IsDeleted && h.SortOrder > entry.SortOrder);
            if (insertAt < 0)
                ordered.Add(entry);
            else
                ordered.Insert(insertAt, entry);

            liveIds.Add(entry.Id);
        }
    }

    void CaptureNewHymnBaseline(string listId)
    {
        if (activeGroup == null || string.IsNullOrWhiteSpace(listId) || !auth.IsSignedIn)
        {
            newHymnBaselineUtc = DateTime.MaxValue;
            return;
        }

        var lastRead = BoardListReadStore.GetLastReadUtc(auth.CurrentUserId, activeGroup.Id, listId);
        var unreadSince = dashboardService.GetListUnreadSinceUtc(activeGroup.Id, listId);
        if (lastRead > DateTime.MinValue)
            newHymnBaselineUtc = lastRead;
        else if (unreadSince > DateTime.MinValue)
            newHymnBaselineUtc = unreadSince.AddMinutes(-1);
        else
            newHymnBaselineUtc = DateTime.MaxValue;
    }

    void MarkActiveListViewed()
    {
        if (activeGroup == null || activeListSummary == null || !auth.IsSignedIn)
            return;

        BoardListReadStore.SetLastReadUtc(
            auth.CurrentUserId,
            activeGroup.Id,
            activeListSummary.Id,
            DateTime.UtcNow);

        newHymnBaselineUtc = DateTime.MaxValue;
        foreach (var entry in hymns)
            entry.IsNew = false;
        sessionDeletedHymns.Clear();
        var deletedRows = hymns.Where(h => h.IsDeleted).ToList();
        if (deletedRows.Count > 0)
        {
            foreach (var entry in deletedRows)
                hymns.Remove(entry);
            RefreshSectionPresentation();
            SyncActiveListSummaryFromHymns();
        }

        activeListSummary.UnreadCount = 0;
    }

    void ApplyUnreadBadgesToLists()
    {
        if (activeGroup == null)
            return;

        foreach (var summary in hymnLists)
            summary.UnreadCount = dashboardService.GetListUnreadCount(activeGroup.Id, summary.Id);
    }

    void RefreshHymnFirstLines()
    {
        if (hymns.Count == 0)
            return;

        // Force rebuild in case hymns finished downloading after an empty cache.
        hymnNumberLookup = null;
        hymnNumberLookupCount = -1;
        var lookup = GetHymnNumberLookup();
        if (lookup.Count == 0)
            return;

        foreach (var entry in hymns)
        {
            if (entry.IsSection)
                continue;

            var line = ResolveFirstLine(entry.HymnNumber, lookup);
            if (!string.Equals(entry.FirstLine, line, StringComparison.Ordinal))
                entry.FirstLine = line;
        }
    }

    void CommitHymnListState(GroupHymnList list)
    {
        if (list?.Hymns == null)
        {
            hymns.Clear();
            displayHymns.Clear();
            SyncActiveListSummaryFromHymns();
            UpdateAddPanelsExpansion();
            return;
        }

        var ordered = list.Hymns.OrderBy(h => h.SortOrder).ToList();
        if (ordered.Count == hymns.Count)
        {
            var unchanged = true;
            for (var i = 0; i < ordered.Count; i++)
            {
                var incoming = ordered[i];
                var existing = hymns[i];
                if (incoming.Id != existing.Id
                    || incoming.IsSection != existing.IsSection
                    || !string.Equals(incoming.SectionName, existing.SectionName, StringComparison.Ordinal)
                    || !string.Equals(incoming.HymnNumber, existing.HymnNumber, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(incoming.Notes, existing.Notes, StringComparison.Ordinal)
                    || incoming.SortOrder != existing.SortOrder
                    || !string.Equals(incoming.AddedByName, existing.AddedByName, StringComparison.Ordinal)
                    || incoming.UpdatedAt != existing.UpdatedAt
                    || incoming.IsDeleted != existing.IsDeleted
                    || incoming.IsNew != existing.IsNew)
                {
                    unchanged = false;
                    break;
                }
            }

            if (unchanged)
            {
                // FirstLine is UI-only and resolved after hymns download — keep it in sync.
                for (var i = 0; i < ordered.Count; i++)
                {
                    if (!ordered[i].IsSection
                        && !string.Equals(hymns[i].FirstLine, ordered[i].FirstLine, StringComparison.Ordinal))
                    {
                        hymns[i].FirstLine = ordered[i].FirstLine;
                    }

                    if (hymns[i].IsNew != ordered[i].IsNew)
                        hymns[i].IsNew = ordered[i].IsNew;
                }

                RefreshSectionPresentation();
                UpdateAddPanelsExpansion();
                return;
            }
        }

        hymns.Clear();
        foreach (var hymn in ordered)
            hymns.Add(hymn);

        SyncActiveListSummaryFromHymns();
        RefreshSectionPresentation();
        UpdateAddPanelsExpansion();
    }

    void RefreshSectionPresentation()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        BoardHymnEntry currentSection = null;

        foreach (var item in hymns)
        {
            if (item.IsSection)
            {
                currentSection = item;
                counts[item.Id] = 0;
                continue;
            }

            if (currentSection != null && !item.IsDeleted)
                counts[currentSection.Id] = counts.GetValueOrDefault(currentSection.Id) + 1;
        }

        currentSection = null;
        var canViewNotes = RolePermissions.CanViewBoardNotes(profileService.CurrentProfile?.Roles);
        foreach (var item in hymns)
        {
            item.ShowDragHandle = canEdit && item.IsNotEditing && !item.IsDeleted;

            if (item.IsSection)
            {
                currentSection = item;
                item.SectionHymnCount = counts.GetValueOrDefault(item.Id, 0);
                item.IsRowVisible = true;
                item.ShowNotes = false;
                var name = item.SectionName?.Trim() ?? string.Empty;
                item.IsSectionSaved = !string.IsNullOrEmpty(name) && savedSectionNames.Contains(name);
                item.ShowSectionSaveAction = canEdit && item.IsNotEditing && !item.IsSectionSaved;
                item.ShowSectionUnsaveAction = canEdit && item.IsNotEditing && item.IsSectionSaved;
                continue;
            }

            item.IsRowVisible = currentSection == null || !currentSection.IsCollapsed;
            item.ShowNotes = canViewNotes && item.HasNotes;
            item.ShowSectionSaveAction = false;
            item.ShowSectionUnsaveAction = false;
        }

        RebuildDisplayHymns();
    }

    void RebuildDisplayHymns()
    {
        displayHymns.Clear();
        BoardHymnEntry currentSection = null;

        foreach (var item in hymns)
        {
            if (item.IsSection)
            {
                currentSection = item;
                displayHymns.Add(item);
                continue;
            }

            if (currentSection == null || !currentSection.IsCollapsed)
                displayHymns.Add(item);
        }
    }

    void SectionHeader_Tapped(object sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not BoardHymnEntry entry || !entry.IsSection)
            return;

        entry.IsCollapsed = !entry.IsCollapsed;
        hymnList.SelectedItem = null;
        RefreshSectionPresentation();
    }

    void BoardEntry_DragStarting(object sender, DragStartingEventArgs e)
    {
        if (!canEdit)
        {
            e.Cancel = true;
            return;
        }

        var entry = ResolveBoardEntry(sender);
        if (entry == null || entry.IsEditing || !entry.ShowDragHandle)
        {
            e.Cancel = true;
            return;
        }

        dragEntry = entry;
        e.Data.Properties["EntryId"] = entry.Id;
        ClearDropIndicators();
        ApplyBoardDragShadow(e);
    }

    static void ApplyBoardDragShadow(DragStartingEventArgs e)
    {
#if ANDROID
        if (e.PlatformArgs?.Sender is not global::Android.Views.View platformView)
            return;

        e.PlatformArgs.SetDragShadowBuilder(
            new Platforms.Android.BoardDragShadowBuilder(platformView, e.PlatformArgs.MotionEvent));
#endif
    }

    static BoardHymnEntry ResolveBoardEntry(object sender)
    {
        if (sender is not Element element)
            return null;

        for (var node = element; node != null; node = node.Parent)
        {
            if (node.BindingContext is BoardHymnEntry entry)
                return entry;
        }

        return null;
    }

    void BoardEntry_DragOver(object sender, DragEventArgs e)
    {
        if (!canEdit || dragEntry == null)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        var target = ResolveBoardEntry(sender);
        if (target == null)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        UpdateDropIndicator(target);
        e.AcceptedOperation = DataPackageOperation.Copy;
    }

    void BoardEntry_DragLeave(object sender, DragEventArgs e)
    {
        // Full clear so a redirected indicator (e.g. end of section block) does not stick.
        // The next DragOver restores the line while still hovering a row.
        ClearDropIndicators();
    }

    void BoardEntry_DropCompleted(object sender, DropCompletedEventArgs e)
    {
        ClearDropIndicators();
        dragEntry = null;
    }

    async void BoardEntry_Drop(object sender, DropEventArgs e)
    {
        ClearDropIndicators();

        if (!canEdit || activeGroup == null)
            return;

        var target = ResolveBoardEntry(sender);
        var source = dragEntry ?? ResolveDragSourceFromPackage(e);
        dragEntry = null;

        if (source == null || target == null || source.Id == target.Id)
            return;

        MoveEntry(source, target);
        await PersistReorderAsync(manualReorder: true);
    }

    BoardHymnEntry ResolveDragSourceFromPackage(DropEventArgs e)
    {
        if (e?.Data?.Properties == null)
            return null;

        if (!e.Data.Properties.TryGetValue("EntryId", out var idObj))
            return null;

        var id = idObj as string;
        if (string.IsNullOrEmpty(id))
            return null;

        return hymns.FirstOrDefault(h => h.Id == id);
    }

    void UpdateDropIndicator(BoardHymnEntry hovered)
    {
        if (dragEntry == null || hovered == null || dragEntry.Id == hovered.Id)
        {
            ClearDropIndicators();
            return;
        }

        var indicator = ResolveDropIndicatorTarget(dragEntry, hovered, out var below);
        if (indicator == null)
        {
            ClearDropIndicators();
            return;
        }

        foreach (var item in hymns)
        {
            var isTarget = ReferenceEquals(item, indicator);
            item.IsDragOver = isTarget;
            item.ShowDropLineAbove = isTarget && !below;
            item.ShowDropLineBelow = isTarget && below;
        }
    }

    BoardHymnEntry ResolveDropIndicatorTarget(BoardHymnEntry source, BoardHymnEntry target, out bool below)
    {
        below = false;
        var sourceIndex = hymns.IndexOf(source);
        var targetIndex = hymns.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0)
            return null;

        // Match MoveEntry / MoveSectionBlock insert side.
        if (source.IsSection)
        {
            if (target.IsSection)
            {
                if (sourceIndex < targetIndex)
                {
                    below = true;
                    return LastVisibleInSectionBlock(target) ?? target;
                }

                below = false;
                return target;
            }

            below = sourceIndex < targetIndex;
            return target;
        }

        if (target.IsSection)
        {
            if (sourceIndex < targetIndex)
            {
                below = true;
                return LastVisibleInSectionBlock(target) ?? target;
            }

            // Insert as first hymn under this section header.
            below = true;
            return target;
        }

        below = sourceIndex < targetIndex;
        return target;
    }

    BoardHymnEntry LastVisibleInSectionBlock(BoardHymnEntry section)
    {
        var block = GetSectionBlock(hymns, section);
        for (var i = block.Count - 1; i >= 0; i--)
        {
            if (displayHymns.Contains(block[i]))
                return block[i];
        }

        return section;
    }

    void ClearDropIndicators()
    {
        foreach (var item in hymns)
        {
            if (!item.IsDragOver && !item.ShowDropLineAbove && !item.ShowDropLineBelow)
                continue;

            item.IsDragOver = false;
            item.ShowDropLineAbove = false;
            item.ShowDropLineBelow = false;
        }
    }

    void MoveEntry(BoardHymnEntry source, BoardHymnEntry target)
    {
        if (source.IsSection)
        {
            MoveSectionBlock(source, target);
            return;
        }

        var sourceIndex = hymns.IndexOf(source);
        var targetIndex = hymns.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0)
            return;

        int insertIndex;
        if (target.IsSection)
        {
            target.IsCollapsed = false;
            if (sourceIndex < targetIndex)
            {
                var targetBlock = GetSectionBlock(hymns, target);
                insertIndex = targetIndex + targetBlock.Count;
            }
            else
            {
                insertIndex = targetIndex + 1;
            }
        }
        else
        {
            insertIndex = sourceIndex < targetIndex ? targetIndex + 1 : targetIndex;
        }

        hymns.RemoveAt(sourceIndex);
        if (sourceIndex < insertIndex)
            insertIndex--;

        insertIndex = Math.Clamp(insertIndex, 0, hymns.Count);
        hymns.Insert(insertIndex, source);
        RefreshSectionPresentation();
    }

    static List<BoardHymnEntry> GetSectionBlock(IList<BoardHymnEntry> entries, BoardHymnEntry section)
    {
        var index = entries.IndexOf(section);
        if (index < 0)
            return new List<BoardHymnEntry>();

        var block = new List<BoardHymnEntry>();
        for (var i = index; i < entries.Count; i++)
        {
            if (i > index && entries[i].IsSection)
                break;

            block.Add(entries[i]);
        }

        return block;
    }

    void MoveSectionBlock(BoardHymnEntry sourceSection, BoardHymnEntry target)
    {
        var block = GetSectionBlock(hymns, sourceSection);
        if (block.Count == 0)
            return;

        var sourceIndex = hymns.IndexOf(sourceSection);
        var targetIndex = hymns.IndexOf(target);
        if (targetIndex < 0)
            return;

        var blockEnd = sourceIndex + block.Count - 1;
        if (targetIndex >= sourceIndex && targetIndex <= blockEnd)
            return;

        int insertIndex;
        if (target.IsSection)
        {
            var targetBlock = GetSectionBlock(hymns, target);
            insertIndex = sourceIndex < targetIndex
                ? targetIndex + targetBlock.Count
                : targetIndex;
        }
        else
        {
            insertIndex = sourceIndex < targetIndex ? targetIndex + 1 : targetIndex;
        }

        foreach (var item in block)
            hymns.Remove(item);

        if (sourceIndex < insertIndex)
            insertIndex -= block.Count;

        insertIndex = Math.Clamp(insertIndex, 0, hymns.Count);
        for (var i = 0; i < block.Count; i++)
            hymns.Insert(insertIndex + i, block[i]);

        RefreshSectionPresentation();
    }

    async Task PersistReorderAsync(bool manualReorder = false)
    {
        if (!canEdit || activeGroup == null || suppressBoardReorder)
            return;

        for (var i = 0; i < hymns.Count; i++)
            hymns[i].SortOrder = i;

        try
        {
            suppressBoardReorder = true;
            await boardService.ReorderHymnsAsync(
                activeGroup.Id,
                boardContext.ActiveListId,
                hymns.Where(h => !h.IsDeleted).ToList());

            if (manualReorder && sectionSortMode != SectionSortMode.AddedOrder)
            {
                var template = await boardService.SetSectionSortPreferenceAsync(
                    activeGroup.Id, SectionSortMode.AddedOrder);
                ApplySavedSectionTemplate(template);
            }
        }
        catch (Exception ex)
        {
            await Application.Current.MainPage.DisplayAlert("Board", ex.Message, "OK");
            await LoadBoardAsync();
        }
        finally
        {
            suppressBoardReorder = false;
        }
    }

    Dictionary<string, Hymn> GetHymnNumberLookup()
    {
        var list = Globals.Instance.HymnList;
        var count = list?.Count ?? 0;
        if (hymnNumberLookup != null && hymnNumberLookupCount == count)
            return hymnNumberLookup;

        hymnNumberLookup = BuildHymnNumberLookup();
        hymnNumberLookupCount = count;
        return hymnNumberLookup;
    }

    static Dictionary<string, Hymn> BuildHymnNumberLookup()
    {
        var list = Globals.Instance.HymnList;
        if (list == null || list.Count == 0)
            return new Dictionary<string, Hymn>(StringComparer.OrdinalIgnoreCase);

        return list
            .Where(h => h != null && !string.IsNullOrWhiteSpace(h.Number))
            .GroupBy(h => h.Number, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    static string ResolveFirstLine(string hymnNumber, Dictionary<string, Hymn> hymnLookup)
    {
        if (string.IsNullOrWhiteSpace(hymnNumber))
            return string.Empty;

        var key = hymnNumber.Trim();
        Hymn hymn = null;
        if (hymnLookup != null)
            hymnLookup.TryGetValue(key, out hymn);

        if (hymn == null)
            hymn = Globals.Instance.HymnList?[key];

        if (hymn == null)
        {
            // Handle stored numbers like "70" matching "70s"/"70t" only when exact fails.
            var list = Globals.Instance.HymnList;
            if (list != null)
                hymn = HymnNumberHelper.FindExact(key, list) ?? HymnNumberHelper.ResolveHymn(key, list);
        }

        if (hymn == null)
            return string.Empty;

        var line = hymn.FirstLine;
        if (string.IsNullOrWhiteSpace(line) && !string.IsNullOrWhiteSpace(hymn.Lyrics))
            line = HymnNumberHelper.ExtractLyricLines(hymn.Lyrics).FirstOrDefault();

        if (string.IsNullOrWhiteSpace(line))
            line = hymn.Title;

        if (string.IsNullOrWhiteSpace(line))
            return string.Empty;

        return HymnNumberHelper.CleanDisplayLine(line);
    }

    static string ResolveFirstLine(string hymnNumber) =>
        ResolveFirstLine(hymnNumber, null);

    async Task RefreshSavedSectionNamesAsync()
    {
        if (activeGroup == null)
        {
            ApplySavedSectionTemplate(null);
            return;
        }

        var template = await boardService.GetSectionTemplateAsync(activeGroup.Id);
        activeSectionTemplate = template;
        ApplySavedSectionTemplate(template);
    }

    void ApplySavedSectionTemplate(BoardSectionTemplate template)
    {
        savedSectionsAutoApply = template?.AutoApply ?? false;
        sectionSortMode = template?.SectionSort ?? SectionSortMode.AddedOrder;
        savedSectionNamesOrdered = template?.SectionNames?
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToList() ?? new List<string>();
        savedSectionNames = savedSectionNamesOrdered.ToHashSet(StringComparer.OrdinalIgnoreCase);
        RefreshSectionPresentation();
    }

    void StripUnsavedSections(GroupHymnList list)
    {
        if (list?.Hymns == null || list.Hymns.Count == 0)
            return;

        list.Hymns = list.Hymns
            .Where(h => !h.IsSection
                || savedSectionNames.Contains(h.SectionName?.Trim() ?? string.Empty))
            .ToList();
    }

    GroupHymnList InjectSavedSections(GroupHymnList list)
    {
        if (list == null)
            return list;

        if (savedSectionsAutoApply && savedSectionNamesOrdered.Count > 0)
        {
            var template = new BoardSectionTemplate
            {
                AutoApply = true,
                SectionNames = savedSectionNamesOrdered,
                SectionSort = sectionSortMode,
            };
            return boardService.MergeSavedSections(list, template);
        }

        if (list.Hymns?.Any(h => h.IsSection) == true)
            return boardService.ApplySectionSort(list, sectionSortMode);

        return list;
    }

    async Task RefreshSectionTemplateAsync()
    {
        if (activeGroup == null)
            return;

        await RefreshSavedSectionNamesAsync();
    }

    void InitializeMemberFilters()
    {
        if (memberFilterChips == null)
            return;

        memberFilterChips.Children.Clear();
        memberFilterChipBorders.Clear();
        memberFilterChipLabels.Clear();

        AddMemberFilterChip(null, "All");
        foreach (var role in GroupMemberRoleExtensions.GetFilterableRoles())
            AddMemberFilterChip(role, role.ToDisplayName());

        UpdateMemberFilterChipStyles();
        UpdateMemberViewModeStyles();
    }

    static string MemberFilterKey(UserRole? role) =>
        role.HasValue ? role.Value.ToStorageKey() : MemberFilterAllKey;

    void AddMemberFilterChip(UserRole? role, string label)
    {
        var key = MemberFilterKey(role);
        var border = new Border
        {
            Padding = new Thickness(10, 6),
            Margin = new Thickness(0, 0, 6, 6),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
        };
        border.SetAppThemeColor(Border.StrokeProperty, (Color)Application.Current.Resources["GrayLight"], (Color)Application.Current.Resources["Gray"]);
        border.SetAppThemeColor(Border.BackgroundColorProperty, Colors.White, Color.FromArgb("#333333"));

        var text = new Label
        {
            Text = label,
            FontSize = 12,
        };
        text.SetAppThemeColor(Label.TextColorProperty, (Color)Application.Current.Resources["PrimaryText"], Colors.White);

        border.Content = text;
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => SelectMemberRoleFilter(role);
        border.GestureRecognizers.Add(tap);

        memberFilterChips.Children.Add(border);
        memberFilterChipBorders[key] = border;
        memberFilterChipLabels[key] = text;
    }

    void SelectMemberRoleFilter(UserRole? role)
    {
        memberRoleFilter = role;
        UpdateMemberFilterChipStyles();
        ApplyMemberListPresentation();
    }

    void UpdateMemberFilterChipStyles()
    {
        var selectedKey = MemberFilterKey(memberRoleFilter);
        foreach (var pair in memberFilterChipBorders)
        {
            var selected = string.Equals(pair.Key, selectedKey, StringComparison.Ordinal);
            var border = pair.Value;
            var label = memberFilterChipLabels[pair.Key];
            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;

            border.BackgroundColor = selected
                ? (Color)Application.Current.Resources["Primary"]
                : isDark ? Color.FromArgb("#333333") : Colors.White;
            border.Stroke = selected
                ? (Color)Application.Current.Resources["Primary"]
                : isDark ? (Color)Application.Current.Resources["Gray"] : (Color)Application.Current.Resources["GrayLight"];
            label.TextColor = selected
                ? (Color)Application.Current.Resources["PrimaryText"]
                : isDark ? Colors.White : (Color)Application.Current.Resources["PrimaryText"];
        }
    }

    void SetMembersGroupByRole(bool grouped)
    {
        membersGroupByRole = grouped;
        UpdateMemberViewModeStyles();
        ApplyMemberListPresentation();
    }

    void UpdateMemberViewModeStyles()
    {
        ApplySegmentStyle(btnMembersFlat, lblMembersFlat, !membersGroupByRole);
        ApplySegmentStyle(btnMembersGrouped, lblMembersGrouped, membersGroupByRole);
    }

    static void ApplySegmentStyle(Border segment, Label label, bool selected)
    {
        if (segment == null || label == null)
            return;

        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        segment.BackgroundColor = selected
            ? (Color)Application.Current.Resources["Primary"]
            : Colors.Transparent;
        label.TextColor = selected
            ? (Color)Application.Current.Resources["PrimaryText"]
            : isDark ? (Color)Application.Current.Resources["GrayLight"] : (Color)Application.Current.Resources["Gray"];
        label.FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None;
    }

    async Task ShowMembersViewAsync()
    {
        currentView = GroupDashboardView.Members;
        viewingListDetail = false;
        boardSubscription?.Dispose();
        boardSubscription = null;
        subscribedListId = null;
        hymns.Clear();
        displayHymns.Clear();
        UpdateBoardViewMode();
        UpdateLayout();
        await LoadMembersAsync();
    }

    async Task LoadMembersAsync()
    {
        if (activeGroup == null || membersLoader == null || membersCollection == null)
            return;

        membersLoader.IsVisible = true;
        membersLoader.IsRunning = true;
        membersCollection.IsVisible = false;
        lblMembersError.IsVisible = false;

        try
        {
            var memberList = await groupService.GetMembersAsync(activeGroup.Id);
            groupMembers.Clear();
            groupMembers.AddRange(memberList);
            ApplyMemberListPresentation();

            membersLoader.IsVisible = false;
            membersLoader.IsRunning = false;
            membersCollection.IsVisible = true;
        }
        catch (Exception ex)
        {
            membersLoader.IsVisible = false;
            membersLoader.IsRunning = false;
            lblMembersError.Text = ex.Message;
            lblMembersError.IsVisible = true;
        }
    }

    void ApplyMemberListPresentation()
    {
        if (membersCollection == null)
            return;

        var showRoles = !membersGroupByRole && !memberRoleFilter.HasValue;
        var filtered = FilterMembers(groupMembers)
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Email, StringComparer.OrdinalIgnoreCase)
            .Select(m => GroupMemberDisplayItem.FromMember(m, showRoles))
            .ToList();

        if (membersGroupByRole)
        {
            memberSections.Clear();
            foreach (var role in GroupMemberRoleExtensions.GetFilterableRoles())
            {
                if (memberRoleFilter.HasValue && memberRoleFilter.Value != role)
                    continue;

                var sectionMembers = filtered
                    .Where(item => item.Member?.Roles?.Contains(role) == true)
                    .ToList();
                if (sectionMembers.Count == 0)
                    continue;

                var section = new GroupMemberRoleSection(role.ToDisplayName());
                section.AddRange(sectionMembers);
                memberSections.Add(section);
            }

            if (!memberRoleFilter.HasValue)
            {
                var others = filtered
                    .Where(item => item.Member?.GetVisibleRoles().Any() != true)
                    .ToList();
                if (others.Count > 0)
                {
                    var section = new GroupMemberRoleSection("Members");
                    section.AddRange(others);
                    memberSections.Add(section);
                }
            }

            if (!membersCollection.IsGrouped || membersCollection.ItemsSource != memberSections)
            {
                membersCollection.IsGrouped = true;
                membersCollection.ItemsSource = memberSections;
            }
        }
        else
        {
            displayMembers.Clear();
            foreach (var item in filtered)
                displayMembers.Add(item);

            if (membersCollection.IsGrouped || membersCollection.ItemsSource != displayMembers)
            {
                membersCollection.IsGrouped = false;
                membersCollection.ItemsSource = displayMembers;
            }
        }
    }

    IEnumerable<GroupMember> FilterMembers(IEnumerable<GroupMember> source)
    {
        var filtered = source;

        if (memberRoleFilter.HasValue)
            filtered = filtered.Where(member => member.Roles?.Contains(memberRoleFilter.Value) == true);

        if (string.IsNullOrWhiteSpace(memberSearchQuery))
            return filtered;

        return filtered.Where(MatchesMemberSearch);
    }

    bool MatchesMemberSearch(GroupMember member)
    {
        if (member == null)
            return false;

        var query = memberSearchQuery.Trim();
        if (query.Length == 0)
            return true;

        var searchable = GetMemberSearchableText(member);
        if (searchable.Length == 0)
            return false;

        var tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tokens.All(token => searchable.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    static string GetMemberSearchableText(GroupMember member)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(member.FirstName))
            parts.Add(member.FirstName.Trim());
        if (!string.IsNullOrWhiteSpace(member.LastName))
            parts.Add(member.LastName.Trim());
        if (!string.IsNullOrWhiteSpace(member.Nickname))
            parts.Add(member.Nickname.Trim());

        return string.Join(' ', parts);
    }

    void ResetMemberSearch()
    {
        memberSearchQuery = string.Empty;
        if (entMemberSearch != null)
            entMemberSearch.Text = string.Empty;
    }

    void MemberSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        memberSearchQuery = e.NewTextValue ?? string.Empty;
        ApplyMemberListPresentation();
    }

    void BackFromMembers_Tapped(object sender, EventArgs e)
    {
        while (boardLoadingCount > 0)
            SetBoardLoading(false);
        ShowHymnListsView();
    }

    /// <summary>
    /// Steps back within the board pane (members / list detail). Returns false at the lists overview.
    /// </summary>
    public bool TryHandleBack()
    {
        if (currentView == GroupDashboardView.Members)
        {
            while (boardLoadingCount > 0)
                SetBoardLoading(false);
            ShowHymnListsView();
            return true;
        }

        if (currentView == GroupDashboardView.HymnListDetail)
        {
            BackToLists_Tapped(this, null);
            return true;
        }

        return false;
    }

    void MembersFlatView_Tapped(object sender, EventArgs e) => SetMembersGroupByRole(false);

    void MembersGroupedView_Tapped(object sender, EventArgs e) => SetMembersGroupByRole(true);

    async void GroupMenu_Tapped(object sender, EventArgs e)
    {
        if (activeGroup == null)
            return;

        var page = GetHostPage();
        if (page == null)
            return;

        var groupMuted = false;
        try
        {
            groupMuted = await groupService.IsGroupNotificationsMutedAsync(activeGroup.Id);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"IsGroupNotificationsMutedAsync failed: {ex.Message}");
        }

        var muteLabel = groupMuted ? "Unmute notifications" : "Mute notifications";
        var action = await ActionMenuPopup.PickAsync(
            page,
            activeGroup.Name,
            new[]
            {
                "View members",
                "Share Invite Code",
                muteLabel,
                "Leave group",
            });

        if (string.IsNullOrEmpty(action))
            return;

        try
        {
            switch (action)
            {
                case "View members":
                    await ShowMembersViewAsync();
                    break;
                case "Share Invite Code":
                    await ShareInviteCodeAsync(page);
                    break;
                case "Mute notifications":
                    await groupService.SetGroupNotificationsMutedAsync(activeGroup.Id, muted: true);
                    Globals.ShowToastPopup("done", "Notifications muted for this group.", 90);
                    break;
                case "Unmute notifications":
                    await groupService.SetGroupNotificationsMutedAsync(activeGroup.Id, muted: false);
                    Globals.ShowToastPopup("done", "Notifications enabled for this group.", 90);
                    break;
                case "Leave group":
                    await LeaveActiveGroupAsync(page);
                    break;
            }
        }
        catch (Exception ex)
        {
            await page.DisplayAlert("Group", ex.Message, "OK");
        }
    }

    async Task ShareInviteCodeAsync(Page page)
    {
        if (activeGroup == null)
            return;

        var code = activeGroup.JoinCode?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            await page.DisplayAlert("Invite code", "This group does not have an invite code yet.", "OK");
            return;
        }

        var groupLabel = string.IsNullOrWhiteSpace(activeGroup.Name) ? "our group" : activeGroup.Name.Trim();
        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Title = "Share Invite Code",
            Text = $"Join {groupLabel} on MobiHymn with invite code: {code}",
        });
    }

    async Task LeaveActiveGroupAsync(Page page)
    {
        if (activeGroup == null)
            return;

        var groupLabel = string.IsNullOrWhiteSpace(activeGroup.Name) ? "this group" : activeGroup.Name;
        var confirm = await page.DisplayAlert(
            "Leave group",
            $"Leave {groupLabel}? You can rejoin later with the group code or ID.",
            "Leave",
            "Cancel");
        if (!confirm)
            return;

        SetBoardLoading(true);
        try
        {
            await groupService.LeaveGroupAsync(activeGroup.Id);
            currentView = GroupDashboardView.HymnLists;
            viewingListDetail = false;
            await LoadBoardAsync();
            Globals.ShowToastPopup("done", "Left group.", 90);
        }
        catch (Exception ex)
        {
            await page.DisplayAlert("Leave group", ex.Message, "OK");
        }
        finally
        {
            SetBoardLoading(false);
        }
    }

    async void BoardOptions_Tapped(object sender, EventArgs e)
    {
        if (!canEdit || activeGroup == null || currentView != GroupDashboardView.HymnListDetail)
            return;

        var action = await ActionMenuPopup.PickAsync(
            GetHostPage(),
            "Board options",
            new[]
            {
                "Sort sections",
                "Clear all hymns in this list",
                "Clear all sections in this list",
                "Clear all saved sections",
            });

        if (string.IsNullOrEmpty(action))
            return;

        try
        {
            switch (action)
            {
                case "Sort sections":
                    await PickSectionSortAsync();
                    break;
                case "Clear all hymns in this list":
                    if (!await ConfirmAsync("Clear all hymns", "Remove every hymn from this list? Unsaved sections will also be removed."))
                        return;
                    await boardService.ClearAllHymnsAsync(activeGroup.Id, boardContext.ActiveListId);
                    break;
                case "Clear all sections in this list":
                    if (!await ConfirmAsync("Clear all sections", "Remove every section from this list? Saved sections for other lists will remain."))
                        return;
                    await boardService.ClearAllSectionsInListAsync(activeGroup.Id, boardContext.ActiveListId);
                    break;
                case "Clear all saved sections":
                    if (!await ConfirmAsync("Clear all saved sections", "Clear saved sections for all lists and remove sections from this list?"))
                        return;
                    await boardService.ClearAllSectionsAsync(activeGroup.Id, boardContext.ActiveListId);
                    break;
            }
        }
        catch (Exception ex)
        {
            await Application.Current.MainPage.DisplayAlert("Board", ex.Message, "OK");
        }
    }

    static Page GetHostPage() =>
        Shell.Current?.CurrentPage ?? Application.Current?.MainPage;

    static async Task<bool> ConfirmAsync(string title, string message) =>
        await Application.Current.MainPage.DisplayAlert(title, message, "Continue", "Cancel");

    async Task PickSectionSortAsync()
    {
        if (!canEdit || activeGroup == null || currentView != GroupDashboardView.HymnListDetail)
            return;

        var modes = new[]
        {
            SectionSortMode.AddedOrder,
            SectionSortMode.DateNewest,
            SectionSortMode.DateOldest,
            SectionSortMode.NameAsc,
            SectionSortMode.NameDesc,
        };

        var options = modes
            .Select(m => m == sectionSortMode
                ? $"✓ {SectionSortHelper.GetDisplayLabel(m)}"
                : SectionSortHelper.GetDisplayLabel(m))
            .ToArray();

        var picked = await ActionMenuPopup.PickAsync(GetHostPage(), "Sort sections", options);
        if (string.IsNullOrEmpty(picked))
            return;

        var selected = modes.FirstOrDefault(m =>
            picked == SectionSortHelper.GetDisplayLabel(m)
            || picked == $"✓ {SectionSortHelper.GetDisplayLabel(m)}");

        if (selected == sectionSortMode)
            return;

        try
        {
            var template = await boardService.SetSectionSortAsync(
                activeGroup.Id, boardContext.ActiveListId, selected);
            ApplySavedSectionTemplate(template);

            var list = await boardService.GetHymnListAsync(activeGroup.Id, boardContext.ActiveListId);
            await ApplyHymnListAsync(list);
            Globals.ShowToastPopup("done", $"Sections sorted by {SectionSortHelper.GetDisplayLabel(selected).ToLowerInvariant()}.", 90);
        }
        catch (Exception ex)
        {
            await Application.Current.MainPage.DisplayAlert("Board", ex.Message, "OK");
        }
    }

    void Dimmer_Tapped(object sender, EventArgs e) => dashboardService.Close();

    async void SignIn_Clicked(object sender, EventArgs e)
    {
        dashboardService.Close();
        await Task.Delay(350);
        await Shell.Current.Navigation.PushModalAsync(new LoginPage());
    }

    void FindGroup_Clicked(object sender, EventArgs e) =>
        findGroupPanel.IsVisible = !findGroupPanel.IsVisible;

    async void JoinGroup_Clicked(object sender, EventArgs e)
    {
        var groupId = entGroupId.Text?.Trim();
        if (string.IsNullOrWhiteSpace(groupId))
        {
            ShowNoGroupsStatus("Enter a group ID.");
            return;
        }

        try
        {
            ShowNoGroupsStatus(null);
            var group = await groupService.JoinGroupByIdAsync(groupId);
            entGroupId.Text = string.Empty;
            findGroupPanel.IsVisible = false;
            await LoadBoardAsync();
            await GroupJoinWelcomePresenter.ShowIfNeededAsync(Shell.Current?.CurrentPage, group);
        }
        catch (Exception ex)
        {
            ShowNoGroupsStatus(ex.Message);
        }
    }

    async void CreateGroup_Clicked(object sender, EventArgs e)
    {
        var page = Application.Current?.MainPage;
        if (page == null)
            return;

        var name = await page.DisplayPromptAsync("Create group", "Enter a name for your worship group:", "Create", "Cancel");
        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            ShowNoGroupsStatus(null);
            var group = await groupService.CreateGroupAsync(name.Trim());
            await LoadBoardAsync();
            await GroupJoinWelcomePresenter.ShowIfNeededAsync(Shell.Current?.CurrentPage, group, isCreator: true);
        }
        catch (Exception ex)
        {
            ShowNoGroupsStatus(ex.Message);
        }
    }

    void ShowNoGroupsStatus(string message)
    {
        lblNoGroupsStatus.Text = message ?? string.Empty;
        lblNoGroupsStatus.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    void ApplyHymnListManageFlags()
    {
        foreach (var summary in hymnLists)
            summary.CanManage = canEdit;
    }

    void AddHymnListSummaries(IEnumerable<GroupHymnListSummary> summaries)
    {
        foreach (var summary in summaries)
        {
            summary.CanManage = canEdit;
            if (activeGroup != null)
                summary.UnreadCount = dashboardService.GetListUnreadCount(activeGroup.Id, summary.Id);
            hymnLists.Add(summary);
        }
    }

    async Task ChangeListDateAsync(GroupHymnListSummary summary = null)
    {
        summary ??= activeListSummary;
        if (!canEdit || activeGroup == null || summary == null)
            return;

        var page = GetHostPage();
        if (page == null)
            return;

        var initialDate = GroupHymnListDates.TryGetScheduledDate(
            summary.Id,
            summary.Name,
            out var scheduled)
            ? scheduled
            : summary.CreatedAt;

        var selectedDate = await CreateHymnListPopup.PickDateAsync(
            page,
            initialDate,
            date => boardService.HymnListExistsForDateAsync(activeGroup.Id, date, summary.Id),
            title: "Change date",
            message: "Choose a new date for this hymn list.",
            confirmLabel: "Save");

        if (!selectedDate.HasValue)
            return;

        SetBoardLoading(true);
        try
        {
            var oldListId = summary.Id;
            var wasViewing = currentView == GroupDashboardView.HymnListDetail
                && string.Equals(activeListSummary?.Id, oldListId, StringComparison.Ordinal);

            var list = await boardService.UpdateHymnListDateAsync(
                activeGroup.Id,
                oldListId,
                selectedDate.Value);

            if (wasViewing)
            {
                boardSubscription?.Dispose();
                boardSubscription = null;
                subscribedListId = null;
                boardContext.ActiveListId = list.Id;
            }

            var listsPage = await boardService.ListHymnListsAsync(activeGroup.Id);
            hymnLists.Clear();
            hymnListsHasMore = listsPage.HasMore;
            AddHymnListSummaries(listsPage.Items);

            if (wasViewing)
            {
                activeListSummary = hymnLists.FirstOrDefault(l => l.Id == list.Id);
                UpdateBoardViewMode();
                await LoadActiveListAsync();
            }

            Globals.ShowToastPopup("done", $"Date changed to {list.Name}.", 90);
        }
        catch (Exception ex)
        {
            await page.DisplayAlert("Board", ex.Message, "OK");
        }
        finally
        {
            SetBoardLoading(false);
        }
    }

    async Task DeleteListAsync(GroupHymnListSummary summary = null)
    {
        summary ??= activeListSummary;
        if (!canEdit || activeGroup == null || summary == null)
            return;

        var page = GetHostPage();
        if (page == null)
            return;

        var confirm = await page.DisplayAlert(
            "Delete hymn list",
            $"Delete {summary.Name}? This cannot be undone.",
            "Delete",
            "Cancel");
        if (!confirm)
            return;

        SetBoardLoading(true);
        try
        {
            var listId = summary.Id;
            var wasActive = string.Equals(activeListSummary?.Id, listId, StringComparison.Ordinal)
                || string.Equals(boardContext.ActiveListId, listId, StringComparison.Ordinal);

            await boardService.DeleteHymnListAsync(activeGroup.Id, listId);

            if (wasActive)
            {
                boardContext.ActiveListId = string.Empty;
                boardNavigation.Clear();
            }

            await LoadBoardAsync();
            Globals.ShowToastPopup("done", "Hymn list deleted.", 90);
        }
        catch (Exception ex)
        {
            await page.DisplayAlert("Board", ex.Message, "OK");
        }
        finally
        {
            SetBoardLoading(false);
        }
    }

    async void CreateHymnList_Tapped(object sender, EventArgs e)
    {
        if (!canEdit || activeGroup == null)
            return;

        var page = GetHostPage();
        if (page == null)
            return;

        var selectedDate = await CreateHymnListPopup.PickDateAsync(
            page,
            DateTime.Today,
            date => boardService.HymnListExistsForDateAsync(activeGroup.Id, date));

        if (!selectedDate.HasValue)
            return;

        try
        {
            var list = await boardService.CreateHymnListAsync(activeGroup.Id, selectedDate.Value);
            var summary = new GroupHymnListSummary
            {
                Id = list.Id,
                Name = list.Name,
                CreatedAt = list.CreatedAt,
                CreatedBy = list.CreatedBy,
                HymnCount = 0,
                EntryCount = 0,
                CanManage = canEdit,
            };
            hymnLists.Insert(0, summary);
            // New lists only show saved (template) sections — never "This list only" leftovers.
            await OpenHymnListAsync(summary, applySavedSections: true, stripUnsavedSections: true);
        }
        catch (Exception ex)
        {
            await page.DisplayAlert("Board", ex.Message, "OK");
        }
    }

    async void HymnList_Edit_Invoked(object sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not GroupHymnListSummary summary)
            return;

        await ChangeListDateAsync(summary);
    }

    async void HymnList_Delete_Invoked(object sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not GroupHymnListSummary summary)
            return;

        await DeleteListAsync(summary);
    }

    async void HymnList_Tapped(object sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not GroupHymnListSummary summary)
            return;

        await OpenHymnListAsync(summary);
    }

    async void AddHymn_Completed(object sender, EventArgs e) => await AddHymnAsync();

    void HymnNumber_HandlerChanged(object sender, EventArgs e) => ApplyHymnEntryPlatformSettings();

    void ApplyHymnEntryPlatformSettings()
    {
        if (entHymnNumber?.Handler?.PlatformView == null)
            return;

        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var textColor = (isDark ? Colors.White : (Color)Application.Current.Resources["PrimaryText"]).ToPlatform();
        var hintColor = (isDark ? Color.FromArgb("#888888") : (Color)Application.Current.Resources["Gray"]).ToPlatform();

#if ANDROID
        if (entHymnNumber.Handler.PlatformView is Android.Widget.EditText editText)
        {
            editText.Background = null;
            editText.SetBackgroundColor(Android.Graphics.Color.Transparent);
            editText.SetTextColor(textColor);
            editText.SetHintTextColor(hintColor);
            editText.InputType = Android.Text.InputTypes.ClassText
                | Android.Text.InputTypes.TextVariationVisiblePassword;
        }
#endif
#if IOS
        if (entHymnNumber.Handler.PlatformView is UIKit.UITextField textField)
        {
            textField.AutocorrectionType = UIKit.UITextAutocorrectionType.No;
            textField.SpellCheckingType = UIKit.UITextSpellCheckingType.No;
            textField.KeyboardType = UIKit.UIKeyboardType.AsciiCapable;
            textField.TextColor = textColor;
        }
#endif
    }

    async void HymnNumber_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (suppressHymnTextChanged)
            return;

        addHymnPickedFromDropdown = false;
        await UpdateHymnSuggestionsAsync(e.NewTextValue);
    }

    void HymnNumber_Unfocused(object sender, FocusEventArgs e)
    {
        if (addHymnPickedFromDropdown || !addHymnDropdownWasShown)
            return;

        var text = entHymnNumber?.Text?.Trim();
        if (string.IsNullOrEmpty(text))
            return;

        var hymns = Globals.Instance.HymnList;
        if (HymnNumberHelper.FindExact(text, hymns) != null)
            return;

        ClearHymnNumberOnly();
    }

    async Task UpdateHymnSuggestionsAsync(string query)
    {
        suggestionCts?.Cancel();

        if (string.IsNullOrWhiteSpace(query))
        {
            suggestionCts = null;
            addHymnDropdownWasShown = false;
            HideHymnSuggestions();
            return;
        }

        var cts = new CancellationTokenSource();
        suggestionCts = cts;
        var token = cts.Token;
        var hymnList = Globals.Instance.HymnList;

        try
        {
            // Debounce rapid keystrokes, then run the (potentially heavy) lyric scan
            // off the UI thread so typing/focus stays responsive.
            await Task.Delay(SuggestionDebounceMs, token);
            var results = await Task.Run(
                () => HymnNumberHelper.GetSuggestions(query, hymnList),
                token);

            if (token.IsCancellationRequested)
                return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (token.IsCancellationRequested)
                    return;

                hymnSuggestions.Clear();
                foreach (var suggestion in results)
                    hymnSuggestions.Add(suggestion);

                addHymnDropdownWasShown = hymnSuggestions.Count > 0;
                suggestionsPanel.IsVisible = hymnSuggestions.Count > 0;
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    void HideHymnSuggestions()
    {
        suggestionCts?.Cancel();
        suggestionCts = null;
        hymnSuggestions.Clear();
        suggestionsPanel.IsVisible = false;
    }

    void ClearHymnNumberOnly()
    {
        suppressHymnTextChanged = true;
        entHymnNumber.Text = string.Empty;
        suppressHymnTextChanged = false;
        addHymnDropdownWasShown = false;
        addHymnPickedFromDropdown = false;
        HideHymnSuggestions();
    }

    void ClearHymnEntry()
    {
        suppressHymnTextChanged = true;
        entHymnNumber.Text = string.Empty;
        suppressHymnTextChanged = false;
        if (entHymnNote != null)
            entHymnNote.Text = string.Empty;
        addHymnDropdownWasShown = false;
        addHymnPickedFromDropdown = false;
        HideHymnSuggestions();
    }

    void HymnSuggestions_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not HymnSuggestion suggestion)
            return;

        // Defer the work: mutating the bound collection while the CollectionView is
        // still processing this SelectionChanged callback crashes the Android RecyclerView.
        // Selecting a suggestion only fills the input; the user taps "Add hymn" to commit
        // (so they still have a chance to add a note first).
        MainThread.BeginInvokeOnMainThread(() =>
        {
            hymnSuggestionsList.SelectedItem = null;
            suppressHymnTextChanged = true;
            entHymnNumber.Text = suggestion.Number;
            suppressHymnTextChanged = false;
            addHymnPickedFromDropdown = true;
            addHymnDropdownWasShown = false;
            HideHymnSuggestions();
        });
    }

    static bool IsNumberQuery(string text) =>
        !string.IsNullOrWhiteSpace(text) && HymnNumberHelper.IsNumberQuery(text);

    async void AddSection_Completed(object sender, EventArgs e) => await AddSectionAsync();

    async Task AddSectionAsync()
    {
        if (!canEdit || activeGroup == null)
            return;

        var name = entSectionName?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (hymns.Any(h => h.IsSection
            && string.Equals(h.SectionName, name, StringComparison.OrdinalIgnoreCase)))
        {
            await Application.Current.MainPage.DisplayAlert("Board", "A section with that name already exists.", "OK");
            return;
        }

        try
        {
            await boardService.AddSectionAsync(activeGroup.Id, boardContext.ActiveListId, name);

            if (chkSectionThisDayOnly?.IsChecked != true)
            {
                var template = await boardService.SaveSectionToTemplateAsync(
                    activeGroup.Id, boardContext.ActiveListId, name);
                ApplySavedSectionTemplate(template);
                Globals.ShowToastPopup("done", "Section saved for all dates.", 90);
            }

            entSectionName.Text = string.Empty;
        }
        catch (Exception ex)
        {
            await Application.Current.MainPage.DisplayAlert("Board", ex.Message, "OK");
        }
    }

    async Task AddHymnAsync(string hymnNumber = null)
    {
        if (!canEdit || activeGroup == null)
            return;

        if (hymnNumber == null && string.IsNullOrWhiteSpace(entHymnNumber.Text))
            return;

        var hymn = hymnNumber != null
            ? HymnNumberHelper.FindExact(hymnNumber, Globals.Instance.HymnList)
            : HymnNumberHelper.ResolveHymn(entHymnNumber.Text, Globals.Instance.HymnList);
        if (hymn == null)
        {
            if (addHymnDropdownWasShown && !addHymnPickedFromDropdown)
                ClearHymnNumberOnly();

            await Application.Current.MainPage.DisplayAlert("Board",
                IsNumberQuery(entHymnNumber.Text)
                    ? "Hymn not found."
                    : "No matching hymn. Pick one from the list or refine your search.",
                "OK");
            return;
        }

        try
        {
            var note = string.IsNullOrWhiteSpace(entHymnNote?.Text) ? null : entHymnNote.Text.Trim();
            var keepOpen = addHymnPanelExpanded;
            await boardService.AddHymnAsync(activeGroup.Id, boardContext.ActiveListId, hymn.Number, note);
            keepAddHymnPanelAfterLoad = keepOpen;
            ClearHymnEntry();
        }
        catch (Exception ex)
        {
            await Application.Current.MainPage.DisplayAlert("Board", ex.Message, "OK");
        }
    }

    bool CanModify(BoardHymnEntry entry)
    {
        if (entry == null || entry.IsDeleted)
            return false;
        if (canEdit)
            return true;

        var uid = auth.CurrentUserId;
        return !string.IsNullOrEmpty(uid)
            && string.Equals(entry.AddedBy, uid, StringComparison.Ordinal);
    }

    async void RemoveHymn_Invoked(object sender, EventArgs e)
    {
        if (activeGroup == null || (sender as BindableObject)?.BindingContext is not BoardHymnEntry entry)
            return;

        await RemoveBoardEntryAsync(entry);
    }

    async Task RemoveBoardEntryAsync(BoardHymnEntry entry)
    {
        if (activeGroup == null || entry == null)
            return;

        if (!CanModify(entry))
        {
            await Application.Current.MainPage.DisplayAlert("Board",
                entry.IsSection ? "You can only remove sections you added." : "You can only remove hymns you added.",
                "OK");
            return;
        }

        if (entry.IsSection)
        {
            await RemoveSectionAsync(entry);
            return;
        }

        var confirm = await Application.Current.MainPage.DisplayAlert(
            "Remove hymn",
            $"Remove Hymn #{entry.HymnNumber} from the board?",
            "Remove", "Cancel");
        if (!confirm)
            return;

        try
        {
            await boardService.RemoveHymnAsync(activeGroup.Id, boardContext.ActiveListId, entry.Id);
        }
        catch (Exception ex)
        {
            await Application.Current.MainPage.DisplayAlert("Board", ex.Message, "OK");
        }
    }

    async Task RemoveSectionAsync(BoardHymnEntry entry)
    {
        var template = await boardService.GetSectionTemplateAsync(activeGroup.Id);
        var isSaved = template.SectionNames.Any(n =>
            string.Equals(n?.Trim(), entry.SectionName?.Trim(), StringComparison.OrdinalIgnoreCase));

        var hymnCount = CountHymnsInSection(entry);
        var confirmation = await RemoveSectionPopup.ConfirmAsync(
            GetHostPage(),
            entry.SectionName,
            hymnCount,
            isSaved);
        if (confirmation == null)
            return;

        try
        {
            if (isSaved && !confirmation.CurrentDateOnly)
                await boardService.RemoveSectionFromTemplateAsync(activeGroup.Id, entry.SectionName);

            await boardService.RemoveSectionAsync(
                activeGroup.Id,
                boardContext.ActiveListId,
                entry.Id,
                confirmation.DeleteHymnsInSection);

            if (isSaved && !confirmation.CurrentDateOnly)
                await RefreshSavedSectionNamesAsync();
        }
        catch (Exception ex)
        {
            await Application.Current.MainPage.DisplayAlert("Board", ex.Message, "OK");
        }
    }

    int CountHymnsInSection(BoardHymnEntry section)
    {
        var index = hymns.IndexOf(section);
        if (index < 0)
            return 0;

        var count = 0;
        for (var i = index + 1; i < hymns.Count; i++)
        {
            if (hymns[i].IsSection)
                break;
            count++;
        }

        return count;
    }

    async void SaveSection_Invoked(object sender, EventArgs e)
    {
        if (!canEdit || activeGroup == null || (sender as BindableObject)?.BindingContext is not BoardHymnEntry entry || !entry.IsSection)
            return;

        try
        {
            var template = await boardService.SaveSectionToTemplateAsync(activeGroup.Id, boardContext.ActiveListId, entry.SectionName);
            ApplySavedSectionTemplate(template);
            Globals.ShowToastPopup("done", "Section saved.", 90);
        }
        catch (Exception ex)
        {
            await Application.Current.MainPage.DisplayAlert("Board", ex.Message, "OK");
        }
    }

    async void UnsaveSection_Invoked(object sender, EventArgs e)
    {
        if (!canEdit || activeGroup == null || (sender as BindableObject)?.BindingContext is not BoardHymnEntry entry || !entry.IsSection)
            return;

        try
        {
            await boardService.RemoveSectionFromTemplateAsync(activeGroup.Id, entry.SectionName);
            await RefreshSavedSectionNamesAsync();
            Globals.ShowToastPopup("done", "Section unsaved.", 90);
        }
        catch (Exception ex)
        {
            await Application.Current.MainPage.DisplayAlert("Board", ex.Message, "OK");
        }
    }

    async void EditHymn_Invoked(object sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is BoardHymnEntry entry && entry.IsSection)
            await BeginEditAsync(sender, revealNote: false);
        else
            await BeginEditAsync(sender, revealNote: true);
    }

    async Task BeginEditAsync(object sender, bool revealNote)
    {
        if (activeGroup == null || (sender as BindableObject)?.BindingContext is not BoardHymnEntry entry)
            return;

        if (!CanModify(entry))
        {
            await Application.Current.MainPage.DisplayAlert("Board",
                entry.IsSection ? "You can only edit sections you added." : "You can only edit hymns you added.",
                "OK");
            return;
        }

        // Only one card may be in edit mode at a time.
        foreach (var other in hymns.Where(h => h != entry && h.IsEditing))
            CollapseEdit(other);

        editingEntry = entry;
        suppressEditTextChanged = true;
        entry.EditText = entry.IsSection ? entry.SectionName : entry.HymnNumber;
        suppressEditTextChanged = false;
        entry.EditNote = entry.Notes;
        entry.ShowEditNote = !entry.IsSection && (revealNote || !string.IsNullOrWhiteSpace(entry.Notes));
        entry.EditSuggestions.Clear();
        entry.HasEditSuggestions = false;
        entry.IsEditing = true;
    }

    void CollapseEdit(BoardHymnEntry entry)
    {
        if (entry == null)
            return;
        editSuggestionCts?.Cancel();
        editSuggestionCts = null;
        entry.IsEditing = false;
        entry.ShowEditNote = false;
        entry.HasEditSuggestions = false;
        entry.EditSuggestions.Clear();
        if (editingEntry == entry)
            editingEntry = null;
    }

    void CancelEdit_Clicked(object sender, EventArgs e)
    {
        var entry = (sender as BindableObject)?.BindingContext as BoardHymnEntry ?? editingEntry;
        if (entry == null)
            return;

        suppressEditTextChanged = true;
        entry.EditText = entry.IsSection ? entry.SectionName : entry.HymnNumber;
        suppressEditTextChanged = false;
        CollapseEdit(entry);
    }

    async void EditEntry_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (suppressEditTextChanged)
            return;

        var entry = (sender as BindableObject)?.BindingContext as BoardHymnEntry ?? editingEntry;
        if (entry == null || entry.IsSection)
            return;

        await UpdateEditSuggestionsAsync(entry, e.NewTextValue);
    }

    async Task UpdateEditSuggestionsAsync(BoardHymnEntry entry, string query)
    {
        editSuggestionCts?.Cancel();

        if (entry == null)
            return;

        if (string.IsNullOrWhiteSpace(query))
        {
            editSuggestionCts = null;
            entry.EditSuggestions.Clear();
            entry.HasEditSuggestions = false;
            return;
        }

        var cts = new CancellationTokenSource();
        editSuggestionCts = cts;
        var token = cts.Token;
        var hymnList = Globals.Instance.HymnList;

        try
        {
            await Task.Delay(SuggestionDebounceMs, token);
            var results = await Task.Run(
                () => HymnNumberHelper.GetSuggestions(query, hymnList),
                token);

            if (token.IsCancellationRequested || editingEntry != entry)
                return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (token.IsCancellationRequested || editingEntry != entry)
                    return;

                entry.EditSuggestions.Clear();
                foreach (var suggestion in results)
                    entry.EditSuggestions.Add(suggestion);
                entry.HasEditSuggestions = entry.EditSuggestions.Count > 0;
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    void EditSuggestion_Tapped(object sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not HymnSuggestion suggestion)
            return;

        var entry = editingEntry;
        if (entry == null || entry.IsSection)
            return;

        suppressEditTextChanged = true;
        entry.EditText = suggestion.Number;
        suppressEditTextChanged = false;
        entry.EditSuggestions.Clear();
        entry.HasEditSuggestions = false;
        editSuggestionCts?.Cancel();
        editSuggestionCts = null;
    }

    async void SaveEdit_Clicked(object sender, EventArgs e)
    {
        var entry = (sender as BindableObject)?.BindingContext as BoardHymnEntry ?? editingEntry;
        if (entry == null)
            return;

        if (entry.IsSection)
        {
            await CommitSectionEditAsync(entry);
            return;
        }

        var hymn = HymnNumberHelper.ResolveHymn(entry.EditText, Globals.Instance.HymnList);
        if (hymn == null)
        {
            await Application.Current.MainPage.DisplayAlert("Board", "No matching hymn found.", "OK");
            return;
        }

        await CommitEditAsync(entry, hymn.Number);
    }

    async Task CommitEditAsync(BoardHymnEntry entry, string newNumber)
    {
        if (activeGroup == null || entry == null || string.IsNullOrWhiteSpace(newNumber))
            return;

        if (!CanModify(entry))
        {
            await Application.Current.MainPage.DisplayAlert("Board", "You can only edit hymns you added.", "OK");
            return;
        }

        var newNote = string.IsNullOrWhiteSpace(entry.EditNote) ? string.Empty : entry.EditNote.Trim();
        var numberUnchanged = string.Equals(newNumber, entry.HymnNumber, StringComparison.OrdinalIgnoreCase);
        var noteUnchanged = string.Equals(newNote, entry.Notes ?? string.Empty, StringComparison.Ordinal);

        if (numberUnchanged && noteUnchanged)
        {
            CollapseEdit(entry);
            return;
        }

        if (!numberUnchanged && hymns.Any(h => !h.IsSection && h.Id != entry.Id
            && string.Equals(h.HymnNumber, newNumber, StringComparison.OrdinalIgnoreCase)))
        {
            await Application.Current.MainPage.DisplayAlert("Board", "That hymn is already on the board.", "OK");
            return;
        }

        try
        {
            var updated = new BoardHymnEntry
            {
                Id = entry.Id,
                IsSection = false,
                HymnNumber = newNumber,
                SortOrder = entry.SortOrder,
                Notes = newNote,
                AddedBy = entry.AddedBy,
                AddedByName = entry.AddedByName,
                UpdatedAt = DateTime.UtcNow,
            };
            CollapseEdit(entry);
            await boardService.UpdateHymnAsync(activeGroup.Id, boardContext.ActiveListId, updated);
        }
        catch (Exception ex)
        {
            await Application.Current.MainPage.DisplayAlert("Board", ex.Message, "OK");
        }
    }

    async Task CommitSectionEditAsync(BoardHymnEntry entry)
    {
        if (activeGroup == null || entry == null || !entry.IsSection)
            return;

        if (!CanModify(entry))
        {
            await Application.Current.MainPage.DisplayAlert("Board", "You can only edit sections you added.", "OK");
            return;
        }

        var newName = string.IsNullOrWhiteSpace(entry.EditText) ? string.Empty : entry.EditText.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            await Application.Current.MainPage.DisplayAlert("Board", "Enter a section name.", "OK");
            return;
        }

        if (string.Equals(newName, entry.SectionName, StringComparison.Ordinal))
        {
            CollapseEdit(entry);
            return;
        }

        try
        {
            var updated = new BoardHymnEntry
            {
                Id = entry.Id,
                IsSection = true,
                SectionName = newName,
                SortOrder = entry.SortOrder,
                AddedBy = entry.AddedBy,
                AddedByName = entry.AddedByName,
                UpdatedAt = DateTime.UtcNow,
            };
            CollapseEdit(entry);
            await boardService.UpdateHymnAsync(activeGroup.Id, boardContext.ActiveListId, updated);
        }
        catch (Exception ex)
        {
            await Application.Current.MainPage.DisplayAlert("Board", ex.Message, "OK");
        }
    }

    async void HymnEntry_Tapped(object sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not BoardHymnEntry entry)
            return;

        // Inline edit controls (X, suggestions, etc.) sit inside this card — don't treat
        // those taps as "open hymn" (which closes the board pane).
        if (entry.IsEditing)
            return;

        await OpenHymnFromBoardAsync(entry);
    }

    async Task OpenHymnFromBoardAsync(BoardHymnEntry entry)
    {
        if (entry == null || activeGroup == null || entry.IsSection || entry.IsDeleted)
            return;

        var numbers = hymns.Where(h => !h.IsSection && !h.IsDeleted).Select(h => h.HymnNumber).ToList();
        var index = numbers.FindIndex(n => string.Equals(n, entry.HymnNumber, StringComparison.OrdinalIgnoreCase));
        boardNavigation.Set(activeGroup.Id, boardContext.ActiveListId, numbers, Math.Max(0, index));

        var hymnLookup = GetHymnNumberLookup();
        if (!hymnLookup.TryGetValue(entry.HymnNumber?.Trim() ?? string.Empty, out var hymn))
            return;

        Globals.Instance.ActiveHymn = hymn;
        dashboardService.Close();

        if (Shell.Current != null)
            await Shell.Current.GoToAsync($"//{Routes.READ}");
    }
}
