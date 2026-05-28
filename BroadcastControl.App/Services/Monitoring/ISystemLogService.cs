namespace BroadcastControl.App.Services;

public interface ISystemLogService
{
    event EventHandler<string>? LogAdded;

    void Add(string message);
}
