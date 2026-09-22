using System.Windows;
using KeyboardPet.App.ViewModels;

namespace KeyboardPet.App.Windows;

/// <summary>비모달 설정 창. 닫히면 뷰모델 구독을 해제한다.</summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose();
    }
}
