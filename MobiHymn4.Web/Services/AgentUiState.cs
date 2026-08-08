namespace MobiHymn4.Web.Services;

/// <summary>Toolbar chrome hooks for the Selah (agent) page.</summary>
public sealed class AgentUiState
{
    public event Action? OpenSettingsRequested;

    public void RequestOpenSettings() => OpenSettingsRequested?.Invoke();
}
