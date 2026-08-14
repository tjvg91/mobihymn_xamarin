using Microsoft.AspNetCore.Components;
using MobiHymn4.Shared;
using MobiHymn4.Shared.Services;

namespace MobiHymn4.Web.Services;

/// <summary>
/// Opens a group/board from a deep link after auth + membership checks.
/// Shareable URLs keep the reader mounted:
/// <c>/read/{n}?groupId=...&amp;listId=...</c>
/// Legacy <c>/groups/...</c> routes redirect here.
/// </summary>
public sealed class BoardDeepLinkService
{
    readonly IAuthService auth;
    readonly AuthNavigation authNav;
    readonly IGroupService groups;
    readonly BoardUiState boardUi;
    readonly AppState appState;
    readonly IAppPreferences prefs;
    readonly NavigationManager nav;

    public BoardDeepLinkService(
        IAuthService auth,
        AuthNavigation authNav,
        IGroupService groups,
        BoardUiState boardUi,
        AppState appState,
        IAppPreferences prefs,
        NavigationManager nav)
    {
        this.auth = auth;
        this.authNav = authNav;
        this.groups = groups;
        this.boardUi = boardUi;
        this.appState = appState;
        this.prefs = prefs;
        this.nav = nav;
    }

    /// <summary>
    /// Reader deep link with optional board query params.
    /// Example: <c>read/549?groupId=abc&amp;listId=2026-08-09</c>
    /// </summary>
    public static string BoardPath(string? groupId, string? listId = null, string? hymnNumber = null)
    {
        var n = (hymnNumber ?? "").Trim();
        var path = string.IsNullOrEmpty(n)
            ? "read"
            : $"read/{Uri.EscapeDataString(n)}";

        var gid = (groupId ?? "").Trim();
        var lid = (listId ?? "").Trim();
        if (string.IsNullOrEmpty(gid) && string.IsNullOrEmpty(lid))
            return path;

        var parts = new List<string>(2);
        if (!string.IsNullOrEmpty(gid))
            parts.Add($"groupId={Uri.EscapeDataString(gid)}");
        if (!string.IsNullOrEmpty(lid))
            parts.Add($"listId={Uri.EscapeDataString(lid)}");
        return $"{path}?{string.Join("&", parts)}";
    }

    /// <summary>Board href using the currently open / last hymn number.</summary>
    public string BoardHref(string? groupId, string? listId = null) =>
        BoardPath(groupId, listId, CurrentHymnNumber());

    public string CurrentHymnNumber()
    {
        var n = appState.ActiveHymn?.Number
            ?? appState.Settings.LastHymnNumber
            ?? prefs.Get(PrefKeys.LastHymnNumber, "1");
        n = (n ?? "").Trim();
        return string.IsNullOrWhiteSpace(n) ? "1" : n;
    }

    /// <summary>Legacy path-style board URLs (still accepted; redirected to <see cref="BoardHref"/>).</summary>
    public static string LegacyGroupsPath(string groupId, string? listId = null)
    {
        var gid = (groupId ?? "").Trim();
        if (string.IsNullOrEmpty(gid))
            return "groups";
        var lid = (listId ?? "").Trim();
        return string.IsNullOrEmpty(lid)
            ? $"groups/{gid}"
            : $"groups/{gid}/boards/{lid}";
    }

    /// <summary>
    /// Returns null on success; "redirect" when navigating to login/verify;
    /// otherwise a user-facing error (e.g. not a member).
    /// </summary>
    public async Task<string?> TryOpenAsync(string? groupId, string? listId, bool replaceHome = true)
    {
        _ = replaceHome;

        for (var i = 0; i < 40 && !auth.IsSignedIn; i++)
            await Task.Delay(50);

        var gid = Uri.UnescapeDataString((groupId ?? "").Trim());
        var lidRaw = string.IsNullOrWhiteSpace(listId) ? null : Uri.UnescapeDataString(listId.Trim());
        var lid = string.IsNullOrWhiteSpace(lidRaw) ? null : lidRaw;
        var returnPath = BoardHref(gid, lid);

        if (!auth.IsSignedIn)
        {
            nav.NavigateTo(authNav.LoginUrl(returnPath), forceLoad: false, replace: true);
            return "redirect";
        }

        if (!auth.IsEmailVerified)
        {
            nav.NavigateTo(authNav.VerifyUrl(returnPath), forceLoad: false, replace: true);
            return "redirect";
        }

        if (string.IsNullOrEmpty(gid))
        {
            appState.SetReadView(true);
            boardUi.Open(null, null);
            return null;
        }

        IReadOnlyList<Shared.Models.WorshipGroupDoc> mine;
        try
        {
            mine = await groups.GetMyGroupsAsync();
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        var member = mine.Any(g => string.Equals(g.Id, gid, StringComparison.OrdinalIgnoreCase));
        if (!member)
            return "You’re not a member of this group. Ask a teammate for an invite code, then join from Worship groups.";

        appState.SetReadView(true);
        boardUi.Open(gid, lid);
        return null;
    }
}
