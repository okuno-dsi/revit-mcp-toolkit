// File: RevitMCPAddin/Commands/System/ListCommandsHandler.cs
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.UI;
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

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            // Keep router-consistent: expand "a|b" and attribute-driven aliases.
            var set = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var h in _handlers)
            {
                if (h == null) continue;
                var raw = h.CommandName ?? "";
                foreach (var m in raw.Split(new[] { '|' }, System.StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()))
                {
                    if (!string.IsNullOrWhiteSpace(m)) set.Add(m);
                }

                try
                {
                    var attr = h.GetType().GetCustomAttribute<RpcCommandAttribute>(inherit: true);
                    if (attr != null)
                    {
                        if (!string.IsNullOrWhiteSpace(attr.Name)) set.Add(attr.Name.Trim());
                        if (attr.Aliases != null)
                        {
                            foreach (var a in attr.Aliases)
                            {
                                var aa = (a ?? "").Trim();
                                if (!string.IsNullOrWhiteSpace(aa)) set.Add(aa);
                            }
                        }
                    }
                }
                catch { /* best-effort */ }
            }

            var names = set.OrderBy(n => n).ToList();
            return new { ok = true, commands = names };
        }
    }
}
