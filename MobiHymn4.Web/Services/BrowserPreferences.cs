using Microsoft.JSInterop;
using MobiHymn4.Shared;
using MobiHymn4.Shared.Services;

namespace MobiHymn4.Web.Services;

public sealed class BrowserPreferences : IAppPreferences
{
    readonly IJSInProcessRuntime js;

    public BrowserPreferences(IJSRuntime js) => this.js = (IJSInProcessRuntime)js;

    public string Get(string key, string defaultValue = "") =>
        js.Invoke<string?>("mobihymnPrefs.get", key) ?? defaultValue;

    public void Set(string key, string value) =>
        js.InvokeVoid("mobihymnPrefs.set", key, value);

    public void Remove(string key) =>
        js.InvokeVoid("mobihymnPrefs.remove", key);

    public bool GetBool(string key, bool defaultValue = false)
    {
        var text = Get(key, defaultValue ? "true" : "false");
        return bool.TryParse(text, out var b) ? b : defaultValue;
    }

    public void SetBool(string key, bool value) => Set(key, value ? "true" : "false");

    public void ClearPrefix(string prefix) =>
        js.InvokeVoid("mobihymnPrefs.clearPrefix", prefix);
}
