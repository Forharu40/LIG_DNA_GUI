namespace BroadcastControl.App.Services;

public sealed class SystemLogService : ISystemLogService
{
    public event EventHandler<string>? LogAdded;

    public void Add(string message)
    {
        LogAdded?.Invoke(this, message);
    }
}
