using System.Windows.Controls;
using System.Windows.Data;
using KeyboardPet.App.ViewModels;
using KeyboardPet.Core.Animation;

namespace KeyboardPet.App.Services;

/// <summary>
/// 트레이 아이콘과 PetWindow가 같은 구성의 컨텍스트 메뉴를 쓰도록 생성한다.
/// ContextMenu 인스턴스는 소유자를 하나만 가질 수 있으므로 호출마다 새로 만든다.
/// </summary>
public static class ContextMenuFactory
{
    public static ContextMenu Create(ShellViewModel shell)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CheckableItem("항상 위(_T)", shell.ToggleTopmostCommand, shell, nameof(ShellViewModel.IsTopmost)));
        menu.Items.Add(BuildModeMenu(shell));
        menu.Items.Add(new MenuItem { Header = "펫 보이기(_P)", Command = shell.ShowPetCommand });
        menu.Items.Add(new MenuItem { Header = "설정(_S)...", Command = shell.OpenSettingsCommand });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "종료(_X)", Command = shell.ExitCommand });
        return menu;
    }

    /// <summary>프레임 전환 방식을 빠르게 바꾸는 하위 메뉴. 세부 값은 설정 창에서 조정한다.</summary>
    private static MenuItem BuildModeMenu(ShellViewModel shell)
    {
        var mode = new MenuItem { Header = "애니메이션 모드(_M)" };
        mode.Items.Add(ModeItem("고정 간격", FrameMode.Fixed, nameof(ShellViewModel.IsFixedMode), shell));
        mode.Items.Add(ModeItem("랜덤 간격", FrameMode.Random, nameof(ShellViewModel.IsRandomMode), shell));
        mode.Items.Add(ModeItem("타수 기반", FrameMode.Keystroke, nameof(ShellViewModel.IsKeystrokeMode), shell));
        mode.Items.Add(ModeItem("타이핑 속도 연동", FrameMode.Adaptive, nameof(ShellViewModel.IsAdaptiveMode), shell));
        return mode;
    }

    private static MenuItem ModeItem(string header, FrameMode mode, string isCheckedPath, ShellViewModel shell)
    {
        var item = CheckableItem(header, shell.SetFrameModeCommand, shell, isCheckedPath);
        item.CommandParameter = mode;
        return item;
    }

    private static MenuItem CheckableItem(string header, System.Windows.Input.ICommand command, ShellViewModel shell, string isCheckedPath)
    {
        var item = new MenuItem
        {
            Header = header,
            IsCheckable = true,
            Command = command,
        };
        item.SetBinding(MenuItem.IsCheckedProperty, new Binding(isCheckedPath)
        {
            Source = shell,
            Mode = BindingMode.OneWay,
        });
        return item;
    }
}
