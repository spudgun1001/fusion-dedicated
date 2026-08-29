using System.Xml.Linq;

namespace FusionDedicated.Server.Ranks;

/// <summary>
/// Reads the roster LabFusion writes when you host a normal lobby, so a rank list
/// built in game carries across to a dedicated server. The import is additive and
/// never lowers a rank already held here.
/// </summary>
public static class PermissionListImporter
{
    public static int Import(RankStore store, string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return 0;
        }

        XDocument document;

        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return 0;
        }

        var added = 0;

        foreach (var element in document.Descendants("Permission"))
        {
            if (!ulong.TryParse(element.Attribute("id")?.Value, out ulong id)
                || !int.TryParse(element.Attribute("level")?.Value, out int rawLevel))
            {
                continue;
            }

            var level = PermissionLevels.Clamp(rawLevel);

            if (level == PermissionLevel.Default || store.Get(id) >= level)
            {
                continue;
            }

            store.Set(id, element.Attribute("username")?.Value ?? "", level);
            added++;
        }

        return added;
    }
}
