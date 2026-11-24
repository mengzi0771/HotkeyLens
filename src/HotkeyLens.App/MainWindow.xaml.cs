using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HotkeyLens.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => UpdateMaximizeButton();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || FindButton(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 鼠标在拖动开始前已经松开时忽略。
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void UpdateMaximizeButton()
    {
        if (MaximizeButton is not null)
        {
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
            MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "还原" : "最大化";
        }
    }

    private static Button? FindButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button button)
            {
                return button;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }
}