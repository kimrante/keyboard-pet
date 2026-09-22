using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KeyboardPet.App.ViewModels;

namespace KeyboardPet.App.Windows;

/// <summary>비모달 설정 창. 닫히면 뷰모델 구독을 해제한다. 프레임 타일의 드래그앤드롭 순서 변경도 여기서 처리한다.</summary>
public partial class SettingsWindow : Window
{
    private const string FrameDataFormat = "KeyboardPet.FrameEntry";

    private Point _dragStart;
    private FrameEntryViewModel? _dragCandidate;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose();
    }

    private void FrameTile_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // × 버튼 위에서 시작된 클릭은 드래그로 취급하지 않는다.
        if (IsInsideButton(e.OriginalSource as DependencyObject))
        {
            _dragCandidate = null;
            return;
        }

        _dragStart = e.GetPosition(null);
        _dragCandidate = (sender as FrameworkElement)?.DataContext as FrameEntryViewModel;
    }

    private void FrameTile_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragCandidate = null;
    }

    private void FrameTile_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        // 드래그는 버튼을 누른 바로 그 타일 위에서만 시작한다(다른 곳에서 누른 채 지나가는 경우 제외).
        if (_dragCandidate is null
            || e.LeftButton != MouseButtonState.Pressed
            || !ReferenceEquals((sender as FrameworkElement)?.DataContext, _dragCandidate))
        {
            return;
        }

        var delta = e.GetPosition(null) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var data = new DataObject(FrameDataFormat, _dragCandidate);
        _dragCandidate = null;
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
    }

    private void FrameTile_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return; // 탐색기 파일 드롭은 세트 카드/탭 핸들러가 처리한다.
        }

        e.Effects = TryGetDropPair(sender, e, out _, out _) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void FrameTile_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (TryGetDropPair(sender, e, out var source, out var target))
        {
            source.Owner.MoveFrame(source, target);
        }

        e.Handled = true;
    }

    // ── 탐색기에서 이미지/폴더 드래그앤드롭 ──

    private void SetsArea_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void SetsArea_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && DataContext is SettingsViewModel vm)
        {
            vm.ImportDroppedPaths(paths, target: null);
        }

        e.Handled = true;
    }

    private void SetCard_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return; // 프레임 타일 간 이동은 타일 핸들러가 처리한다.
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void SetCard_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths
            && DataContext is SettingsViewModel vm
            && (sender as FrameworkElement)?.DataContext is FrameSetItemViewModel target)
        {
            vm.ImportDroppedPaths(paths, target);
        }

        e.Handled = true;
    }

    /// <summary>같은 세트 안의 다른 타일 위에 놓을 때만 유효한 드롭이다.</summary>
    private static bool TryGetDropPair(object sender, DragEventArgs e, out FrameEntryViewModel source, out FrameEntryViewModel target)
    {
        source = null!;
        target = null!;

        if (!e.Data.GetDataPresent(FrameDataFormat)
            || e.Data.GetData(FrameDataFormat) is not FrameEntryViewModel src
            || (sender as FrameworkElement)?.DataContext is not FrameEntryViewModel tgt
            || !ReferenceEquals(src.Owner, tgt.Owner)
            || ReferenceEquals(src, tgt))
        {
            return false;
        }

        source = src;
        target = tgt;
        return true;
    }

    private static bool IsInsideButton(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is Button)
            {
                return true;
            }

            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return false;
    }
}
