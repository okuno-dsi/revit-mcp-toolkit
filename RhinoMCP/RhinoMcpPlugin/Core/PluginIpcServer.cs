using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace RhinoMcpPlugin.Core
{
    public static class PluginIpcServer
    {
        private static HttpListener _listener;
        private static CancellationTokenSource _cts;
        private static readonly string _prefix = "http://127.0.0.1:5201/";

        public static void Start()
        {
            try
            {
                if (_listener != null) return;
                _cts = new CancellationTokenSource();
                _listener = new HttpListener();
                _listener.Prefixes.Add(_prefix);
                _listener.Start();
                Logger.Info($"Plugin IPC listening on {_prefix}");
                Task.Run(() => AcceptLoop(_cts.Token));
            }
            catch (Exception ex)
            {
                Logger.Error("PluginIpcServer.Start failed: " + ex.Message);
            }
        }

        public static void Stop()
        {
            try
            {
                _cts?.Cancel();
                if (_listener != null)
                {
                    _listener.Close();
                    _listener = null;
                }
            }
            catch { }
        }

        private static async Task AcceptLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext ctx = null;
                try
                {
                    ctx = await _listener.GetContextAsync();
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception)
                {
                    if (token.IsCancellationRequested) break;
                    continue;
                }
                _ = Task.Run(() => Handle(ctx));
            }
        }

        private static void WriteJson(HttpListenerResponse res, JObject obj)
        {
            var bytes = Encoding.UTF8.GetBytes(obj.ToString());
            res.StatusCode = 200;
            res.ContentType = "application/json; charset=utf-8";
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.OutputStream.Close();
        }

        private static void Handle(HttpListenerContext ctx)
        {
            try
            {
                if (ctx.Request.HttpMethod != "POST" || ctx.Request.Url.AbsolutePath != "/rpc")
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.OutputStream.Close();
                    return;
                }

                string body;
                using (var sr = new System.IO.StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                    body = sr.ReadToEnd();

                var call = JObject.Parse(body);
                var id = call["id"];
                var method = (string)(call["method"] ?? "");
                var p = call["params"] as JObject ?? new JObject();

                try
                {
                    var result = Dispatch(method, p);
                    WriteJson(ctx.Response, new JObject {
                        ["jsonrpc"] = "2.0",
                        ["id"] = id,
                        ["result"] = result
                    });
                }
                catch (Exception ex)
                {
                    WriteJson(ctx.Response, new JObject {
                        ["jsonrpc"] = "2.0",
                        ["id"] = id,
                        ["error"] = new JObject { ["code"] = -32000, ["message"] = ex.Message }
                    });
                }
            }
            catch { }
        }

        private static JObject Dispatch(string method, JObject p)
        {
            switch (method)
            {
                case "rhino_import_snapshot":
                    return RhinoImportSnapshot(p);
                case "rhino_get_selection":
                    return RhinoGetSelection(p);
                case "rhino_commit_transform":
                    return RhinoCommitTransform(p);
                case "rhino_lock_objects":
                    return RhinoLockUnlock(p, true);
                case "rhino_unlock_objects":
                    return RhinoLockUnlock(p, false);
                case "rhino_import_3dm":
                    return RhinoImport3dm(p);
                case "rhino_list_revit_objects":
                    return RhinoListRevitObjects(p);
                case "rhino_find_by_element":
                    return RhinoFindByElement(p);
                case "rhino_collect_boxes":
                    return RhinoCollectBoxes(p);
                default:
                    throw new Exception($"Unknown method: {method}");
            }
        }

        private static JObject RhinoImport3dm(JObject p)
        {
            var doc = RhinoDoc.ActiveDoc;
            if (doc == null) return new JObject { ["ok"] = false, ["msg"] = "No active doc" };

            var path = (string)(p["path"] ?? "");
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                return new JObject { ["ok"] = false, ["msg"] = "Invalid path" };

            var before = new System.Collections.Generic.HashSet<Guid>();
            foreach (var o in doc.Objects) before.Add(o.Id);

            // Import using Rhino command to keep layers/materials/blocks
            var escaped = path.Replace("\"", "\\\"");
            var ok = Rhino.RhinoApp.RunScript($"_-Import \"{escaped}\" _Enter", false);
            if (!ok)
                return new JObject { ["ok"] = false, ["msg"] = "Import command failed" };

            // Collect new objects
            var newObjs = new System.Collections.Generic.List<RhinoObject>();
            foreach (var o in doc.Objects)
                if (!before.Contains(o.Id)) newObjs.Add(o);

            int updated = 0;
            var layers = new System.Collections.Generic.HashSet<string>();
            foreach (var obj in newObjs)
            {
                try
                {
                    var layerName = string.Empty;
                    try { var li = obj.Attributes.LayerIndex; var layer = doc.Layers.FindIndex(li); layerName = layer?.FullPath ?? layer?.Name ?? string.Empty; } catch { }
                    if (!string.IsNullOrWhiteSpace(layerName)) layers.Add(layerName);
                    var attr = obj.Attributes.Duplicate();
                    // Extract metadata from user strings
                    var uniqueId = attr.GetUserString("Revit.UniqueId") ?? attr.GetUserString("UniqueId") ?? "";
                    var snapshot = attr.GetUserString("Revit.SnapshotStamp") ?? "";
                    var geomHash = attr.GetUserString("Revit.GeomHash") ?? "";

                    // Attach RevitRefUserData for downstream ops
                    var ud = new UserData.RevitRefUserData
                    {
                        RevitUniqueId = uniqueId,
                        BaselineWorldXform = (obj is InstanceObject io) ? io.InstanceXform : Transform.Identity,
                        Units = "feet",
                        ScaleToRhino = UnitUtil.FeetToMm(1.0),
                        SnapshotStamp = snapshot,
                        GeomHash = geomHash
                    };
                    attr.UserData.Add(ud);
                    doc.Objects.ModifyAttributes(obj, attr, true);
                    updated++;
                }
                catch { }
            }

            doc.Views.Redraw();
            var arrLayers = new JArray(); foreach (var ln in layers) arrLayers.Add(ln);
            return new JObject { ["ok"] = true, ["objectCount"] = newObjs.Count, ["layers"] = arrLayers, ["revitLink"] = updated > 0 };
        }

        private static JObject RhinoListRevitObjects(JObject p)
        {
            var doc = RhinoDoc.ActiveDoc;
            if (doc == null) return new JObject { ["ok"] = false, ["msg"] = "No active doc" };

            var arr = new JArray();
            foreach (var obj in doc.Objects)
            {
                var attr = obj.Attributes;
                var uid = attr.GetUserString("Revit.UniqueId") ?? attr.GetUserString("UniqueId");
                if (string.IsNullOrWhiteSpace(uid))
                {
                    var ud = UserData.RevitRefUserData.From(obj);
                    uid = ud?.RevitUniqueId;
                }
                if (string.IsNullOrWhiteSpace(uid)) continue;

                var eid = attr.GetUserString("Revit.ElementId");
                var cat = attr.GetUserString("Revit.Category");
                var typ = attr.GetUserString("Revit.TypeName");

                var bbox = obj.Geometry?.GetBoundingBox(true) ?? BoundingBox.Empty;
                var min = new JObject { ["x"] = bbox.Min.X, ["y"] = bbox.Min.Y, ["z"] = bbox.Min.Z };
                var max = new JObject { ["x"] = bbox.Max.X, ["y"] = bbox.Max.Y, ["z"] = bbox.Max.Z };
                var layerName2 = string.Empty; try { var li = obj.Attributes.LayerIndex; var layer = doc.Layers.FindIndex(li); layerName2 = layer?.FullPath ?? layer?.Name ?? string.Empty; } catch { }
                arr.Add(new JObject
                {
                    ["uniqueId"] = uid,
                    ["elementId"] = eid,
                    ["category"] = cat,
                    ["typeName"] = typ,
                    ["rhinoId"] = obj.Id.ToString(),
                    ["layer"] = layerName2,
                    ["bboxMm"] = new JObject { ["min"] = min, ["max"] = max }
                });
            }
            return new JObject { ["ok"] = true, ["items"] = arr };
        }

        private static JObject RhinoFindByElement(JObject p)
        {
            var doc = RhinoDoc.ActiveDoc;
            if (doc == null) return new JObject { ["ok"] = false, ["msg"] = "No active doc" };

            var res = new JArray();
            var uids = p["uniqueIds"] as JArray;
            var eids = p["elementIds"] as JArray;
            foreach (var obj in doc.Objects)
            {
                var attr = obj.Attributes;
                var uid = attr.GetUserString("Revit.UniqueId") ?? attr.GetUserString("UniqueId");
                var eid = attr.GetUserString("Revit.ElementId");
                bool match = false;
                if (uids != null)
                {
                    foreach (var u in uids) { if ((string)u == uid) { match = true; break; } }
                }
                if (!match && eids != null)
                {
                    foreach (var e in eids) { if ((string)e == eid) { match = true; break; } }
                }
                if (!match) continue;
                res.Add(new JObject { ["uniqueId"] = uid, ["elementId"] = eid, ["rhinoId"] = obj.Id.ToString() });
            }
            return new JObject { ["ok"] = true, ["items"] = res };
        }

        private static JObject RhinoCollectBoxes(JObject p)
        {
            var doc = RhinoDoc.ActiveDoc;
            if (doc == null) return new JObject { ["ok"] = false, ["msg"] = "No active doc" };

            var filterUids = p["uniqueIds"] as JArray; // optional
            var res = new JArray();
            foreach (var obj in doc.Objects)
            {
                var attr = obj.Attributes;
                var uid = attr.GetUserString("Revit.UniqueId") ?? attr.GetUserString("UniqueId");
                if (filterUids != null && filterUids.Count > 0)
                {
                    bool hit = false; foreach (var u in filterUids) { if ((string)u == uid) { hit = true; break; } }
                    if (!hit) continue;
                }
                var g = obj.Geometry; if (g == null) continue;
                var bb = g.GetBoundingBox(true);
                var plane = Plane.WorldXY;
                var ext = new Vector3d((bb.Max.X - bb.Min.X) / 2.0, (bb.Max.Y - bb.Min.Y) / 2.0, (bb.Max.Z - bb.Min.Z) / 2.0);
                res.Add(new JObject
                {
                    ["uniqueId"] = uid ?? "",
                    ["rhinoId"] = obj.Id.ToString(),
                    ["plane"] = new JObject
                    {
                        ["origin"] = new JObject { ["x"] = plane.OriginX, ["y"] = plane.OriginY, ["z"] = plane.OriginZ },
                        ["xaxis"] = new JObject { ["x"] = plane.XAxis.X, ["y"] = plane.XAxis.Y, ["z"] = plane.XAxis.Z },
                        ["yaxis"] = new JObject { ["x"] = plane.YAxis.X, ["y"] = plane.YAxis.Y, ["z"] = plane.YAxis.Z },
                        ["zaxis"] = new JObject { ["x"] = plane.ZAxis.X, ["y"] = plane.ZAxis.Y, ["z"] = plane.ZAxis.Z }
                    },
                    ["extents"] = new JObject { ["x"] = ext.X, ["y"] = ext.Y, ["z"] = ext.Z, ["units"] = "mm" }
                });
            }
            return new JObject { ["ok"] = true, ["items"] = res };
        }

        private static JObject RhinoImportSnapshot(JObject root)
        {
            var doc = RhinoDoc.ActiveDoc;
            if (doc == null) return new JObject { ["ok"] = false, ["msg"] = "No active doc" };

            try
            {
                var mesh = new Mesh();
                var vertices = root["vertices"] as JArray;
                if (vertices == null) return new JObject { ["ok"] = false, ["msg"] = "No vertices" };
                double s = UnitUtil.FeetToMm(1.0);
                foreach (var v in vertices)
                {
                    var x = (double)v[0];
                    var y = (double)v[1];
                    var z = (double)v[2];
                    mesh.Vertices.Add((float)(x * s), (float)(y * s), (float)(z * s));
                }
                var submeshes = root["submeshes"] as JArray;
                if (submeshes != null)
                {
                    foreach (var sm in submeshes)
                    {
                        var idx = sm["intIndices"] as JArray ?? sm["indices"] as JArray; // tolerate naming
                        if (idx == null) continue;
                        for (int i = 0; i + 2 < idx.Count; i += 3)
                        {
                            int a = (int)idx[i + 0];
                            int b = (int)idx[i + 1];
                            int c = (int)idx[i + 2];
                            mesh.Faces.AddFace(a, b, c);
                        }
                    }
                }
                mesh.Normals.ComputeNormals();
                mesh.Compact();

                var defName = "RevitRef_" + (string)(root["uniqueId"] ?? Guid.NewGuid().ToString());
                var defId = doc.InstanceDefinitions.Add(defName, "Revit reference", Point3d.Origin, new System.Collections.Generic.List<GeometryBase> { mesh });
                if (defId < 0)
                {
                    // If definition already exists, reuse it to allow repeated imports
                    var existing = doc.InstanceDefinitions.Find(defName, true);
                    if (existing == null)
                        return new JObject { ["ok"] = false, ["msg"] = "Failed to create or find block definition" };
                    defId = existing.Index;
                }

                var iid = doc.Objects.AddInstanceObject(defId, Transform.Identity);
                var iobj = doc.Objects.FindId(iid);
                if (iobj == null) return new JObject { ["ok"] = false, ["msg"] = "Failed to place instance" };

                var ud = new UserData.RevitRefUserData
                {
                    RevitUniqueId = (string)(root["uniqueId"] ?? ""),
                    BaselineWorldXform = Transform.Identity,
                    Units = "feet",
                    ScaleToRhino = UnitUtil.FeetToMm(1.0),
                    SnapshotStamp = (string)(root["snapshotStamp"] ?? ""),
                    GeomHash = (string)(root["geomHash"] ?? "")
                };
                var attr = iobj.Attributes.Duplicate();
                attr.UserData.Add(ud);
                doc.Objects.ModifyAttributes(iobj, attr, true);

                doc.Views.Redraw();
                return new JObject { ["ok"] = true, ["msg"] = "imported" };
            }
            catch (Exception ex)
            {
                return new JObject { ["ok"] = false, ["msg"] = ex.Message };
            }
        }

        private static JObject RhinoGetSelection(JObject p)
        {
            var doc = RhinoDoc.ActiveDoc;
            if (doc == null) return new JObject { ["ok"] = false, ["msg"] = "No active doc" };

            var arr = new JArray();
            foreach (var obj in doc.Objects.GetSelectedObjects(false, false))
            {
                var ud = UserData.RevitRefUserData.From(obj);
                if (ud == null) continue;
                var instanceXform = (obj is InstanceObject io) ? io.InstanceXform : Transform.Identity;
                ud.BaselineWorldXform.TryGetInverse(out Transform inv);
                var delta = inv * instanceXform;
                if (!TransformUtil.TryExtractTROnly(delta, 1e-6, out Vector3d t_mm, out double yawDeg, out string err))
                {
                    continue;
                }
                var t_ft = t_mm / UnitUtil.FeetToMm(1.0);
                arr.Add(new JObject
                {
                    ["uniqueId"] = ud.RevitUniqueId,
                    ["delta"] = new JObject
                    {
                        ["translate"] = new JObject{ ["x"] = t_ft.X, ["y"] = t_ft.Y, ["z"] = t_ft.Z, ["units"] = "feet" },
                        ["rotateZDeg"] = yawDeg
                    },
                    ["guard"] = new JObject{ ["snapshotStamp"] = ud.SnapshotStamp, ["geomHash"] = ud.GeomHash }
                });
            }
            return new JObject { ["ok"] = true, ["items"] = arr };
        }

        private static JObject RhinoCommitTransform(JObject p)
        {
            var doc = RhinoDoc.ActiveDoc;
            if (doc == null) return new JObject { ["ok"] = false, ["msg"] = "No active doc" };

            int okCount = 0; int errCount = 0;
            foreach (var obj in doc.Objects.GetSelectedObjects(false, false))
            {
                var ud = UserData.RevitRefUserData.From(obj);
                if (ud == null) continue;
                var instanceXform = (obj is InstanceObject io) ? io.InstanceXform : Transform.Identity;
                ud.BaselineWorldXform.TryGetInverse(out Transform inv);
                var delta = inv * instanceXform;
                if (!TransformUtil.TryExtractTROnly(delta, 1e-6, out Vector3d t_mm, out double yawDeg, out string err))
                {
                    RhinoApp.WriteLine(err);
                    errCount++;
                    continue;
                }
                var t_ft = t_mm / UnitUtil.FeetToMm(1.0);
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
                            ["translate"] = new JObject { ["x"] = t_ft.X, ["y"] = t_ft.Y, ["z"] = t_ft.Z, ["units"] = "feet" },
                            ["rotateZDeg"] = yawDeg
                        },
                        ["guard"] = new JObject { ["snapshotStamp"] = ud.SnapshotStamp, ["geomHash"] = ud.GeomHash }
                    }
                };
                try
                {
                    var baseUrl = RhinoMcpPlugin.Instance.RevitMcpBaseUrl.TrimEnd('/');
                    HttpJsonRpcClient.PostJson(baseUrl + "/rpc", payload.ToString());
                    okCount++;
                }
                catch (Exception ex)
                {
                    RhinoApp.WriteLine("Commit failed: " + ex.Message);
                    errCount++;
                }
            }
            return new JObject { ["ok"] = errCount == 0, ["appliedCount"] = okCount, ["errors"] = errCount };
        }

        private static JObject RhinoLockUnlock(JObject p, bool locked)
        {
            var doc = RhinoDoc.ActiveDoc;
            if (doc == null) return new JObject { ["ok"] = false, ["msg"] = "No active doc" };

            int count = 0;
            var ids = p["uniqueIds"] as JArray;
            if (ids != null && ids.Count > 0)
            {
                foreach (var obj in doc.Objects)
                {
                    var ud = UserData.RevitRefUserData.From(obj);
                    if (ud == null) continue;
                    foreach (var id in ids)
                    {
                        if ((string)id == ud.RevitUniqueId)
                        {
                            if (locked) doc.Objects.Lock(obj.Id, true); else doc.Objects.Unlock(obj.Id, true);
                            count++;
                            break;
                        }
                    }
                }
            }
            else
            {
                foreach (var obj in doc.Objects.GetSelectedObjects(false, false))
                {
                    if (locked) doc.Objects.Lock(obj.Id, true); else doc.Objects.Unlock(obj.Id, true);
                    count++;
                }
            }

            doc.Views.Redraw();
            return new JObject { ["ok"] = true, ["count"] = count };
        }
    }
}
