using System.Windows;
using System.Windows.Input;

namespace HotkeyLens.App.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string confirmLabel)
    {
        InitializeComponent();
        DialogTitle.Text = title;
        DialogMessage.Text = message;
        ConfirmButton.Content = confirmLabel;
    }

    public static bool Show(Window? owner, string title, string message, string confirmLabel)
    {
        var dialog = new ConfirmDialog(title, message, confirmLabel);
        if (owner is not null && owner.IsVisible)
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}