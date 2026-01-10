// File: RevitMCPAddin/Commands/System/ListCommandsHandler.cs
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.MetaOps
{
    /// <summary>
    /// 現在登録されているコマンド名一覧を返すユーティリティ
    /// </summary>
    public class ListCommandsHandler : IRevitCommandHandler
    {
        public string CommandName => "list_commands";

        private readonly IReadOnlyList<IRevitCommandHandler> _handlers;

        public ListCommandsHandler(IEnumerable<IRevitCommandHandler> handlers)
        {
            _handlers = handlers.ToList();
        }

        private sealed class ListCommandsParams
        {
            public bool? includeDeprecated { get; set; }
            public bool? includeDetails { get; set; }
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (cmd.Params as JObject) != null
                ? ((JObject)cmd.Params!).ToObject<ListCommandsParams>() ?? new ListCommandsParams()
                : new ListCommandsParams();

            bool includeDeprecated = p.includeDeprecated == true;
            bool includeDetails = p.includeDetails == true;

            // Default: canonical only (namespaced). Legacy names remain callable, but are deprecated aliases.
            var metas = CommandMetadataRegistry.GetAll();
            var items = new List<JObject>();

            foreach (var m in metas)
            {
                if (m == null) continue;
                if (string.IsNullOrWhiteSpace(m.name)) continue;

                items.Add(new JObject
                {
                    ["method"] = m.name,
                    ["deprecated"] = false,
                    ["canonical"] = m.name,
                    ["aliases"] = new JArray((m.aliases ?? System.Array.Empty<string>()).ToArray())
                });

                if (!includeDeprecated) continue;
                foreach (var a in (m.aliases ?? System.Array.Empty<string>()))
                {
                    var aa = (a ?? string.Empty).Trim();
                    if (aa.Length == 0) continue;
                    items.Add(new JObject
                    {
                        ["method"] = aa,
                        ["deprecated"] = true,
                        ["canonical"] = m.name
                    });
                }
            }

            var commands = items
                .Select(x => (x.Value<string>("method") ?? string.Empty).Trim())
                .Where(x => x.Length > 0)
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, System.StringComparer.OrdinalIgnoreCase)
                .ToList();

            var result = new JObject
            {
                ["ok"] = true,
                ["commands"] = new JArray(commands),
                ["canonicalOnly"] = !includeDeprecated,
                ["deprecatedIncluded"] = includeDeprecated
            };
            if (includeDetails) result["items"] = new JArray(items.ToArray());
            return result;
        }
    }
}
