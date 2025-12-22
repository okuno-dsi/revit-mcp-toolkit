// ================================================================
// File   : Commands/LookupOps/LookupElementCommand.cs
// Target : .NET Framework 4.8 (Revit 2023+)
// Author : RevitMCP Project
// Purpose: RevitLookup-style inspection of a single Element as JSON
// Command: "lookup_element"
// ================================================================

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.LookupOps
{
    /// <summary>
    /// Provides a RevitLookup-style inspection of a single Element.
    /// Exposed as a Revit MCP command: "lookup_element".
    /// </summary>
    public class LookupElementCommand : IRevitCommandHandler
    {
        public string CommandName => "lookup_element";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var payload = cmd.Params as JObject ?? new JObject();
            try
            {
                bool includeGeometry = payload.Value<bool?>("includeGeometry") ?? true;
                bool includeRelations = payload.Value<bool?>("includeRelations") ?? true;

                var element = FindElement(doc, payload);
                if (element == null)
                {
                    return new
                    {
                        ok = false,
                        msg = "Element not found for the given identifiers.",
                        detail = $"uniqueId={payload.Value<string?>("uniqueId")}, elementId={payload.Value<int?>("elementId")}"
                    };
                }

                var elementJson = BuildElementJson(doc, element, includeGeometry, includeRelations);
                return new
                {
                    ok = true,
                    element = elementJson
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    ok = false,
                    msg = "lookup_element failed with an exception.",
                    detail = ex.ToString()
                };
            }
        }

        /// <summary>
        /// Resolves the element either by UniqueId or ElementId (IntegerValue).
        /// </summary>
        private static Element? FindElement(Document doc, JObject payload)
        {
            string? uniqueId = payload.Value<string?>("uniqueId");
            int? elementIdInt = payload.Value<int?>("elementId");

            Element? element = null;

            if (!string.IsNullOrWhiteSpace(uniqueId))
            {
                element = doc.GetElement(uniqueId);
            }

            if (element == null && elementIdInt.HasValue)
            {
                var id = Autodesk.Revit.DB.ElementIdCompat.From(elementIdInt.Value);
                element = doc.GetElement(id);
            }

            return element;
        }

        /// <summary>
        /// Builds the JSON representation of the element, including identity, location,
        /// parameters, and optional geometry / relations.
        /// </summary>
        private static JObject BuildElementJson(
            Document doc,
            Element element,
            bool includeGeometry,
            bool includeRelations)
        {
            bool isElementType = element is ElementType;
            Category? cat = element.Category;
            ElementId typeId = element.GetTypeId();
            ElementType? elemType = (typeId != ElementId.InvalidElementId)
                ? doc.GetElement(typeId) as ElementType
                : null;

            // Level
            JObject levelJson = BuildLevelJson(doc, element);

            // Workset
            JObject worksetJson = BuildWorksetJson(doc, element);

            // Design Option
            JObject designOptionJson = BuildDesignOptionJson(doc, element);

            // Location
            JObject? locationJson = BuildLocationJson(element);

            // Bounding box (in model coordinates)
            JObject? bboxJson = BuildBoundingBoxJson(element);

            // Geometry summary (lightweight)
            JObject? geomSummaryJson = includeGeometry ? BuildGeometrySummaryJson(doc, element) : null;

            // Parameters
            JArray parametersJson = BuildParametersJson(element);

            // Relations (host, group, etc.)
            JObject? relationsJson = includeRelations ? BuildRelationsJson(element) : null;

            var elemJson = new JObject
            {
                ["id"] = element.Id.IntValue(),
                ["uniqueId"] = element.UniqueId,
                ["category"] = cat?.Name,
                ["categoryId"] = cat?.Id.IntValue(),
                ["familyName"] = elemType?.FamilyName,
                ["typeName"] = elemType?.Name,
                ["isElementType"] = isElementType,
                ["level"] = levelJson,
                ["workset"] = worksetJson,
                ["designOption"] = designOptionJson,
                ["location"] = locationJson,
                ["boundingBox"] = bboxJson,
                ["parameters"] = parametersJson
            };

            if (geomSummaryJson != null)
            {
                elemJson["geometrySummary"] = geomSummaryJson;
            }
            if (relationsJson != null)
            {
                elemJson["relations"] = relationsJson;
            }

            return elemJson;
        }

        private static JObject BuildLevelJson(Document doc, Element element)
        {
            try
            {
                ElementId levelId = element.LevelId;
                if (levelId == ElementId.InvalidElementId)
                {
                    return new JObject
                    {
                        ["id"] = null,
                        ["name"] = null
                    };
                }

                Level? level = doc.GetElement(levelId) as Level;
                return new JObject
                {
                    ["id"] = levelId.IntValue(),
                    ["name"] = level?.Name
                };
            }
            catch
            {
                return new JObject { ["id"] = null, ["name"] = null };
            }
        }

        private static JObject BuildWorksetJson(Document doc, Element element)
        {
            try
            {
                WorksetId wsId = element.WorksetId;
                if (wsId == WorksetId.InvalidWorksetId)
                {
                    return new JObject
                    {
                        ["id"] = null,
                        ["name"] = null
                    };
                }

                Workset? ws = doc.GetWorksetTable().GetWorkset(wsId);
                return new JObject
                {
                    ["id"] = wsId.IntValue(),
                    ["name"] = ws?.Name
                };
            }
            catch
            {
                // Some elements may throw; be defensive.
                return new JObject
                {
                    ["id"] = null,
                    ["name"] = null
                };
            }
        }

        private static JObject BuildDesignOptionJson(Document doc, Element element)
        {
            try
            {
                DesignOption? opt = element.DesignOption;
                if (opt == null)
                {
                    return new JObject
                    {
                        ["id"] = null,
                        ["name"] = null
                    };
                }

                return new JObject
                {
                    ["id"] = opt.Id.IntValue(),
                    ["name"] = opt.Name
                };
            }
            catch
            {
                return new JObject
                {
                    ["id"] = null,
                    ["name"] = null
                };
            }
        }

        private static JObject? BuildLocationJson(Element element)
        {
            Location? loc = element.Location;
            if (loc == null)
            {
                // ロケーションを持たない要素（ビュー専用要素など）は null を返す
                return null;
            }

            if (loc is LocationPoint lp)
            {
                // 一部の要素では Rotation プロパティが未サポートなので防御的に扱う
                double? rot = null;
                try
                {
                    rot = lp.Rotation;
                }
                catch
                {
                    // Rotation が未サポートの場合は省略
                }

                var jo = new JObject
                {
                    ["kind"] = "LocationPoint",
                    ["point"] = ToPointJson(lp.Point)
                };
                if (rot.HasValue)
                    jo["rotation"] = rot.Value;
                return jo;
            }

            if (loc is LocationCurve lc)
            {
                Curve? c = null;
                try
                {
                    c = lc.Curve;
                }
                catch
                {
                    c = null;
                }

                if (c == null)
                {
                    return new JObject
                    {
                        ["kind"] = "LocationCurve",
                        ["curveType"] = "Unknown"
                    };
                }

                string curveType = c switch
                {
                    Line _ => "Line",
                    Arc _ => "Arc",
                    HermiteSpline _ => "HermiteSpline",
                    NurbSpline _ => "NurbSpline",
                    _ => c.GetType().Name
                };

                JObject start = ToPointJson(c.GetEndPoint(0));
                JObject end = ToPointJson(c.GetEndPoint(1));

                return new JObject
                {
                    ["kind"] = "LocationCurve",
                    ["curveType"] = curveType,
                    ["start"] = start,
                    ["end"] = end
                };
            }

            // その他の Location 種別（まれ）: 型名だけ返す
            return new JObject
            {
                ["kind"] = loc.GetType().Name
            };
        }

        private static JObject? BuildBoundingBoxJson(Element element)
        {
            try
            {
                BoundingBoxXYZ? bb = element.get_BoundingBox(null);
                if (bb == null)
                {
                    return null;
                }

                return new JObject
                {
                    ["min"] = ToPointJson(bb.Min),
                    ["max"] = ToPointJson(bb.Max)
                };
            }
            catch
            {
                return null;
            }
        }

        private static JObject? BuildGeometrySummaryJson(Document doc, Element element)
        {
            try
            {
                Options opt = new Options
                {
                    ComputeReferences = false,
                    IncludeNonVisibleObjects = false,
                    DetailLevel = ViewDetailLevel.Coarse
                };

                GeometryElement? geo = element.get_Geometry(opt);
                if (geo == null)
                    return null;

                int solidCount = 0;
                double vol = 0.0;
                double area = 0.0;

                foreach (var obj in geo)
                {
                    if (obj is Solid solid && solid.Volume > 0)
                    {
                        solidCount++;
                        vol += solid.Volume;
                        area += solid.SurfaceArea;
                    }
                }

                if (solidCount == 0)
                    return new JObject { ["hasSolid"] = false };

                // Convert internal ft^3 / ft^2 to something readable (m3, m2)
                double volM3 = UnitUtils.ConvertFromInternalUnits(vol, UnitTypeId.CubicMeters);
                double areaM2 = UnitUtils.ConvertFromInternalUnits(area, UnitTypeId.SquareMeters);

                return new JObject
                {
                    ["hasSolid"] = true,
                    ["solidCount"] = solidCount,
                    ["approxVolume"] = Math.Round(volM3, 3),
                    ["approxSurfaceArea"] = Math.Round(areaM2, 3)
                };
            }
            catch
            {
                return null;
            }
        }

        private static JArray BuildParametersJson(Element element)
        {
            var arr = new JArray();
            foreach (Parameter p in element.Parameters)
            {
                try
                {
                    if (p.Definition == null) continue;

                    var def = p.Definition;
                    string name = def.Name;
                    StorageType st = p.StorageType;

                    string? builtinName = null;
                    try
                    {
                        var bip = (BuiltInParameter)p.Id.IntValue();
                        builtinName = Enum.GetName(typeof(BuiltInParameter), bip);
                    }
                    catch { }

                    bool isShared = p.IsShared;

                    Autodesk.Revit.DB.ForgeTypeId grpId = null;
                    string grpTypeId = null;
                    string grpLabel = null;
                    try
                    {
                        grpId = def.GetGroupTypeId();
                        grpTypeId = grpId != null ? grpId.TypeId : null;
                        grpLabel = grpId != null ? LabelUtils.GetLabelForGroup(grpId) : null;
                    }
                    catch { }

                    string valueStr = GetParameterValueString(p);
                    string displayStr = p.AsValueString() ?? valueStr;

                    bool isInstance = !(element is ElementType);

                    Guid? guid = null;
                    try
                    {
                        if (isShared)
                        {
                            guid = p.GUID;
                        }
                    }
                    catch { }

                    var jp = new JObject
                    {
                        ["name"] = name,
                        ["builtin"] = builtinName,
                        ["storageType"] = st.ToString(),
                        ["parameterGroup"] = grpTypeId,
                        ["parameterGroupLabel"] = grpLabel,
                        ["isReadOnly"] = p.IsReadOnly,
                        ["isShared"] = isShared,
                        ["isInstance"] = isInstance,
                        ["guid"] = guid != null ? guid.ToString() : null,
                        ["value"] = valueStr,
                        ["displayValue"] = displayStr
                    };
                    arr.Add(jp);
                }
                catch
                {
                    // ignore problematic parameters
                }
            }
            return arr;
        }

        private static JObject? BuildRelationsJson(Element e)
        {
            try
            {
                ElementId hostId = ElementId.InvalidElementId;
                ElementId superId = ElementId.InvalidElementId;
                ElementId groupId = ElementId.InvalidElementId;

                if (e is FamilyInstance fi)
                {
                    hostId = fi.Host?.Id ?? ElementId.InvalidElementId;
                    superId = fi.SuperComponent?.Id ?? ElementId.InvalidElementId;
                }

                if (e.GroupId != null && e.GroupId != ElementId.InvalidElementId)
                {
                    groupId = e.GroupId;
                }

                return new JObject
                {
                    ["hostId"] = hostId != ElementId.InvalidElementId ? hostId.IntValue() : (int?)null,
                    ["superComponentId"] = superId != ElementId.InvalidElementId ? superId.IntValue() : (int?)null,
                    ["groupId"] = groupId != ElementId.InvalidElementId ? groupId.IntValue() : (int?)null
                };
            }
            catch
            {
                return null;
            }
        }

        private static JObject ToPointJson(XYZ p)
        {
            return new JObject
            {
                ["x"] = p.X,
                ["y"] = p.Y,
                ["z"] = p.Z
            };
        }

        private static string GetParameterValueString(Parameter p)
        {
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.Double:
                        return p.AsDouble().ToString("G17");
                    case StorageType.Integer:
                        return p.AsInteger().ToString();
                    case StorageType.String:
                        return p.AsString() ?? string.Empty;
                    case StorageType.ElementId:
                        var id = p.AsElementId();
                        return id != null && id != ElementId.InvalidElementId ? id.IntValue().ToString() : string.Empty;
                    default:
                        return string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}


