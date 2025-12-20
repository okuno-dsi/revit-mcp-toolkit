using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace IfcCore;

/// <summary>
/// Exports a ProfileDefinition into a buildingSMART IDS XML file.
/// </summary>
public class IdsExporter
{
    public void Export(ProfileDefinition profile, string outPath, bool includeComments)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        if (string.IsNullOrWhiteSpace(outPath)) throw new ArgumentNullException(nameof(outPath));

        var ids = new XElement("ids");

        foreach (var kv in profile.EntityRules)
        {
            var entityName = kv.Key;
            var rule = kv.Value;
            if (rule.RequiredProperties == null || rule.RequiredProperties.Count == 0)
            {
                continue; // nothing to export for this entity
            }

            var spec = new XElement("specification",
                new XAttribute("name", $"{entityName}_Requirements"));

            if (includeComments)
            {
                spec.Add(new XComment(
                    $"Requirements for {entityName} generated from profile '{profile.ProfileName}' on {profile.CreatedAt.ToString("u", CultureInfo.InvariantCulture)}"));
            }

            var applicability = new XElement("applicability",
                new XElement("entity", entityName));
            spec.Add(applicability);

            var requirements = new XElement("requirements");

            foreach (var req in rule.RequiredProperties)
            {
                if (string.IsNullOrWhiteSpace(req.Pset) || string.IsNullOrWhiteSpace(req.Name))
                {
                    // Unsupported or incomplete rule; skip but continue others
                    continue;
                }

                var propElem = new XElement("property",
                    new XElement("pset", req.Pset),
                    new XElement("name", req.Name),
                    new XElement("occurrence", "required"));

                if (includeComments)
                {
                    propElem.Add(new XComment(
                        $"MinFillRate in profile: {req.MinFillRate.ToString("0.###", CultureInfo.InvariantCulture)}"));
                }

                requirements.Add(propElem);
            }

            if (requirements.HasElements)
            {
                spec.Add(requirements);
                ids.Add(spec);
            }
        }

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), ids);

        // Ensure directory exists
        var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        doc.Save(outPath);
    }
}

