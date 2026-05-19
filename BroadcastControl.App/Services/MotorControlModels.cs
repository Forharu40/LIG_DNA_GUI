namespace BroadcastControl.App.Services;

[Flags]
public enum MotorButtonMask : byte
{
    None = 0,
    Right = 0x01,
    Left = 0x02,
    Up = 0x04,
    Down = 0x08,
    Center = 0x10
}
