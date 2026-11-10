using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using StarVideo.Models;
using StarVideo.Services;
using Windows.System;

namespace StarVideo.Views;

/// <summary>搜索页：关键词检索 + 热搜词 + 排序 + 结果网格。</summary>
public sealed partial class SearchPage : Page
{
    private string _keyword = "";
    private string _sort = "热度";

    public string Keyword => _keyword;

    public SearchPage()
    {
        InitializeComponent();
        ResultGrid.IsItemClickEnabled = true;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _keyword = (e.Parameter as string ?? "").Trim();
        SearchInput.Text = _keyword;
        BuildHotChips();
        BuildSortChips();
        ApplySearch();
    }

    private void BuildHotChips()
    {
        HotChips.Children.Clear();
        var normal = (Style)Application.Current.Resources["ChipButtonStyle"];

        var words = AppServices.Catalog.All.OrderByDescending(i => i.Hot)
            .Select(i => i.Title).Take(5).ToList();
        words.AddRange(new[] { "悬疑", "都市", "科幻" });

        foreach (var word in words)
        {
            var button = new Button { Content = word, Tag = word, Style = normal };
            button.Click += (sender, _) =>
            {
                _keyword = (string)((Button)sender).Tag;
                SearchInput.Text = _keyword;
                ApplySearch();
            };
            HotChips.Children.Add(button);
        }
    }

    private void BuildSortChips()
    {
        var normal = (Style)Application.Current.Resources["ChipButtonStyle"];
        var active = (Style)Application.Current.Resources["SelectedChipStyle"];
        SortChips.Children.Clear();

        foreach (var label in new[] { "热度", "评分", "最新" })
        {
            var button = new Button
            {
                Content = label,
                Tag = label,
                Style = label == _sort ? active : normal
            };
            button.Click += (sender, _) =>
            {
                foreach (var sibling in SortChips.Children.OfType<Button>())
                    sibling.Style = ReferenceEquals(sibling, sender) ? active : normal;
                _sort = (string)((Button)sender).Tag;
                ApplySearch();
            };
            SortChips.Children.Add(button);
        }
    }

    private void ApplySearch()
    {
        var items = AppServices.Catalog.Query(null, null, null, _sort, keyword: _keyword);
        ResultGrid.ItemsSource = items;

        var empty = _keyword.Length == 0;
        ResultTitle.Text = empty ? "全部片库" : $"“{_keyword}” 的搜索结果";
        ResultCount.Text = $"共 {items.Count} 部 · 按{_sort}排序";
        EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = $"没有找到与“{_keyword}”相关的作品";
    }

    private void OnSearchClick(object sender, RoutedEventArgs e)
    {
        _keyword = SearchInput.Text.Trim();
        ApplySearch();
    }

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) OnSearchClick(sender, e);
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MediaItem item) App.MainWindow?.OpenDetail(item);
    }
}
