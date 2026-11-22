using System.Windows;
using System.Windows.Controls;

namespace InternalManagement.Desktop.Controls;

public partial class EmptyState : UserControl
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(EmptyState),
            new PropertyMetadata("\uE8B7", (d, e) => ((EmptyState)d).IconGlyph.Text = e.NewValue as string ?? "\uE8B7"));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(EmptyState),
            new PropertyMetadata("Chưa có dữ liệu", (d, e) => ((EmptyState)d).TitleBlock.Text = e.NewValue as string ?? string.Empty));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(EmptyState),
            new PropertyMetadata("Thử đổi bộ lọc hoặc thêm bản ghi mới để bắt đầu.", (d, e) => ((EmptyState)d).SubtitleBlock.Text = e.NewValue as string ?? string.Empty));

    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }

    public EmptyState()
    {
        InitializeComponent();
    }
}
