using System.Windows.Input;

namespace BroadcastControl.App.Infrastructure;

/// <summary>
/// WPF 버튼과 ViewModel 메서드를 연결하기 위한 가장 기본적인 ICommand 구현체이다.
/// 화면에서는 버튼을 누르는 동작만 일어나지만,
/// 내부적으로는 "무엇을 실행할지"와 "지금 실행 가능한지"를 이 객체가 함께 관리한다.
/// </summary>
public sealed class RelayCommand : ICommand
{
    /// <summary>
    /// 실제로 실행할 동작을 보관한다.
    /// 예를 들어 "설정창 열기", "녹화 시작", "모드 변경" 같은 메서드가 여기에 연결된다.
    /// </summary>
    private readonly Action<object?> _execute;

    /// <summary>
    /// 현재 명령이 실행 가능한지 판단하는 조건이다.
    /// 이 값이 없으면 항상 실행 가능한 명령으로 동작한다.
    /// </summary>
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>
    /// WPF는 이 이벤트를 보고 버튼 활성/비활성 상태를 다시 계산한다.
    /// 예를 들어 자동 모드에서는 누를 수 없던 버튼이 수동 모드에서 다시 활성화될 수 있다.
    /// </summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// 현재 명령을 실행할 수 있는지 반환한다.
    /// 조건 함수가 없으면 기본값으로 실행 가능(true)을 반환한다.
    /// </summary>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    /// <summary>
    /// 실제 동작을 실행한다.
    /// WPF 버튼 클릭 시 최종적으로 이 메서드가 호출된다.
    /// </summary>
    public void Execute(object? parameter) => _execute(parameter);

    /// <summary>
    /// 실행 가능 여부가 바뀌었음을 WPF에 알린다.
    /// 이 메서드를 호출해야 버튼의 회색/활성 상태가 화면에 즉시 반영된다.
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
