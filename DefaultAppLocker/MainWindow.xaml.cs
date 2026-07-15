using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace DefaultAppLocker;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        ApplyWindows11WindowBackdrop();
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.InitializeAsync().ConfigureAwait(true);
    }

    private void ApplyWindows11WindowBackdrop()
    {
        Background = Brushes.Transparent;
    }

    private void TitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        DragMove();
    }

    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseClick(object sender, RoutedEventArgs e) => Close();


    private void WindowChromeRootLoaded(object sender, RoutedEventArgs e) => UpdateWindowChromeRootClip();

    private void WindowChromeRootSizeChanged(object sender, SizeChangedEventArgs e) => UpdateWindowChromeRootClip();

    private void UpdateWindowChromeRootClip()
    {
        if (WindowChromeRoot.ActualWidth <= 0 || WindowChromeRoot.ActualHeight <= 0) return;
        WindowChromeRoot.Clip = new RectangleGeometry(new Rect(0, 0, WindowChromeRoot.ActualWidth, WindowChromeRoot.ActualHeight), 24, 24);
    }
    private void NavigationChecked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        HomePage.Visibility = sender == HomeNav ? Visibility.Visible : Visibility.Collapsed;
        SnapshotPage.Visibility = sender == SnapshotNav ? Visibility.Visible : Visibility.Collapsed;
        ComparePage.Visibility = sender == CompareNav ? Visibility.Visible : Visibility.Collapsed;
        QuickPage.Visibility = sender == QuickNav ? Visibility.Visible : Visibility.Collapsed;
        LockPage.Visibility = sender == LockNav ? Visibility.Visible : Visibility.Collapsed;
        HelpPage.Visibility = sender == HelpNav ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DataGridPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Shift)
        {
            if (TryScrollDataGridHorizontally(grid, e.Delta)) e.Handled = true;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProc);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmMouseHWheel = 0x020E;
        if (msg == wmMouseHWheel)
        {
            var delta = unchecked((short)((wParam.ToInt64() >> 16) & 0xffff));
            if (Mouse.DirectlyOver is DependencyObject element)
            {
                var grid = FindAncestor<DataGrid>(element);
                if (grid is not null && TryScrollDataGridHorizontally(grid, delta))
                {
                    handled = true;
                }
            }
        }

        return IntPtr.Zero;
    }

    private static bool TryScrollDataGridHorizontally(DataGrid grid, int delta)
    {
        var scrollViewer = FindDescendant<ScrollViewer>(grid);
        if (scrollViewer is null || scrollViewer.ScrollableWidth <= 0) return false;

        var offset = scrollViewer.HorizontalOffset - delta;
        if (offset < 0) offset = 0;
        if (offset > scrollViewer.ScrollableWidth) offset = scrollViewer.ScrollableWidth;
        scrollViewer.ScrollToHorizontalOffset(offset);
        return true;
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (true)
        {
            if (current is T match) return match;
            var parent = VisualTreeHelper.GetParent(current);
            if (parent is null) return null;
            current = parent;
        }
    }

    private static T? FindDescendant<T>(DependencyObject current) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
        {
            var child = VisualTreeHelper.GetChild(current, i);
            if (child is T match) return match;
            var nested = FindDescendant<T>(child);
            if (nested is not null) return nested;
        }

        return null;
    }

    private void TargetExecutableDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            _viewModel.AcceptDroppedExecutable(files[0]);
        }
    }

    private void TargetExecutableDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void HelpMouseEnter(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string text }) _viewModel.HelpText = text;
    }

    private void HelpMouseLeave(object sender, RoutedEventArgs e)
    {
        _viewModel.HelpText = "将鼠标放在按钮上查看功能说明。";
    }
}
