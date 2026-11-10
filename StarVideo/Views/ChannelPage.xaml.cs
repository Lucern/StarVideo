using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StarVideo.Models;
using StarVideo.Services;

namespace StarVideo.Views;

/// <summary>频道页：类型 / 地区 / 年份 / 排序 / 会员 多条件筛选。</summary>
public sealed partial class ChannelPage : Page
{
    private string _genre = "全部";
    private string _region = "全部";
    private string _year = "全部";
    private string _sort = "热度";
    private string _vip = "全部";

    public string Category { get; private set; } = "电视剧";

    public ChannelPage()
    {
        InitializeComponent();
        ResultGrid.IsItemClickEnabled = true;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string cat && !string.IsNullOrWhiteSpace(cat) && cat != Category)
        {
            Category = cat;
            ResetFilters();
        }

        TitleText.Text = Category;
        BuildFilters();
        ApplyFilters();
    }

    private void ResetFilters()
    {
        _genre = _region = _year = _vip = "全部";
        _sort = "热度";
    }

    private void BuildFilters()
    {
        var catalog = AppServices.Catalog;

        var genres = new List<string> { "全部" };
        genres.AddRange(catalog.Query(Category, null, null)
            .SelectMany(i => i.Genre.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Distinct().Take(9));
        BuildChips(GenreChips, genres, _genre, value => { _genre = value; ApplyFilters(); });

        var regions = new List<string> { "全部" };
        regions.AddRange(catalog.RegionsOf(Category));
        BuildChips(RegionChips, regions, _region, value => { _region = value; ApplyFilters(); });

        var years = new List<string> { "全部" };
        years.AddRange(catalog.YearsOf(Category).Take(5));
        BuildChips(YearChips, years, _year, value => { _year = value; ApplyFilters(); });

        BuildChips(SortChips, new[] { "热度", "评分", "最新" }, _sort,
            value => { _sort = value; ApplyFilters(); });
        BuildChips(VipChips, new[] { "全部", "免费", "VIP" }, _vip,
            value => { _vip = value; ApplyFilters(); });
    }

    private void BuildChips(Panel container, IEnumerable<string> labels, string selected,
        Action<string> onSelected)
    {
        container.Children.Clear();
        var normal = (Style)Application.Current.Resources["ChipButtonStyle"];
        var active = (Style)Application.Current.Resources["SelectedChipStyle"];

        foreach (var label in labels)
        {
            var button = new Button
            {
                Content = label,
                Tag = label,
                Style = label == selected ? active : normal
            };
            button.Click += (sender, _) =>
            {
                foreach (var sibling in container.Children.OfType<Button>())
                    sibling.Style = ReferenceEquals(sibling, sender) ? active : normal;
                onSelected((string)((Button)sender).Tag);
            };
            container.Children.Add(button);
        }
    }

    private void ApplyFilters()
    {
        var onlyVip = _vip == "VIP";
        var onlyFree = _vip == "免费";
        var items = AppServices.Catalog.Query(Category, _region, _year, _sort, onlyVip, onlyFree);

        if (_genre != "全部")
            items = items.Where(i => i.Genre.Contains(_genre, StringComparison.Ordinal)).ToList();

        ResultGrid.ItemsSource = items;
        EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountBadge.Text = $"{items.Count} 部";
        SummaryText.Text = $"{Category}频道 · 按{_sort}排序 · " +
                           $"{(_region == "全部" ? "全部地区" : _region)} · " +
                           $"{(_year == "全部" ? "全部年份" : _year)}";
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MediaItem item) App.MainWindow?.OpenDetail(item);
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        ResetFilters();
        BuildFilters();
        ApplyFilters();
    }
}
