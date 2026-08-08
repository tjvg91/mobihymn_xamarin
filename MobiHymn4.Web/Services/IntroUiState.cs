namespace MobiHymn4.Web.Services;

/// <summary>UI signal to replay the first-run intro slider (About → View Features Tour).</summary>
public sealed class IntroUiState
{
    public event Action? ReplayRequested;

    public void RequestReplay() => ReplayRequested?.Invoke();
}
