// File: RevitMCPAddin/Commands/System/ListCommandsHandler.cs
using System.Collections.Generic;
using System.Linq;
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
            var names = _handlers.Select(h => h.CommandName)
                                 .Distinct()
                                 .OrderBy(n => n)
                                 .ToList();
            return new { ok = true, commands = names };
        }
    }
}
