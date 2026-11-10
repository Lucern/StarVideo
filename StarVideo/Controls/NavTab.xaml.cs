using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace StarVideo.Controls;

/// <summary>顶部频道标签。刻意做成独立控件，避免依赖系统控件的 VisualState 名称。</summary>
public sealed partial class NavTab : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(NavTab), new PropertyMetadata("", OnVisualPropertyChanged));

    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected), typeof(bool), typeof(NavTab), new PropertyMetadata(false, OnVisualPropertyChanged));

    public NavTab()
    {
        InitializeComponent();
        UpdateVisual();
    }

    /// <summary>标签文字。</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public event EventHandler? Clicked;

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((NavTab)d).UpdateVisual();

    private void UpdateVisual()
    {
        Label.Text = Text;
        Label.Opacity = IsSelected ? 1.0 : 0.6;
        Label.FontWeight = IsSelected
            ? Microsoft.UI.Text.FontWeights.SemiBold
            : Microsoft.UI.Text.FontWeights.Normal;
        Underline.Visibility = IsSelected ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTapped(object sender, TappedRoutedEventArgs e) => Clicked?.Invoke(this, EventArgs.Empty);

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) =>
        HoverLayer.Visibility = Visibility.Visible;

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) =>
        HoverLayer.Visibility = Visibility.Collapsed;

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e) => Root.Opacity = 0.7;

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e) => Root.Opacity = 1.0;
}
