using System.Windows.Input;

namespace BroadcastControl.App.Infrastructure;

/// <summary>
/// WPF 버튼과 뷰모델 메서드를 연결하기 위한 가장 단순한 ICommand 구현이다.
/// 버튼 클릭 시 실행할 동작과 실행 가능 여부를 외부에서 주입받는다.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    /// <summary>
    /// 버튼 활성/비활성 조건이 바뀌었을 때 화면에 즉시 반영하도록 알린다.
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
