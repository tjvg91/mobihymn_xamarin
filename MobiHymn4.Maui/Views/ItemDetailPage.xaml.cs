using System.ComponentModel;
using Microsoft.Maui.Controls;
using MobiHymn4.ViewModels;

namespace MobiHymn4.Views
{
    public partial class ItemDetailPage : ContentPage
    {
        public ItemDetailPage()
        {
            InitializeComponent();
            BindingContext = new ItemDetailViewModel();
        }
    }
}
