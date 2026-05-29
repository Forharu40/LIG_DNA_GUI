// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
namespace BroadcastControl.App.Models.Motor;

// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
public readonly record struct MotorStatusSnapshot(
    MotorStatusPacket Pan,
    MotorStatusPacket? Tilt);

// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
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
    // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    public double PresentInputVoltage => PresentInputVoltageRaw / 10.0;
}
