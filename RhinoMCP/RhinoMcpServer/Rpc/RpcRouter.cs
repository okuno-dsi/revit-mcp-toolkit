using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RhinoMcpServer.Rpc
{
    public static class RpcRouter
    {
        public static Task<object> RouteAsync(string method, JObject p)
        {
            return method switch
            {
                "rhino_import_snapshot" => Rhino.ImportSnapshotCommand.HandleAsync(p),
                "rhino_get_selection" => Rhino.GetSelectionCommand.HandleAsync(p),
                "rhino_commit_transform" => Rhino.CommitTransformCommand.HandleAsync(p),
                "rhino_lock_objects" => Rhino.LockObjectsCommand.HandleAsync(p),
                "rhino_unlock_objects" => Rhino.UnlockObjectsCommand.HandleAsync(p),
                "rhino_refresh_from_revit" => Rhino.RefreshFromRevitCommand.HandleAsync(p),
                "rhino_import_by_ids" => Rhino.ImportByIdsCommand.HandleAsync(p),
                // Extended: 3dm workflow and queries
                "import_3dm" => Rhino.Import3dmCommand.HandleAsync(p),
                "list_revit_objects" => Rhino.ListRevitObjectsCommand.HandleAsync(p),
                "find_by_element" => Rhino.FindByElementCommand.HandleAsync(p),
                "collect_boxes" => Rhino.CollectBoxesCommand.HandleAsync(p),
                // Tools
                "convert_3dm_version" => Tools.Convert3dmVersionCommand.HandleAsync(p),
                "inspect_3dm" => Tools.Inspect3dmCommand.HandleAsync(p),
                "merge_3dm_files" => Tools.Merge3dmCommand.HandleAsync(p),
                _ => throw new JsonRpcException(-32601, $"Unknown method: {method}")
            };
        }
    }

    public class JsonRpcException : System.Exception
    {
        public int Code { get; }
        public JsonRpcException(int code, string message) : base(message) { Code = code; }
    }
}

