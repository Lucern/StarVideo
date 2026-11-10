using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using StarVideo.Models;
using StarVideo.Services;

namespace StarVideo.Views;

/// <summary>详情页：主视觉、简介演职员、选集、猜你喜欢、收藏。</summary>
public sealed partial class DetailPage : Page
{
    private MediaItem _item = null!;
    private int _currentEpisode = 1;

    public DetailPage()
    {
        InitializeComponent();
        RelatedGrid.IsItemClickEnabled = true;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _item = e.Parameter as MediaItem ?? AppServices.Catalog.All.First();
        DataContext = _item;

        var record = AppServices.User.RecordOf(_item.Id);
        _currentEpisode = record?.Episode ?? 1;
        PlayButtonText.Text = record is { ProgressPercent: > 1 and < 96 }
            ? "继续播放"
            : "立即播放";

        ApplyImages();
        UpdateFavoriteButton();
        BuildEpisodes();
        RelatedGrid.ItemsSource = AppServices.Catalog.Related(_item, 6);
    }

    // ---------------- 图片 ----------------

    private void ApplyImages()
    {
        var converter = (Helpers.ImagePathConverter)Application.Current.Resources["ImagePath"];

        BitmapImage? ToImage(string path) => converter.Convert(path, typeof(ImageSource), null!, "") as BitmapImage;

        var backdrop = ToImage(_item.BannerPath) ?? ToImage(_item.PosterPath);
        if (backdrop is not null)
            HeroBackdrop.Background = new ImageBrush
            {
                ImageSource = backdrop,
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };

        var poster = ToImage(_item.PosterPath);
        if (poster is not null)
            HeroPoster.Background = new ImageBrush
            {
                ImageSource = poster,
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };
    }

    // ---------------- 选集 ----------------

    private void BuildEpisodes()
    {
        EpisodeRows.Children.Clear();
        var names = _item.EpisodeNames;
        var isSeries = _item.IsSeries && names.Count > 1;

        EpisodeSection.Visibility = isSeries ? Visibility.Visible : Visibility.Collapsed;
        if (!isSeries) return;

        EpisodeCountText.Text = $"共 {names.Count} 集";
        EpisodeHint.Text = _item.Vip ? "VIP 抢先看 · 演示版全部可播" : "全部免费";

        var normal = (Style)Application.Current.Resources["ChipButtonStyle"];
        var active = (Style)Application.Current.Resources["SelectedChipStyle"];

        const int perRow = 10;
        for (var offset = 0; offset < names.Count; offset += perRow)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            for (var i = offset; i < Math.Min(offset + perRow, names.Count); i++)
            {
                var episode = i + 1;
                var button = new Button
                {
                    Content = (i + 1).ToString("00"),
                    Tag = episode,
                    Width = 58,
                    Height = 40,
                    Margin = new Thickness(0),
                    Style = episode == _currentEpisode ? active : normal
                };
                ToolTipService.SetToolTip(button, names[i]);
                button.Click += OnEpisodeClick;
                row.Children.Add(button);
            }
            EpisodeRows.Children.Add(row);
        }
    }

    private void OnEpisodeClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int episode })
            App.MainWindow?.OpenPlayer(_item, episode);
    }

    // ---------------- 操作 ----------------

    private void OnPlayClick(object sender, RoutedEventArgs e) =>
        App.MainWindow?.OpenPlayer(_item, _currentEpisode);

    private void OnFavoriteClick(object sender, RoutedEventArgs e)
    {
        var added = AppServices.User.ToggleFavorite(_item.Id);
        UpdateFavoriteButton();
        App.MainWindow?.ShowToast(added ? $"已收藏《{_item.Title}》" : "已取消收藏");
    }

    private void UpdateFavoriteButton() =>
        FavoriteButton.Content = AppServices.User.IsFavorite(_item.Id) ? "★ 已收藏" : "☆ 收藏";

    private void OnShareClick(object sender, RoutedEventArgs e) =>
        App.MainWindow?.ShowToast("演示版：分享链接功能未接入真实服务");

    private void OnBackClick(object sender, RoutedEventArgs e) => App.MainWindow?.GoBackPage();

    private void OnRelatedClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MediaItem item) App.MainWindow?.OpenDetail(item);
    }
}
