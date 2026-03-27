#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
        private readonly Application _app;

        public FamilyBatchParameterService(Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
        }

        public JObject Execute(JObject payload)
        {
            var parseError = ParseRequest(payload, out var request);
            if (parseError != null) return parseError;
            if (request == null) return RpcResultEnvelope.Fail("INVALID_PARAMS", "Request could not be parsed.");

            var files = EnumerateFamilyFiles(request).ToList();
            if (files.Count == 0)
            {
                return RpcResultEnvelope.Fail(
                    "NO_FAMILY_FILES",
                    $"No .rfa files were found under '{request.FolderPath}' using searchPattern '{request.SearchPattern}'.");
            }

            using (var audit = new JsonlAuditWriter())
            {
                audit.Write(new
                {
                    eventType = "started",
                    method = "family.batch_add_parameter_from_folder",
                    timestamp = DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
                    folderPath = request.FolderPath,
                    searchPattern = request.SearchPattern,
                    recursive = request.Recursive,
                    dryRun = request.DryRun,
                    saveMode = request.SaveMode.ToString(),
                    fileCount = files.Count,
                    parameterCount = request.Parameters.Count
                });

                var items = new JArray();
                var processed = 0;
                var succeeded = 0;
                var failed = 0;
                var skipped = 0;

                foreach (var filePath in files)
                {
                    processed++;
                    var item = ProcessFile(request, filePath);
                    items.Add(item);
                    audit.Write(new
                    {
                        eventType = "file_result",
                        timestamp = DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
                        item
                    });

                    var ok = item.Value<bool?>("ok") ?? false;
                    var action = (item.Value<string>("action") ?? string.Empty).Trim();
                    if (ok && (action.Equals("updated", StringComparison.OrdinalIgnoreCase) || action.Equals("unchanged", StringComparison.OrdinalIgnoreCase) || action.Equals("dry_run", StringComparison.OrdinalIgnoreCase)))
                        succeeded++;
                    else if (action.Equals("skipped", StringComparison.OrdinalIgnoreCase))
                        skipped++;
                    else if (!ok)
                        failed++;

                    if (!ok && !request.ContinueOnError)
                        break;
                }

                var result = new JObject
                {
                    ["ok"] = failed == 0,
                    ["method"] = "family.batch_add_parameter_from_folder",
                    ["folderPath"] = request.FolderPath,
                    ["processed"] = processed,
                    ["succeeded"] = succeeded,
                    ["failed"] = failed,
                    ["skipped"] = skipped,
                    ["dryRun"] = request.DryRun,
                    ["logPath"] = audit.LogPath,
                    ["items"] = items
                };

                audit.Write(new
                {
                    eventType = "completed",
                    timestamp = DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
                    ok = failed == 0,
                    processed,
                    succeeded,
                    failed,
                    skipped,
                    logPath = audit.LogPath
                });

                return result;
            }
        }

        private static JObject? ParseRequest(JObject payload, out BatchRequest? request)
        {
            request = null;

            var folderPath = NormalizePath(payload.Value<string>("folderPath"));
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return RpcResultEnvelope.Fail("INVALID_FOLDER", "folderPath is required and must exist.");

            var saveModeRaw = (payload.Value<string>("saveMode") ?? "overwrite").Trim().ToLowerInvariant();
            SaveMode saveMode;
            if (saveModeRaw == "overwrite") saveMode = SaveMode.Overwrite;
            else if (saveModeRaw == "save_as_copy") saveMode = SaveMode.SaveAsCopy;
            else return RpcResultEnvelope.Fail("INVALID_SAVE_MODE", "saveMode must be 'overwrite' or 'save_as_copy'.");

            var outputFolder = NormalizePath(payload.Value<string>("outputFolder"));
            if (saveMode == SaveMode.SaveAsCopy)
            {
                if (string.IsNullOrWhiteSpace(outputFolder))
                    return RpcResultEnvelope.Fail("OUTPUT_FOLDER_REQUIRED", "outputFolder is required when saveMode=save_as_copy.");
                Directory.CreateDirectory(outputFolder);
            }

            var backupFolder = NormalizePath(payload.Value<string>("backupFolder"));
            if (string.IsNullOrWhiteSpace(backupFolder))
                backupFolder = Path.Combine(folderPath, "_backup");

            var parameters = ParseParameters(payload, out var paramError);
            if (paramError != null) return paramError;
            if (parameters.Count == 0)
                return RpcResultEnvelope.Fail("INVALID_PARAMS", "parameters[] must contain at least one parameter definition.");

            request = new BatchRequest
            {
                FolderPath = folderPath,
                SearchPattern = string.IsNullOrWhiteSpace(payload.Value<string>("searchPattern")) ? "*.rfa" : payload.Value<string>("searchPattern")!.Trim(),
                Recursive = payload.Value<bool?>("recursive") ?? false,
                DryRun = payload.Value<bool?>("dryRun") ?? false,
                ContinueOnError = payload.Value<bool?>("continueOnError") ?? true,
                SaveMode = saveMode,
                OutputFolder = outputFolder,
                CreateBackup = payload.Value<bool?>("createBackup") ?? true,
                BackupFolder = backupFolder,
                CloseWithoutSaveOnNoChange = payload.Value<bool?>("closeWithoutSaveOnNoChange") ?? true,
                DefaultSharedParameterFile = NormalizePath(payload.Value<string>("defaultSharedParameterFile")),
                DefaultSharedParameterGroupName = (payload.Value<string>("defaultSharedParameterGroupName") ?? "Common").Trim(),
                Parameters = parameters
            };

            return null;
        }

        private static List<ParameterSpec> ParseParameters(JObject payload, out JObject? error)
        {
            error = null;
            var list = new List<ParameterSpec>();
            if (!(payload["parameters"] is JArray arr))
                return list;

            int index = 0;
            foreach (var token in arr)
            {
                index++;
                if (!(token is JObject obj))
                {
                    error = RpcResultEnvelope.Fail("INVALID_PARAMETER_ITEM", $"parameters[{index - 1}] must be an object.");
                    return list;
                }

                var modeRaw = (obj.Value<string>("parameterMode") ?? string.Empty).Trim().ToLowerInvariant();
                ParameterMode mode;
                if (modeRaw == "shared") mode = ParameterMode.Shared;
                else if (modeRaw == "family") mode = ParameterMode.Family;
                else
                {
                    error = RpcResultEnvelope.Fail("INVALID_PARAMETER_MODE", $"parameters[{index - 1}].parameterMode must be 'shared' or 'family'.");
                    return list;
                }

                var parameterName = (obj.Value<string>("parameterName") ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(parameterName))
                {
                    error = RpcResultEnvelope.Fail("INVALID_PARAMETER_NAME", $"parameters[{index - 1}].parameterName is required.");
                    return list;
                }

                var onExistsRaw = (obj.Value<string>("onExists") ?? "skip").Trim().ToLowerInvariant();
                OnExistsMode onExists;
                if (onExistsRaw == "skip") onExists = OnExistsMode.Skip;
                else if (onExistsRaw == "error") onExists = OnExistsMode.Error;
                else
                {
                    error = RpcResultEnvelope.Fail("INVALID_ON_EXISTS", $"parameters[{index - 1}].onExists must be 'skip' or 'error'.");
                    return list;
                }

                var spec = new ParameterSpec
                {
                    ParameterMode = mode,
                    ParameterName = parameterName,
                    ParameterGroupRaw = (obj.Value<string>("parameterGroup") ?? "PG_DATA").Trim(),
                    IsInstance = obj.Value<bool?>("isInstance") ?? true,
                    OnExists = onExists,
                    SharedParameterFile = NormalizePath(obj.Value<string>("sharedParameterFile") ?? obj.Value<string>("sharedParametersFile")),
                    SharedParameterGroupName = (obj.Value<string>("sharedParameterGroupName") ?? string.Empty).Trim(),
                    SharedParameterDefinitionName = (obj.Value<string>("sharedParameterDefinitionName") ?? string.Empty).Trim(),
                    FamilySpecTypeRaw = (obj.Value<string>("familySpecType") ?? string.Empty).Trim(),
                    SharedParameterGuid = TryParseGuid(
                        obj.Value<string>("sharedParameterGuid")
                        ?? obj.Value<string>("guid")
                        ?? obj.Value<string>("sharedGuid"))
                };

                if (mode == ParameterMode.Family && string.IsNullOrWhiteSpace(spec.FamilySpecTypeRaw))
                {
                    error = RpcResultEnvelope.Fail("MISSING_FAMILY_SPEC_TYPE", $"parameters[{index - 1}].familySpecType is required when parameterMode=family.");
                    return list;
                }

                list.Add(spec);
            }

            return list;
        }

        private IEnumerable<string> EnumerateFamilyFiles(BatchRequest request)
        {
            var option = request.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(request.FolderPath, request.SearchPattern, option);
            }
            catch
            {
                files = Array.Empty<string>();
            }

            return files
                .Where(x => string.Equals(Path.GetExtension(x), ".rfa", StringComparison.OrdinalIgnoreCase))
                .Select(NormalizePath)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        }
    }
}
