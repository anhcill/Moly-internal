using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace InternalManagement.Desktop.Controls;

public partial class MolyLoader : UserControl
{
    private Storyboard? _storyboard;

    public MolyLoader()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateAnimationState();
        Unloaded += (_, _) => StopAnimation();
        IsVisibleChanged += (_, _) => UpdateAnimationState();
    }

    private void UpdateAnimationState()
    {
        _storyboard ??= Resources["RollStoryboard"] as Storyboard;
        if (_storyboard is null) return;

        if (IsLoaded && IsVisible)
        {
            _storyboard.Begin(this, isControllable: true);
        }
        else
        {
            _storyboard.Stop(this);
        }
    }

    private void StopAnimation()
    {
        _storyboard?.Stop(this);
    }
}
