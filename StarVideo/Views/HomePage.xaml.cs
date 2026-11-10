using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using StarVideo.Models;
using StarVideo.Services;

namespace StarVideo.Views;

/// <summary>首页：焦点轮播、快捷入口、继续观看、推荐、榜单、频道精选。</summary>
public sealed partial class HomePage : Page
{
    private readonly List<MediaItem> _banners;
    private readonly DispatcherTimer _bannerTimer;
    private int _recommendOffset;

    public HomePage()
    {
        InitializeComponent();

        // ListViewBase 的 ItemClick 默认关闭，统一在这里开启（与页面上的 ItemClick 事件配合）。
        EnableItemClicks();

        _bannerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _bannerTimer.Tick += (_, _) => NextBanner();

        // 给 FlipView 赋 ItemsSource 会立刻触发 SelectionChanged → ResetBannerTimer，
        // 所以定时器必须先于 ItemsSource 初始化。
        _banners = AppServices.Catalog.Banners.ToList();
        BannerFlip.ItemsSource = _banners;
        BuildDots();
        BuildQuickEntries();

        Loaded += (_, _) =>
        {
            AppServices.DataChanged += OnDataChanged;
            _bannerTimer.Start();
        };
        Unloaded += (_, _) =>
        {
            AppServices.DataChanged -= OnDataChanged;
            _bannerTimer.Stop();
        };

        Load();
    }

    private void EnableItemClicks()
    {
        ContinueList.IsItemClickEnabled = true;
        RecommendGrid.IsItemClickEnabled = true;
        RankList.IsItemClickEnabled = true;
        TopRatedGrid.IsItemClickEnabled = true;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Load();
    }

    private void Load()
    {
        var catalog = AppServices.Catalog;

        var continueItems = AppServices.User.ContinueWatching(catalog);
        ContinueList.ItemsSource = continueItems;
        ContinueSection.Visibility = continueItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        RecommendGrid.ItemsSource = catalog.All.OrderByDescending(i => i.Hot)
            .Skip(_recommendOffset).Take(12).ToList();

        RankList.ItemsSource = catalog.Ranked(null, 6)
            .Select((item, i) => new RankEntry { Rank = i + 1, Item = item }).ToList();

        TopRatedGrid.ItemsSource = catalog.Query(null, null, null, "评分").Take(6).ToList();

        BuildCategorySections();

        if (_banners.Count > 0)
        {
            BannerFlip.SelectedIndex = 0;
            UpdateBannerInfo(0);
        }
    }

    private void OnDataChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var items = AppServices.User.ContinueWatching(AppServices.Catalog);
            ContinueList.ItemsSource = items;
            ContinueSection.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    // ---------------- 焦点图 ----------------

    private void BuildDots()
    {
        BannerDots.Children.Clear();
        for (var i = 0; i < _banners.Count; i++)
        {
            var button = new Button
            {
                Tag = i,
                Width = 22,
                Height = 20,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                Content = new Border
                {
                    Width = 8,
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Colors.White)
                }
            };
            button.Click += OnDotClick;
            BannerDots.Children.Add(button);
        }
    }

    private void OnDotClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int index })
        {
            BannerFlip.SelectedIndex = index;
            ResetBannerTimer();
        }
    }

    private void OnBannerChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateBannerInfo(BannerFlip.SelectedIndex);
        ResetBannerTimer();
    }

    private void UpdateBannerInfo(int index)
    {
        if (index < 0 || index >= _banners.Count) return;
        BannerInfo.DataContext = _banners[index];

        foreach (var button in BannerDots.Children.OfType<Button>())
        {
            if (button.Content is not Border dot) continue;
            var active = button.Tag is int i && i == index;
            dot.Width = active ? 20 : 8;
            dot.Background = new SolidColorBrush(active
                ? Colors.White
                : Windows.UI.Color.FromArgb(110, 255, 255, 255));
        }
    }

    private void NextBanner()
    {
        if (_banners.Count == 0) return;
        BannerFlip.SelectedIndex = (BannerFlip.SelectedIndex + 1) % _banners.Count;
    }

    private void ResetBannerTimer()
    {
        if (_bannerTimer is null) return;
        _bannerTimer.Stop();
        _bannerTimer.Start();
    }

    private void OnBannerPlay(object sender, RoutedEventArgs e) =>
        App.MainWindow?.OpenPlayer(_banners[BannerFlip.SelectedIndex]);

    private void OnBannerDetail(object sender, RoutedEventArgs e) =>
        App.MainWindow?.OpenDetail(_banners[BannerFlip.SelectedIndex]);

    // ---------------- 快捷入口 / 频道分区 ----------------

    private void BuildQuickEntries()
    {
        var entries = new (string Name, string Glyph)[]
        {
            ("电视剧", "\uE7F4"), ("电影", "\uE714"), ("综艺", "\uE8F1"),
            ("动漫", "\uE8B9"), ("纪录片", "\uE721"), ("少儿", "\uE8D3"),
            ("在线试播", "\uE768")
        };

        for (var i = 0; i < entries.Length; i++)
        {
            var (name, glyph) = entries[i];
            var button = new Button
            {
                Tag = name,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 8, 6, 8),
                Content = new StackPanel
                {
                    Spacing = 7,
                    Children =
                    {
                        new Border
                        {
                            Width = 42, Height = 42, CornerRadius = new CornerRadius(21),
                            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(38, 255, 122, 24)),
                            Child = new FontIcon
                            {
                                Glyph = glyph, FontSize = 19,
                                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 150, 60)),
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                            }
                        },
                        new TextBlock
                        {
                            Text = name, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center
                        }
                    }
                }
            };
            if (name == "在线试播")
                button.Click += (_, _) => App.MainWindow?.OpenOnlinePlayer();
            else
                button.Click += OnQuickCategoryClick;
            Grid.SetColumn(button, i);
            QuickGrid.Children.Add(button);
        }
    }

    private void BuildCategorySections()
    {
        CategorySections.Children.Clear();
        var appResources = Application.Current.Resources;

        foreach (var category in new[] { "电视剧", "电影", "综艺", "动漫" })
        {
            var cat = category;
            var header = new Grid();
            header.Children.Add(new TextBlock
            {
                Text = cat,
                Style = (Style)appResources["SectionTitleStyle"]
            });

            var more = new Button
            {
                Content = "查看全部  ›",
                HorizontalAlignment = HorizontalAlignment.Right,
                FontSize = 12.5,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0)
            };
            more.Click += (_, _) => App.MainWindow?.NavigateTo(cat, cat);
            header.Children.Add(more);

            var grid = new GridView
            {
                ItemTemplate = (DataTemplate)appResources["PosterCardTemplate"],
                ItemContainerStyle = (Style)appResources["CardItemStyle"],
                IsItemClickEnabled = true,
                SelectionMode = ListViewSelectionMode.None,
                ItemsSource = AppServices.Catalog.Query(cat, null, null).Take(6).ToList()
            };
            grid.SetValue(ScrollViewer.VerticalScrollModeProperty, ScrollMode.Disabled);
            grid.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            grid.ItemClick += OnPosterItemClick;

            var section = new StackPanel { Spacing = 14 };
            section.Children.Add(header);
            section.Children.Add(grid);
            CategorySections.Children.Add(section);
        }
    }

    private void OnQuickCategoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string cat }) App.MainWindow?.NavigateTo(cat, cat);
    }

    // ---------------- 卡片点击 ----------------

    private void OnPosterItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MediaItem item) App.MainWindow?.OpenDetail(item);
    }

    private void OnContinueItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is WatchEntry entry)
            App.MainWindow?.OpenPlayer(entry.Item, entry.Record.Episode);
    }

    private void OnRankItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RankEntry entry) App.MainWindow?.OpenDetail(entry.Item);
    }

    private void OnShuffle(object sender, RoutedEventArgs e)
    {
        _recommendOffset = (_recommendOffset + 6) % 30;
        RecommendGrid.ItemsSource = AppServices.Catalog.All.OrderByDescending(i => i.Hot)
            .Skip(_recommendOffset).Take(12).ToList();
    }

    private void OnOpenHistory(object sender, RoutedEventArgs e) => App.MainWindow?.OpenLibrary(1);
    private void OnOpenRank(object sender, RoutedEventArgs e) => App.MainWindow?.NavigateTo("rank");

    private void OnOpenTopRated(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.NavigateTo("rank", "评分");
    }
}
