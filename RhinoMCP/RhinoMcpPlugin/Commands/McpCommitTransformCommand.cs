using System;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Newtonsoft.Json.Linq;

namespace RhinoMcpPlugin.Commands
{
    public class McpCommitTransformCommand : Command
    {
        public override string EnglishName => "McpCommitTransform";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var objref = new Rhino.Input.Custom.GetObject();
            objref.SetCommandPrompt("Select instance(s) to commit transform (move/rotate only)");
            objref.SubObjectSelect = false;
            objref.GroupSelect = true;
            objref.GetMultiple(1, 0);
            if (objref.CommandResult() != Result.Success) return objref.CommandResult();

            foreach (var or in objref.Objects())
            {
                var obj = or.Object();
                var geo = obj?.Geometry;
                if (geo == null) continue;

                var ud = Core.UserData.RevitRefUserData.From(obj);
                if (ud == null)
                {
                    RhinoApp.WriteLine("Object has no RevitRefUserData. Skipped.");
                    continue;
                }

                var instanceXform = (obj is InstanceObject io) ? io.InstanceXform : Transform.Identity;

                var delta = ud.BaselineWorldXform;
                delta.TryGetInverse(out Transform inv);
                delta = inv * instanceXform;

                if (!Core.TransformUtil.TryExtractTROnly(delta, 1e-6, out Vector3d t_mm, out double yawDeg, out string err))
                {
                    RhinoApp.WriteLine(err);
                    continue;
                }

                // Convert mm -> feet
                var t_ft = t_mm / Core.UnitUtil.FeetToMm(1.0);

                // Post to Revit MCP
                var payload = new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ["method"] = "apply_transform_delta",
                    ["params"] = new JObject
                    {
                        ["uniqueId"] = ud.RevitUniqueId,
                        ["delta"] = new JObject
                        {
                            ["translate"] = new JObject
                            {
                                ["x"] = t_ft.X, ["y"] = t_ft.Y, ["z"] = t_ft.Z, ["units"] = "feet"
                            },
                            ["rotateZDeg"] = yawDeg
                        },
                        ["guard"] = new JObject
                        {
                            ["snapshotStamp"] = ud.SnapshotStamp,
                            ["geomHash"] = ud.GeomHash
                        }
                    }
                };

                try
                {
                    var baseUrl = RhinoMcpPlugin.Instance.RevitMcpBaseUrl.TrimEnd('/');
                    var res = Core.HttpJsonRpcClient.PostJson(baseUrl + "/rpc", payload.ToString());
                    RhinoApp.WriteLine($"Committed: {ud.RevitUniqueId} -> {res}");
                }
                catch (Exception ex)
                {
                    RhinoApp.WriteLine("Commit failed: " + ex.Message);
                }
            }

            return Result.Success;
        }
    }
}
