using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;

namespace RhinoMcpPlugin.Commands
{
    public class McpUnlockObjectsCommand : Command
    {
        public override string EnglishName => "McpUnlockObjects";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var go = new Rhino.Input.Custom.GetObject();
            go.SetCommandPrompt("Select objects to unlock shapes");
            go.SubObjectSelect = false;
            go.GroupSelect = true;
            go.GetMultiple(1, 0);
            if (go.CommandResult() != Result.Success) return go.CommandResult();

            foreach (var or in go.Objects())
            {
                var obj = or.Object();
                if (obj == null) continue;
                doc.Objects.Unlock(obj.Id, true);
            }
            doc.Views.Redraw();
            RhinoApp.WriteLine("Unlocked.");
            return Result.Success;
        }
    }
}
