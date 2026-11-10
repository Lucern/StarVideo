using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StarVideo.Models;
using StarVideo.Services;

namespace StarVideo.Views;

/// <summary>我的：收藏 / 观看历史 / 继续观看，数据落地本地 JSON。</summary>
public sealed partial class LibraryPage : Page
{
    private static readonly string[] Tabs = { "我的收藏", "观看历史", "继续观看" };
    private int _tabIndex;

    public LibraryPage()
    {
        InitializeComponent();
        ContentGrid.IsItemClickEnabled = true;

        Loaded += (_, _) => AppServices.DataChanged += OnDataChanged;
        Unloaded += (_, _) => AppServices.DataChanged -= OnDataChanged;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is int index) _tabIndex = Math.Clamp(index, 0, Tabs.Length - 1);
        BuildTabs();
        Refresh();
    }

    private void OnDataChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(Refresh);

    private void BuildTabs()
    {
        TabChips.Children.Clear();
        var normal = (Style)Application.Current.Resources["ChipButtonStyle"];
        var active = (Style)Application.Current.Resources["SelectedChipStyle"];

        for (var i = 0; i < Tabs.Length; i++)
        {
            var index = i;
            var button = new Button
            {
                Content = Tabs[i],
                Tag = i,
                Style = i == _tabIndex ? active : normal
            };
            button.Click += (sender, _) =>
            {
                foreach (var sibling in TabChips.Children.OfType<Button>())
                    sibling.Style = ReferenceEquals(sibling, sender) ? active : normal;
                _tabIndex = index;
                Refresh();
            };
            TabChips.Children.Add(button);
        }
    }

    private void Refresh()
    {
        var catalog = AppServices.Catalog;
        var user = AppServices.User;
        var resources = Application.Current.Resources;
        int count;

        switch (_tabIndex)
        {
            case 0:
                ContentGrid.ItemTemplate = (DataTemplate)resources["PosterCardTemplate"];
                ContentGrid.ItemContainerStyle = (Style)resources["CardItemStyle"];
                var favorites = user.Favorites(catalog);
                ContentGrid.ItemsSource = favorites;
                count = favorites.Count;
                break;
            case 1:
                ContentGrid.ItemTemplate = (DataTemplate)resources["HistoryCardTemplate"];
                ContentGrid.ItemContainerStyle = (Style)resources["CardItemStyle"];
                var history = user.History(catalog);
                ContentGrid.ItemsSource = history;
                count = history.Count;
                break;
            default:
                ContentGrid.ItemTemplate = (DataTemplate)resources["ContinueCardTemplate"];
                ContentGrid.ItemContainerStyle = (Style)resources["CardItemStyle"];
                var watching = user.ContinueWatching(catalog);
                ContentGrid.ItemsSource = watching;
                count = watching.Count;
                break;
        }

        SummaryText.Text = $"收藏 {user.Favorites(catalog).Count} 部  ·  观看记录 {user.History(catalog).Count} 条  ·  数据保存在本机";
        EmptyState.Visibility = count > 0 ? Visibility.Collapsed : Visibility.Visible;
        ClearButton.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.Content = _tabIndex == 0 ? "清空收藏" : "清空记录";

        (EmptyTitle.Text, EmptyHint.Text) = _tabIndex switch
        {
            0 => ("还没有收藏的作品", "在详情页点「收藏」，喜欢的作品就会出现在这里"),
            1 => ("还没有观看记录", "播放任意作品后，这里会自动记录进度"),
            _ => ("暂无未看完的作品", "看到一半的作品会出现在这里，方便接着看")
        };
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (_tabIndex == 0)
        {
            AppServices.User.ClearFavorites();
            App.MainWindow?.ShowToast("已清空收藏");
        }
        else
        {
            AppServices.User.ClearHistory();
            App.MainWindow?.ShowToast("已清空观看记录");
        }
        Refresh();
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is WatchEntry entry)
            App.MainWindow?.OpenPlayer(entry.Item, entry.Record.Episode);
        else if (e.ClickedItem is MediaItem item)
            App.MainWindow?.OpenDetail(item);
    }

    private void OnGoHome(object sender, RoutedEventArgs e) => App.MainWindow?.NavigateTo("home");
}
