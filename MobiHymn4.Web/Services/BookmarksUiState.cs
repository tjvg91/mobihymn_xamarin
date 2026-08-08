namespace MobiHymn4.Web.Services;

/// <summary>Toolbar chrome for Bookmarks pages (mirrors MAUI group + items toolbars).</summary>
public sealed class BookmarksUiState
{
    int _owner;

    public bool IsActive { get; private set; }
    public bool IsGroupList { get; private set; }
    public bool SelectionMode { get; private set; }
    public bool HasGroups { get; private set; }
    public bool HasItems { get; private set; }
    public bool CanSelectAll { get; private set; }
    public bool CanDelete { get; private set; }
    public bool CanMove { get; private set; }
    public string Title { get; private set; } = "Bookmarks";

    public event Action? Changed;

    public Action? OnEnterSelection { get; set; }
    public Action? OnSelectAll { get; set; }
    public Action? OnMoveSelected { get; set; }
    public Action? OnDeleteSelected { get; set; }
    public Action? OnCancelSelection { get; set; }

    /// <summary>Claim exclusive chrome ownership (invalidates any previous page).</summary>
    public int ClaimOwner() => ++_owner;

    public bool Owns(int owner) => owner == _owner;

    public void SetGroupList(int owner, bool hasGroups, bool selectionMode, bool canSelectAll, bool canDelete)
    {
        if (!Owns(owner)) return;
        IsActive = true;
        IsGroupList = true;
        Title = "Bookmarks";
        HasGroups = hasGroups;
        HasItems = false;
        SelectionMode = selectionMode;
        CanSelectAll = canSelectAll;
        CanDelete = canDelete;
        CanMove = false;
        OnMoveSelected = null;
        Changed?.Invoke();
    }

    public void SetGroupItems(
        int owner,
        string groupName,
        bool hasItems,
        bool selectionMode,
        bool canSelectAll,
        bool canMove,
        bool canDelete)
    {
        if (!Owns(owner)) return;
        IsActive = true;
        IsGroupList = false;
        Title = string.IsNullOrWhiteSpace(groupName) ? "Bookmarks" : groupName;
        HasGroups = false;
        HasItems = hasItems;
        SelectionMode = selectionMode;
        CanSelectAll = canSelectAll;
        CanMove = canMove;
        CanDelete = canDelete;
        Changed?.Invoke();
    }

    public void Release(int owner)
    {
        if (!Owns(owner)) return;
        IsActive = false;
        IsGroupList = false;
        SelectionMode = false;
        HasGroups = false;
        HasItems = false;
        CanSelectAll = false;
        CanDelete = false;
        CanMove = false;
        Title = "Bookmarks";
        OnEnterSelection = null;
        OnSelectAll = null;
        OnMoveSelected = null;
        OnDeleteSelected = null;
        OnCancelSelection = null;
        Changed?.Invoke();
    }
}
