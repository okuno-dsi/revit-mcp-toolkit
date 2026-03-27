#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    internal sealed partial class FamilyBatchParameterService
    {
        private static JObject FileResult(string filePath, bool ok, string action, bool saved, string savePath, string? backupPath, int addedCount, int skippedCount, int failedCount, long elapsedMs, IEnumerable<string> messages, IEnumerable<JObject> parameterResults)
        {
            return new JObject
            {
                ["filePath"] = filePath,
                ["ok"] = ok,
                ["action"] = action,
                ["saved"] = saved,
                ["savePath"] = savePath,
                ["backupPath"] = backupPath ?? string.Empty,
                ["addedCount"] = addedCount,
                ["skippedCount"] = skippedCount,
                ["failedCount"] = failedCount,
                ["elapsedMs"] = elapsedMs,
                ["messages"] = JArray.FromObject(messages.ToArray()),
                ["parameterResults"] = new JArray(parameterResults)
            };
        }

        private static JObject ParameterResult(string? parameterName, bool ok, string action, string message, bool isShared, bool? isInstance, bool? existing)
        {
            return new JObject
            {
                ["parameterName"] = parameterName ?? string.Empty,
                ["ok"] = ok,
                ["action"] = action,
                ["message"] = message,
                ["isShared"] = isShared,
                ["isInstance"] = isInstance.HasValue ? JToken.FromObject(isInstance.Value) : JValue.CreateNull(),
                ["existing"] = existing.HasValue ? JToken.FromObject(existing.Value) : JValue.CreateNull()
            };
        }

        private bool IsDocumentAlreadyOpen(string filePath)
        {
            try
            {
                foreach (Document doc in _app.Documents)
                {
                    try
                    {
                        if (doc == null) continue;
                        if (PathsEqual(doc.PathName, filePath)) return true;
                    }
                    catch { }
                }
            }
            catch { }

            return false;
        }

        private static FamilyParameter? FindExistingByName(FamilyManager fm, string parameterName)
        {
            foreach (FamilyParameter fp in fm.Parameters)
            {
                if (string.Equals(GetParameterName(fp), parameterName, StringComparison.Ordinal))
                    return fp;
            }
            return null;
        }

        private static FamilyParameter? FindExistingSharedByGuid(FamilyManager fm, Guid guid)
        {
            foreach (FamilyParameter fp in fm.Parameters)
            {
                var existingGuid = TryGetSharedGuid(fp);
                if (existingGuid.HasValue && existingGuid.Value == guid)
                    return fp;
            }
            return null;
        }

        private static string GetParameterName(FamilyParameter fp)
        {
            try { return fp.Definition?.Name ?? string.Empty; } catch { return string.Empty; }
        }

        private static bool IsShared(FamilyParameter fp)
        {
            try { return fp.IsShared; } catch { return TryGetSharedGuid(fp).HasValue; }
        }

        private static Guid? TryGetSharedGuid(FamilyParameter fp)
        {
            try
            {
                var pi = fp.GetType().GetProperty("GUID", BindingFlags.Public | BindingFlags.Instance);
                var value = pi != null ? pi.GetValue(fp, null) : null;
                if (value is Guid g && g != Guid.Empty) return g;
            }
            catch { }
            return null;
        }

        private static ForgeTypeId? GetParameterDataType(FamilyParameter fp)
        {
            try { return fp.Definition?.GetDataType(); } catch { return null; }
        }

        private static ForgeTypeId? GetParameterGroup(FamilyParameter fp)
        {
            try { return fp.Definition?.GetGroupTypeId(); } catch { return null; }
        }

        private static bool ForgeTypeIdEquals(ForgeTypeId? a, ForgeTypeId? b)
        {
            var sa = SafeTypeId(a);
            var sb = SafeTypeId(b);
            if (string.IsNullOrWhiteSpace(sa) && string.IsNullOrWhiteSpace(sb)) return true;
            return string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase);
        }

        private static string SafeTypeId(ForgeTypeId? id)
        {
            try { return id?.TypeId ?? string.Empty; } catch { return string.Empty; }
        }

        private static void InvokeAddSharedParameter(FamilyManager familyManager, ExternalDefinition definition, ForgeTypeId groupTypeId, string groupRaw, bool isInstance)
        {
            var methods = typeof(FamilyManager).GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => string.Equals(m.Name, "AddParameter", StringComparison.Ordinal)).ToList();
            Exception? lastError = null;
            var bipg = TryResolveBuiltInParameterGroup(groupRaw);
            var attempts = new List<string>();

            if (bipg != null)
            {
                foreach (var method in methods)
                {
                    var ps = method.GetParameters();
                    if (ps.Length == 3 && typeof(ExternalDefinition).IsAssignableFrom(ps[0].ParameterType) && string.Equals(ps[1].ParameterType.FullName, "Autodesk.Revit.DB.BuiltInParameterGroup", StringComparison.Ordinal) && ps[2].ParameterType == typeof(bool))
                    {
                        try
                        {
                            method.Invoke(familyManager, new[] { (object)definition, bipg, isInstance });
                            return;
                        }
                        catch (TargetInvocationException tie)
                        {
                            lastError = tie.InnerException ?? tie;
                            attempts.Add($"{method}: {GetInnermostMessage(lastError)}");
                        }
                        catch (Exception ex)
                        {
                            lastError = ex;
                            attempts.Add($"{method}: {GetInnermostMessage(lastError)}");
                        }
                    }
                }
            }

            foreach (var method in methods)
            {
                var ps = method.GetParameters();
                if (ps.Length == 3 && typeof(ExternalDefinition).IsAssignableFrom(ps[0].ParameterType) && ps[1].ParameterType == typeof(ForgeTypeId) && ps[2].ParameterType == typeof(bool))
                {
                    try
                    {
                        method.Invoke(familyManager, new object[] { definition, groupTypeId, isInstance });
                        return;
                    }
                    catch (TargetInvocationException tie)
                    {
                        lastError = tie.InnerException ?? tie;
                        attempts.Add($"{method}: {GetInnermostMessage(lastError)}");
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        attempts.Add($"{method}: {GetInnermostMessage(lastError)}");
                    }
                }
            }

            if (lastError != null)
            {
                var detail = attempts.Count > 0 ? string.Join(" | ", attempts) : GetInnermostMessage(lastError);
                throw new InvalidOperationException(detail, lastError);
            }
            throw new MissingMethodException("FamilyManager.AddParameter shared-parameter overload was not found.");
        }

        private static void InvokeAddFamilyParameter(FamilyManager familyManager, string parameterName, ForgeTypeId groupTypeId, string groupRaw, ForgeTypeId specTypeId, string familySpecTypeRaw, bool isInstance)
        {
            var methods = typeof(FamilyManager).GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => string.Equals(m.Name, "AddParameter", StringComparison.Ordinal)).ToList();
            Exception? lastError = null;
            var bipg = TryResolveBuiltInParameterGroup(groupRaw);
            var oldParamType = TryResolveLegacyParameterType(familySpecTypeRaw);

            if (bipg != null && oldParamType != null)
            {
                foreach (var method in methods)
                {
                    var ps = method.GetParameters();
                    if (ps.Length == 4 && ps[0].ParameterType == typeof(string) && string.Equals(ps[1].ParameterType.FullName, "Autodesk.Revit.DB.BuiltInParameterGroup", StringComparison.Ordinal) && string.Equals(ps[2].ParameterType.FullName, "Autodesk.Revit.DB.ParameterType", StringComparison.Ordinal) && ps[3].ParameterType == typeof(bool))
                    {
                        try
                        {
                            method.Invoke(familyManager, new[] { (object)parameterName, bipg, oldParamType, isInstance });
                            return;
                        }
                        catch (TargetInvocationException tie)
                        {
                            lastError = tie.InnerException ?? tie;
                        }
                        catch (Exception ex)
                        {
                            lastError = ex;
                        }
                    }
                }
            }

            foreach (var method in methods)
            {
                var ps = method.GetParameters();
                if (ps.Length == 4 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(ForgeTypeId) && ps[2].ParameterType == typeof(ForgeTypeId) && ps[3].ParameterType == typeof(bool))
                {
                    try
                    {
                        method.Invoke(familyManager, new object[] { parameterName, groupTypeId, specTypeId, isInstance });
                        return;
                    }
                    catch (TargetInvocationException tie)
                    {
                        lastError = tie.InnerException ?? tie;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                    }
                }
            }

            if (lastError != null) throw new InvalidOperationException(GetInnermostMessage(lastError), lastError);
            throw new MissingMethodException("FamilyManager.AddParameter family-parameter overload was not found.");
        }

        private static void SaveFamilyAsCopy(Document familyDoc, string savePath)
        {
            var opts = new SaveAsOptions { OverwriteExistingFile = true, Compact = false, MaximumBackups = 1 };
            familyDoc.SaveAs(savePath, opts);
        }

        private void InvokeAddSharedParameterFromRequest(BatchRequest request, ParameterSpec parameter, FamilyManager familyManager, ForgeTypeId groupTypeId)
        {
            var sharedFilePath = !string.IsNullOrWhiteSpace(parameter.SharedParameterFile) ? parameter.SharedParameterFile : request.DefaultSharedParameterFile;
            if (string.IsNullOrWhiteSpace(sharedFilePath) || !File.Exists(sharedFilePath))
                throw new InvalidOperationException("Shared parameter file could not be opened.");

            var definitionGroupName = !string.IsNullOrWhiteSpace(parameter.SharedParameterGroupName) ? parameter.SharedParameterGroupName : request.DefaultSharedParameterGroupName;
            if (string.IsNullOrWhiteSpace(definitionGroupName))
                definitionGroupName = "Common";

            var definitionName = !string.IsNullOrWhiteSpace(parameter.SharedParameterDefinitionName) ? parameter.SharedParameterDefinitionName : parameter.ParameterName;

            using (var scope = new SharedParameterFileScope(_app, sharedFilePath))
            {
                var definition = scope.FindDefinition(definitionGroupName, definitionName, parameter.SharedParameterGuid, out var error);
                if (!(definition is ExternalDefinition externalDefinition))
                    throw new InvalidOperationException(error ?? $"Shared parameter definition '{definitionName}' was not found in group '{definitionGroupName}'.");
                InvokeAddSharedParameter(familyManager, externalDefinition, groupTypeId, parameter.ParameterGroupRaw, parameter.IsInstance);
            }
        }

        private static bool EnsureWritableFamilyType(FamilyManager familyManager, out bool createdTemporaryType, out string? temporaryTypeName)
        {
            createdTemporaryType = false;
            temporaryTypeName = null;

            try
            {
                if (familyManager.CurrentType != null)
                    return true;
            }
            catch { }

            try
            {
                temporaryTypeName = "__RMCP_TEMP_" + DateTime.Now.ToString("yyyyMMdd_HHmmssfff");
                var created = familyManager.NewType(temporaryTypeName);
                if (created != null)
                {
                    createdTemporaryType = true;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static void TryCleanupTemporaryFamilyType(Document familyDoc, FamilyManager familyManager, string temporaryTypeName, List<string> messages)
        {
            try
            {
                var current = familyManager.CurrentType;
                if (current == null || !string.Equals(current.Name, temporaryTypeName, StringComparison.Ordinal))
                    return;

                using (var tx = new Transaction(familyDoc, "Cleanup temporary family type"))
                {
                    tx.Start();
                    TxnUtil.ConfigureProceedWithWarnings(tx);
                    try
                    {
                        familyManager.DeleteCurrentType();
                        tx.Commit();
                        messages.Add($"Temporary family type removed: {temporaryTypeName}");
                    }
                    catch (Exception ex)
                    {
                        try { tx.RollBack(); } catch { }
                        messages.Add($"Temporary family type could not be removed: {GetInnermostMessage(ex)}");
                    }
                }
            }
            catch (Exception ex)
            {
                messages.Add($"Temporary family type cleanup failed: {GetInnermostMessage(ex)}");
            }
        }

        private static ForgeTypeId? ResolveGroupTypeIdOrDefault(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) raw = "PG_DATA";
            var normalized = raw.Trim();
            if (normalized == "機械設備") normalized = "Mechanical";

            string path;
            if (normalized.StartsWith("GroupTypeId.", StringComparison.OrdinalIgnoreCase))
                path = "Autodesk.Revit.DB." + normalized;
            else if (normalized.StartsWith("Autodesk.Revit.DB.GroupTypeId.", StringComparison.OrdinalIgnoreCase))
                path = normalized;
            else if (normalized.StartsWith("PG_", StringComparison.OrdinalIgnoreCase))
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PG_TEXT"] = "Autodesk.Revit.DB.GroupTypeId.Text",
                    ["PG_DATA"] = "Autodesk.Revit.DB.GroupTypeId.Data",
                    ["PG_IDENTITY_DATA"] = "Autodesk.Revit.DB.GroupTypeId.IdentityData",
                    ["PG_CONSTRAINTS"] = "Autodesk.Revit.DB.GroupTypeId.Constraints",
                    ["PG_DIMENSIONS"] = "Autodesk.Revit.DB.GroupTypeId.Dimensions",
                    ["PG_MATERIALS"] = "Autodesk.Revit.DB.GroupTypeId.Materials",
                    ["PG_GEOMETRY"] = "Autodesk.Revit.DB.GroupTypeId.Geometry",
                    ["PG_PHASING"] = "Autodesk.Revit.DB.GroupTypeId.Phasing",
                    ["PG_STRUCTURAL"] = "Autodesk.Revit.DB.GroupTypeId.Structural",
                    ["PG_GRAPHICS"] = "Autodesk.Revit.DB.GroupTypeId.Graphics"
                };
                if (!map.TryGetValue(normalized, out path!)) return null;
            }
            else
            {
                path = "Autodesk.Revit.DB.GroupTypeId." + normalized;
            }

            return TryResolveForgeTypeIdByPath(path);
        }

        private static object? TryResolveBuiltInParameterGroup(string raw)
        {
            var enumType = typeof(Element).Assembly.GetType("Autodesk.Revit.DB.BuiltInParameterGroup");
            if (enumType == null) return null;

            var normalized = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                normalized = "PG_DATA";

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["機械設備"] = "PG_MECHANICAL",
                ["Mechanical"] = "PG_MECHANICAL",
                ["Text"] = "PG_TEXT",
                ["Data"] = "PG_DATA",
                ["IdentityData"] = "PG_IDENTITY_DATA",
                ["Constraints"] = "PG_CONSTRAINTS",
                ["Dimensions"] = "PG_GEOMETRY",
                ["Materials"] = "PG_MATERIALS",
                ["Geometry"] = "PG_GEOMETRY",
                ["Phasing"] = "PG_PHASING",
                ["Structural"] = "PG_STRUCTURAL",
                ["Graphics"] = "PG_GRAPHICS"
            };

            if (!normalized.StartsWith("PG_", StringComparison.OrdinalIgnoreCase) && map.TryGetValue(normalized, out var mapped))
                normalized = mapped;

            try
            {
                return Enum.Parse(enumType, normalized, true);
            }
            catch
            {
                return null;
            }
        }

        private static object? TryResolveLegacyParameterType(string raw)
        {
            var enumType = typeof(Element).Assembly.GetType("Autodesk.Revit.DB.ParameterType");
            if (enumType == null) return null;

            var normalized = (raw ?? string.Empty).Trim();
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["text"] = "Text",
                ["integer"] = "Integer",
                ["number"] = "Number",
                ["length"] = "Length",
                ["area"] = "Area",
                ["volume"] = "Volume",
                ["angle"] = "Angle",
                ["yesno"] = "YesNo",
                ["yes_no"] = "YesNo",
                ["boolean"] = "YesNo",
                ["bool"] = "YesNo"
            };

            if (!map.TryGetValue(normalized, out var enumName))
                return null;

            try
            {
                return Enum.Parse(enumType, enumName, true);
            }
            catch
            {
                return null;
            }
        }

        private static string GetInnermostMessage(Exception ex)
        {
            Exception current = ex;
            while (current.InnerException != null)
                current = current.InnerException;
            return string.IsNullOrWhiteSpace(current.Message) ? ex.Message : current.Message;
        }

        private static ForgeTypeId? ResolveFamilySpecTypeOrNull(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["text"] = new[] { "Autodesk.Revit.DB.SpecTypeId.String.Text" },
                ["integer"] = new[] { "Autodesk.Revit.DB.SpecTypeId.Int.Integer" },
                ["number"] = new[] { "Autodesk.Revit.DB.SpecTypeId.Number" },
                ["length"] = new[] { "Autodesk.Revit.DB.SpecTypeId.Length" },
                ["area"] = new[] { "Autodesk.Revit.DB.SpecTypeId.Area" },
                ["volume"] = new[] { "Autodesk.Revit.DB.SpecTypeId.Volume" },
                ["angle"] = new[] { "Autodesk.Revit.DB.SpecTypeId.Angle" },
                ["yesno"] = new[] { "Autodesk.Revit.DB.SpecTypeId.Boolean.YesNo" },
                ["yes_no"] = new[] { "Autodesk.Revit.DB.SpecTypeId.Boolean.YesNo" },
                ["boolean"] = new[] { "Autodesk.Revit.DB.SpecTypeId.Boolean.YesNo" },
                ["bool"] = new[] { "Autodesk.Revit.DB.SpecTypeId.Boolean.YesNo" }
            };

            if (!map.TryGetValue(raw.Trim(), out var paths))
                return null;

            foreach (var path in paths)
            {
                var resolved = TryResolveForgeTypeIdByPath(path);
                if (resolved != null) return resolved;
            }
            return null;
        }

        private static ForgeTypeId? TryResolveForgeTypeIdByPath(string path)
        {
            try
            {
                var parts = path.Split('.');
                if (parts.Length < 5) return null;
                var baseTypeName = string.Join(".", parts[0], parts[1], parts[2], parts[3]);
                var type = Type.GetType(baseTypeName) ?? typeof(Element).Assembly.GetType(baseTypeName);
                if (type == null) return null;

                object? current = null;
                for (int i = 4; i < parts.Length; i++)
                {
                    var name = parts[i];
                    var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
                    if (prop != null)
                    {
                        current = prop.GetValue(null, null);
                        if (current is ForgeTypeId id) return id;
                        if (current != null) type = current.GetType();
                        continue;
                    }

                    var nested = type.GetNestedType(name, BindingFlags.Public);
                    if (nested != null)
                    {
                        type = nested;
                        continue;
                    }
                    return null;
                }

                return current as ForgeTypeId;
            }
            catch
            {
                return null;
            }
        }

        private static string BuildMirroredTargetPath(string rootFolder, string sourceRoot, string filePath)
        {
            var root = NormalizePath(rootFolder);
            var source = NormalizePath(sourceRoot);
            var full = NormalizePath(filePath);
            var relative = GetRelativePathCompat(source, full);
            return NormalizePath(Path.Combine(root, relative));
        }

        private static bool PathsEqual(string? a, string? b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            return string.Equals(NormalizePath(a), NormalizePath(b), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try { return Path.GetFullPath(path.Trim()); } catch { return path.Trim(); }
        }

        private static string GetRelativePathCompat(string basePath, string targetPath)
        {
            var normalizedBase = NormalizePath(basePath);
            var normalizedTarget = NormalizePath(targetPath);
            if (string.IsNullOrWhiteSpace(normalizedBase) || string.IsNullOrWhiteSpace(normalizedTarget))
                return normalizedTarget;

            try
            {
                var baseDirectory = normalizedBase.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                var baseUri = new Uri(baseDirectory, UriKind.Absolute);
                var targetUri = new Uri(normalizedTarget, UriKind.Absolute);
                var relative = Uri.UnescapeDataString(baseUri.MakeRelativeUri(targetUri).ToString())
                    .Replace('/', Path.DirectorySeparatorChar);
                return string.IsNullOrWhiteSpace(relative) ? Path.GetFileName(normalizedTarget) : relative;
            }
            catch
            {
                return Path.GetFileName(normalizedTarget) ?? normalizedTarget;
            }
        }

        private static Guid? TryParseGuid(string? raw)
        {
            if (Guid.TryParse(raw, out var g) && g != Guid.Empty) return g;
            return null;
        }

        private sealed class SharedParameterFileScope : IDisposable
        {
            private readonly Application _app;
            private readonly string _originalPath;

            public SharedParameterFileScope(Application app, string sharedParameterFile)
            {
                _app = app;
                _originalPath = app.SharedParametersFilename ?? string.Empty;
                _app.SharedParametersFilename = sharedParameterFile;
            }

            public ExternalDefinition? FindDefinition(string groupName, string definitionName, Guid? guid, out string? error)
            {
                error = null;
                DefinitionFile? file;
                try
                {
                    file = _app.OpenSharedParameterFile();
                }
                catch (Exception ex)
                {
                    error = "Shared parameter file could not be opened. " + ex.Message;
                    return null;
                }

                if (file == null)
                {
                    error = "Shared parameter file could not be opened.";
                    return null;
                }

                DefinitionGroup? group = null;
                try { group = file.Groups.get_Item(groupName); } catch { }
                if (group == null)
                {
                    error = $"Shared parameter group '{groupName}' was not found.";
                    return null;
                }

                if (guid.HasValue)
                {
                    foreach (Definition definition in group.Definitions)
                    {
                        if (definition is ExternalDefinition ext && ext.GUID == guid.Value)
                            return ext;
                    }
                }

                foreach (Definition definition in group.Definitions)
                {
                    if (definition is ExternalDefinition ext && string.Equals(ext.Name, definitionName, StringComparison.Ordinal))
                        return ext;
                }

                error = $"Shared parameter definition '{definitionName}' was not found in group '{groupName}'.";
                return null;
            }

            public void Dispose()
            {
                try { _app.SharedParametersFilename = _originalPath; } catch { }
            }
        }

        private sealed class JsonlAuditWriter : IDisposable
        {
            private readonly StreamWriter _writer;

            public JsonlAuditWriter()
            {
                var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Revit_MCP", "Logs", "family.batch_add_parameter_from_folder");
                Directory.CreateDirectory(root);
                LogPath = Path.Combine(root, $"family.batch_add_parameter_from_folder_{DateTime.Now:yyyyMMdd_HHmmssfff}.jsonl");
                _writer = new StreamWriter(new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false))
                {
                    AutoFlush = true,
                    NewLine = "\n"
                };
            }

            public string LogPath { get; }

            public void Write(object entry)
            {
                var json = JsonConvert.SerializeObject(entry, Formatting.None);
                _writer.WriteLine(json);
            }

            public void Dispose()
            {
                try { _writer.Flush(); } catch { }
                try { _writer.Dispose(); } catch { }
            }
        }
    }
}
