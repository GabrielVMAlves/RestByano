using System.Windows;
using System.Windows.Controls;

namespace restByAno.Desktop.Services;

/// <summary>
/// Adapter that lets a <see cref="PasswordBox"/> take part in MVVM binding. The text lives only in memory in the bound
/// property (never in saved view-model JSON); binding an empty string clears the control.
/// </summary>
public static class PasswordBoxBinder
{
    public static readonly DependencyProperty BoundPasswordProperty = DependencyProperty.RegisterAttached(
        "BoundPassword", typeof(string), typeof(PasswordBoxBinder),
        // Default is null (not ""), so the very first binding of an empty string still raises the change callback
        // that attaches the PasswordChanged handler.
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached("IsUpdating", typeof(bool), typeof(PasswordBoxBinder));

    public static string GetBoundPassword(DependencyObject element) => (string?)element.GetValue(BoundPasswordProperty) ?? string.Empty;

    public static void SetBoundPassword(DependencyObject element, string value) => element.SetValue(BoundPasswordProperty, value);

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;

        box.PasswordChanged -= OnPasswordChanged;
        var value = e.NewValue as string ?? string.Empty;
        if (!(bool)box.GetValue(IsUpdatingProperty) && box.Password != value)
            box.Password = value;
        box.PasswordChanged += OnPasswordChanged;
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        box.SetValue(IsUpdatingProperty, true);
        SetBoundPassword(box, box.Password);
        box.SetValue(IsUpdatingProperty, false);
    }
}
