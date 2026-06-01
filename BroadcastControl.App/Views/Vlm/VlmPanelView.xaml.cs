using System.Windows.Controls;

// 파일 역할:
// VLM/YOLO 분석 패널 UserControl을 초기화합니다.
// 분석 문장, 위험도, 객체 목록은 VlmViewModel과 MainViewModel 바인딩으로 표시되므로 이 파일에는 별도 이벤트 로직을 두지 않습니다.

namespace BroadcastControl.App.Views.Vlm;

public partial class VlmPanelView : UserControl
{
    public VlmPanelView()
    {
        InitializeComponent();
    }
}
