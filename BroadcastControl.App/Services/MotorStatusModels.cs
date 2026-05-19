namespace BroadcastControl.App.Services;

public readonly record struct MotorStatusSnapshot(
    MotorStatusPacket Pan,
    MotorStatusPacket? Tilt);

public readonly record struct MotorStatusPacket(
    byte HardwareErrorStatus,
    byte PresentTemperature,
    ushort PresentInputVoltageRaw,
    uint PresentPosition,
    uint PresentVelocity,
    ushort PresentCurrentRaw,
    ushort PresentPwm,
    uint GoalPosition,
    uint GoalVelocity,
    byte Moving,
    byte MovingStatus,
    DateTime ReceivedAt)
{
    public double PresentInputVoltage => PresentInputVoltageRaw / 10.0;
}
