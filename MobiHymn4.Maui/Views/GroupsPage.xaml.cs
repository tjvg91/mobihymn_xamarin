using MobiHymn4.Models;
using MobiHymn4.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;

namespace MobiHymn4.Views;

[XamlCompilation(XamlCompilationOptions.Compile)]
public partial class GroupsPage : ContentPage
{
    public GroupsPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is GroupsViewModel vm)
            vm.RefreshCommand.Execute(null);
    }

    void GroupRow_Tapped(object sender, TappedEventArgs e)
    {
        if (sender is not BindableObject bindable)
            return;

        if (bindable.BindingContext is WorshipGroup group && BindingContext is GroupsViewModel vm)
            vm.OpenGroupCommand.Execute(group);
    }

    async void GroupLeave_Invoked(object sender, EventArgs e)
    {
        if (BindingContext is not GroupsViewModel vm)
            return;

        WorshipGroup group = null;
        if (sender is SwipeItemView swipeItem)
            group = swipeItem.BindingContext as WorshipGroup;
        else if (sender is BindableObject bindable)
            group = bindable.BindingContext as WorshipGroup;

        if (group != null)
            await vm.LeaveGroupAsync(group);
    }
}
