using System;
using System.IO;
using Rhino;
using Rhino.Commands;
using Rhino.Input;
using Rhino.Input.Custom;
using Newtonsoft.Json.Linq;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace RhinoMcpPlugin.Commands
{
    public class McpImportCommand : Command
    {
        public override string EnglishName => "McpImport";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            // Ask for a file path via prompt for Rhino 7 compatibility
            var gs = new GetString();
            gs.SetCommandPrompt("Enter JSON file path to import (Revit snapshot)");
            gs.AcceptNothing(false);
            if (gs.Get() != GetResult.String) return Result.Cancel;
            var path = gs.StringResult().Trim('"');
            if (!File.Exists(path)) { RhinoApp.WriteLine("File not found: " + path); return Result.Failure; }

            var json = File.ReadAllText(path);
            var root = JToken.Parse(json);

            // Accept both single-element and view export formats
            // Expect keys: vertices, submeshes, uniqueId, transform, materials...
            try
            {
                var mesh = new Mesh();
                var vertices = root["vertices"] as JArray;
                if (vertices == null) { RhinoApp.WriteLine("No 'vertices' found."); return Result.Failure; }
                foreach (var v in vertices)
                {
                    var x = (double)v[0];
                    var y = (double)v[1];
                    var z = (double)v[2];
                    // Convert feet -> mm
                    var s = Core.UnitUtil.FeetToMm(1.0);
                    mesh.Vertices.Add((float)(x*s), (float)(y*s), (float)(z*s));
                }

                // Faces from submeshes indices
                var submeshes = root["submeshes"] as JArray;
                if (submeshes != null)
                {
                    foreach (var sm in submeshes)
                    {
                        var indices = sm["indices"] as JArray;
                        for (int i = 0; i + 2 < indices.Count; i += 3)
                        {
                            int a = (int)indices[i + 0];
                            int b = (int)indices[i + 1];
                            int c = (int)indices[i + 2];
                            mesh.Faces.AddFace(a, b, c);
                        }
                    }
                }
                mesh.Normals.ComputeNormals();
                mesh.Compact();

                // Create block definition
                var defName = "RevitRef_" + (string)(root["uniqueId"] ?? Guid.NewGuid().ToString());
                var basePlane = Plane.WorldXY;
                var defId = doc.InstanceDefinitions.Add(defName, "Revit reference", Point3d.Origin, new System.Collections.Generic.List<GeometryBase> { mesh });
                if (defId < 0) { RhinoApp.WriteLine("Failed to create block definition."); return Result.Failure; }

                // Place instance
                var xform = Transform.Identity; // baseline as identity in Rhino space (already scaled to mm)
                var iid = doc.Objects.AddInstanceObject(defId, xform);
                var iobj = doc.Objects.FindId(iid);
                if (iobj == null) { RhinoApp.WriteLine("Failed to place instance."); return Result.Failure; }

                // Attach UserData
                var ud = new Core.UserData.RevitRefUserData
                {
                    RevitUniqueId = (string)(root["uniqueId"] ?? ""),
                    BaselineWorldXform = Transform.Identity, // since we imported at world coords in mm
                    Units = "feet",
                    ScaleToRhino = Core.UnitUtil.FeetToMm(1.0),
                    SnapshotStamp = (string)(root["snapshotStamp"] ?? ""),
                    GeomHash = (string)(root["geomHash"] ?? "")
                };
                var attr = iobj.Attributes.Duplicate();
                attr.UserData.Add(ud);
                doc.Objects.ModifyAttributes(iobj, attr, true);

                doc.Views.Redraw();
                Rhino.RhinoApp.WriteLine("Imported 1 element.");
                return Result.Success;
            }
            catch (Exception ex)
            {
                Rhino.RhinoApp.WriteLine("Import failed: " + ex.Message);
                return Result.Failure;
            }
        }
    }
}
