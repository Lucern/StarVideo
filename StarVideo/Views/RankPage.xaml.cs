using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using StarVideo.Models;
using StarVideo.Services;

namespace StarVideo.Views;

/// <summary>排行榜：榜单类型 × 频道筛选，左侧完整榜单、右侧前三名速览。</summary>
public sealed partial class RankPage : Page
{
    private string _board = "热播榜";
    private string _category = "全部";

    public RankPage()
    {
        InitializeComponent();
        RankList.IsItemClickEnabled = true;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string p && p == "评分") _board = "口碑榜";

        BuildChips();
        Refresh();
        UpdateText.Text = $"更新于 {DateTime.Now:MM 月 dd 日 HH:mm} · 共 {AppServices.Catalog.All.Count} 部作品";
    }

    private string SortMode => _board switch
    {
        "口碑榜" => "评分",
        "新片榜" => "最新",
        _ => "热度"
    };

    private void BuildChips()
    {
        var normal = (Style)Application.Current.Resources["ChipButtonStyle"];
        var active = (Style)Application.Current.Resources["SelectedChipStyle"];

        BuildGroup(BoardChips, new[] { "热播榜", "口碑榜", "新片榜" }, _board, value =>
        {
            _board = value;
            Refresh();
        }, normal, active);

        var cats = new List<string> { "全部" };
        cats.AddRange(AppServices.Catalog.Categories);
        BuildGroup(CategoryChips, cats, _category, value =>
        {
            _category = value;
            Refresh();
        }, normal, active);
    }

    private static void BuildGroup(Panel container, IEnumerable<string> labels, string selected,
        Action<string> onSelected, Style normal, Style active)
    {
        container.Children.Clear();
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

    private void Refresh()
    {
        var items = AppServices.Catalog.Query(_category, null, null, SortMode).Take(20).ToList();
        RankList.ItemsSource = items
            .Select((item, i) => new RankEntry { Rank = i + 1, Item = item }).ToList();
        BuildPodium(items.Take(3).ToList());
    }

    private void BuildPodium(List<MediaItem> top3)
    {
        Podium.Children.Clear();
        var appResources = Application.Current.Resources;
        var imageConverter = (Helpers.ImagePathConverter)appResources["ImagePath"];

        for (var i = 0; i < top3.Count; i++)
        {
            var item = top3[i];
            var rankText = (i + 1).ToString("00");
            var poster = new Border
            {
                Width = 62,
                Height = 84,
                CornerRadius = new CornerRadius(6)
            };
            poster.Background = new ImageBrush
            {
                ImageSource = (Microsoft.UI.Xaml.Media.Imaging.BitmapImage?)imageConverter.Convert(
                    item.PosterPath, typeof(Microsoft.UI.Xaml.Media.ImageSource), null!, ""),
                Stretch = Stretch.UniformToFill
            };

            var card = new Border
            {
                Background = (Brush)appResources["SurfaceBrush"],
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Tag = item
            };

            var layout = new Grid { ColumnSpacing = 14 };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var rank = new TextBlock
            {
                Text = rankText,
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 150, 60))
            };
            Grid.SetColumn(rank, 0);

            Grid.SetColumn(poster, 1);

            var info = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock
            {
                Text = item.Title,
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            info.Children.Add(new TextBlock
            {
                Text = item.MetaText,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            info.Children.Add(new TextBlock
            {
                Text = $"{item.ScoreText} 分  ·  {item.HotText}热度",
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 194, 74))
            });
            Grid.SetColumn(info, 2);

            layout.Children.Add(rank);
            layout.Children.Add(poster);
            layout.Children.Add(info);
            card.Child = layout;
            card.PointerReleased += (_, _) => App.MainWindow?.OpenDetail(item);
            Podium.Children.Add(card);
        }
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RankEntry entry) App.MainWindow?.OpenDetail(entry.Item);
    }
}
