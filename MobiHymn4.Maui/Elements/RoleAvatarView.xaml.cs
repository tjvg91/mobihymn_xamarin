using System.Collections.Generic;
using MobiHymn4.Utils;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MobiHymn4.Elements;

public partial class RoleAvatarView : ContentView
{
    public RoleAvatarView()
    {
        InitializeComponent();
        ApplyAppearance();
    }

    public static readonly BindableProperty RolesProperty = BindableProperty.Create(
        nameof(Roles),
        typeof(IList<UserRole>),
        typeof(RoleAvatarView),
        null,
        propertyChanged: OnRolesChanged);

    public static readonly BindableProperty SizeProperty = BindableProperty.Create(
        nameof(Size),
        typeof(double),
        typeof(RoleAvatarView),
        48.0,
        propertyChanged: OnSizeChanged);

    public IList<UserRole> Roles
    {
        get => (IList<UserRole>)GetValue(RolesProperty);
        set => SetValue(RolesProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    static void OnRolesChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((RoleAvatarView)bindable).ApplyAppearance();
    }

    static void OnSizeChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((RoleAvatarView)bindable).ApplySize();
    }

    void ApplySize()
    {
        var size = Size > 0 ? Size : 48;
        WidthRequest = size;
        HeightRequest = size;
        avatarBorder.WidthRequest = size;
        avatarBorder.HeightRequest = size;
        avatarImage.WidthRequest = size * 0.55;
        avatarImage.HeightRequest = size * 0.55;
    }

    void ApplyAppearance()
    {
        ApplySize();

        var roles = Roles;
        var glyph = RoleAvatarHelper.GetIconGlyph(roles);
        var tint = RoleAvatarHelper.GetTintColor(roles);

        avatarBorder.Stroke = tint;
        avatarBorder.BackgroundColor = tint.WithAlpha(0.15f);
        avatarImage.Source = new FontImageSource
        {
            FontFamily = "FAS",
            Glyph = glyph,
            Size = Size > 0 ? Size * 0.42 : 20,
            Color = tint,
        };
    }
}
