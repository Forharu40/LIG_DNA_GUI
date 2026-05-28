using System.Buffers.Binary;
using BroadcastControl.App.Models.Motor;

namespace BroadcastControl.App.Services;

public static class MotorPacketSerializer
{
    public const int CommandPacketSize = 10;

    public static byte[] CreateCommandPacket(
        byte mode,
        byte tracking,
        byte trackId,
        MotorButtonMask btnMask,
        ushort panPos,
        ushort tiltPos,
        byte scanStep,
        byte manualStep)
    {
        var packet = new byte[CommandPacketSize];
        packet[0] = mode;
        packet[1] = tracking;
        packet[2] = trackId;
        packet[3] = EncodeButtonMask(btnMask);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), panPos);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6, 2), tiltPos);
        packet[8] = EncodeStepSize(scanStep);
        packet[9] = EncodeStepSize(manualStep);
        return packet;
    }

    private static byte EncodeStepSize(int stepSize)
    {
        return (byte)Math.Clamp(stepSize, 1, 10);
    }

    private static byte EncodeButtonMask(MotorButtonMask buttons)
    {
        return (byte)((byte)buttons & 0x0F);
    }
}
