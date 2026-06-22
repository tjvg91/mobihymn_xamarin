using System.ComponentModel;

namespace MobiHymn4.Models;

public class GridOptions : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    string text = string.Empty;
    public string Text
    {
        get => text;
        set
        {
            if (text == value)
                return;
            text = value;
            OnPropertyChanged(nameof(Text));
        }
    }

    object index = -1;
    public object Index
    {
        get => index;
        set
        {
            index = value;
            OnPropertyChanged(nameof(Index));
        }
    }

    bool isActive;
    public bool IsActive
    {
        get => isActive;
        set
        {
            if (isActive == value)
                return;
            isActive = value;
            OnPropertyChanged(nameof(IsActive));
        }
    }

    void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
