using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Tests.Harness;

/// <summary>A message split into its tag, route, sender and payload.</summary>
public readonly record struct Envelope(byte Tag, byte RelayType, byte? Target, byte? Sender, byte[] Payload)
{
    public static Envelope? Read(byte[] message)
    {
        try
        {
            var reader = new FusionNetReader(message);

            byte tag = reader.ReadByte();
            byte relayType = reader.ReadByte();
            reader.ReadByte(); // channel

            byte? target = null;

            if (relayType == 4)
            {
                target = reader.ReadNullableByte();
            }
            else if (relayType == 5)
            {
                int count = reader.ReadInt32();

                for (var i = 0; i < count; i++)
                {
                    reader.ReadByte();
                }
            }

            byte? sender = relayType != 0 ? reader.ReadNullableByte() : null;
            int length = reader.ReadInt32();

            return new Envelope(tag, relayType, target, sender, reader.ReadRaw(length).ToArray());
        }
        catch
        {
            return null;
        }
    }
}
