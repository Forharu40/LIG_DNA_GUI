using System.Windows.Controls;

// 파일 역할:
// VLM/YOLO 분석 패널의 XAML 초기화만 담당합니다.
// 표시 상태와 분석 결과 관리는 VlmViewModel에서 처리합니다.

namespace BroadcastControl.App.Views.Vlm;

public partial class VlmPanelView : UserControl
{
    public VlmPanelView()
    {
        InitializeComponent();
    }
}
