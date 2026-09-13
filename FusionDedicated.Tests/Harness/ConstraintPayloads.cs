using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Tests.Harness;

internal abstract record EndSpec;

internal sealed record EntityEnd(ushort Id, ushort Body = 0) : EndSpec;

internal sealed record SceneEnd(string Path) : EndSpec;

internal sealed record NullEnd : EndSpec;

/// <summary>
/// Builds the handler payload of Fusion's ConstraintCreateMessage the way the client
/// writes it: SmallID, a nullable ConstrainerID, Mode, two ends, then transforms,
/// points and normals the server never reads, and the two point ids last.
/// </summary>
internal static class ConstraintPayloads
{
    public static byte[] Build(byte smallId, EndSpec first, EndSpec second, ushort? constrainer = null)
    {
        var writer = new FusionNetWriter(128);

        writer.Write(smallId);
        writer.Write(constrainer.HasValue);

        if (constrainer is { } id)
        {
            writer.WriteUInt16(id);
        }

        writer.Write((byte)0);

        WriteEnd(writer, first);
        WriteEnd(writer, second);

        writer.WriteRaw(new byte[64]);
        writer.WriteUInt16(0);
        writer.WriteUInt16(0);

        return writer.ToArray();
    }

    private static void WriteEnd(FusionNetWriter writer, EndSpec end)
    {
        switch (end)
        {
            case NullEnd:
                writer.Write(0);
                break;

            case EntityEnd entity:
                writer.Write(1);
                writer.WriteUInt16(entity.Id);
                writer.WriteUInt16(entity.Body);
                break;

            case SceneEnd scene:
                writer.Write(2);
                writer.Write(scene.Path);
                break;
        }
    }
}
