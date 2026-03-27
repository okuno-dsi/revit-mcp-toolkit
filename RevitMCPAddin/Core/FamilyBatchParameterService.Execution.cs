#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    internal sealed partial class FamilyBatchParameterService
    {
        private JObject ProcessFile(BatchRequest request, string filePath)
        {
            var sw = Stopwatch.StartNew();
            var messages = new List<string>();
            var parameterResults = new List<JObject>();
            var action = "unchanged";
            var ok = true;
            var saved = false;
            var savePath = filePath;
            string? backupPath = null;
            var addedCount = 0;
            var skippedCount = 0;
            var failedCount = 0;
            var hadChanges = false;
            Document? familyDoc = null;
            var createdTemporaryType = false;
            string? temporaryTypeName = null;

            try
            {
                if (IsDocumentAlreadyOpen(filePath))
                {
                    return FileResult(filePath, false, "failed", false, filePath, null, 0, 0, 1, sw.ElapsedMilliseconds,
                        new[] { "Target family is already open in this Revit session." },
                        new[] { ParameterResult(null, false, "failed", "Target family is already open in this Revit session.", false, null, false) });
                }

                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                {
                    return FileResult(filePath, false, "failed", false, filePath, null, 0, 0, 1, sw.ElapsedMilliseconds,
                        new[] { "Family file was not found." }, Array.Empty<JObject>());
                }

                if (fileInfo.IsReadOnly && request.SaveMode == SaveMode.Overwrite && !request.DryRun)
                {
                    return FileResult(filePath, false, "failed", false, filePath, null, 0, 0, 1, sw.ElapsedMilliseconds,
                        new[] { "Target file is read-only." }, Array.Empty<JObject>());
                }

                familyDoc = _app.OpenDocumentFile(filePath);
                if (familyDoc == null || !familyDoc.IsFamilyDocument)
                {
                    return FileResult(filePath, false, "failed", false, filePath, null, 0, 0, 1, sw.ElapsedMilliseconds,
                        new[] { "Target file is not a family document." }, Array.Empty<JObject>());
                }

                var familyManager = familyDoc.FamilyManager;
                if (familyManager == null)
                {
                    return FileResult(filePath, false, "failed", false, filePath, null, 0, 0, 1, sw.ElapsedMilliseconds,
                        new[] { "FamilyManager is not available for this family document." }, Array.Empty<JObject>());
                }

                try
                {
                    var ownerFamily = familyDoc.OwnerFamily;
                    var familyCategory = ownerFamily?.FamilyCategory;
                    var categoryName = familyCategory?.Name ?? "(null)";
                    bool? allowsBoundParameters = null;
                    try { allowsBoundParameters = familyCategory?.AllowsBoundParameters; } catch { }
                    int typeCount = 0;
                    try
                    {
                        foreach (FamilyType _ in familyManager.Types) typeCount++;
                    }
                    catch { }
                    messages.Add($"Family diagnostics: category={categoryName}, allowsBoundParameters={allowsBoundParameters?.ToString() ?? "(null)"}, hasCurrentType={(familyManager.CurrentType != null)}, typeCount={typeCount}");
                }
                catch { }

                foreach (var parameter in request.Parameters)
                {
                    var evaluation = EvaluateParameter(request, familyManager, parameter);
                    if (!evaluation.Ok && !evaluation.CanAttemptAdd)
                    {
                        failedCount++;
                        ok = false;
                        parameterResults.Add(ParameterResult(parameter.ParameterName, false, "failed", evaluation.Message, parameter.ParameterMode == ParameterMode.Shared, parameter.IsInstance, evaluation.Existing));
                        messages.Add(evaluation.Message);
                        if (!request.ContinueOnError) break;
                        continue;
                    }

                    if (!evaluation.CanAttemptAdd)
                    {
                        if (evaluation.Action == "failed")
                        {
                            failedCount++;
                            ok = false;
                        }
                        else
                        {
                            skippedCount++;
                        }

                        parameterResults.Add(ParameterResult(parameter.ParameterName, evaluation.Action != "failed", evaluation.Action, evaluation.Message, parameter.ParameterMode == ParameterMode.Shared, parameter.IsInstance, evaluation.Existing));
                        messages.Add(evaluation.Message);
                        if (evaluation.Action == "failed" && !request.ContinueOnError) break;
                        continue;
                    }

                    if (request.DryRun)
                    {
                        skippedCount++;
                        action = "dry_run";
                        parameterResults.Add(ParameterResult(parameter.ParameterName, true, "dry_run", evaluation.Message, parameter.ParameterMode == ParameterMode.Shared, parameter.IsInstance, false));
                        messages.Add(evaluation.Message);
                        continue;
                    }

                    using (var tx = new Transaction(familyDoc, $"Add Family Parameter {parameter.ParameterName}"))
                    {
                        tx.Start();
                        TxnUtil.ConfigureProceedWithWarnings(tx);
                        try
                        {
                            if (EnsureWritableFamilyType(familyManager, out var tempTypeCreatedNow, out var tempTypeNameNow))
                            {
                                if (tempTypeCreatedNow)
                                {
                                    createdTemporaryType = true;
                                    temporaryTypeName = tempTypeNameNow;
                                    messages.Add($"Temporary family type created for parameter add: {tempTypeNameNow}");
                                }
                            }
                            else
                            {
                                throw new InvalidOperationException("Could not create or activate a writable family type.");
                            }

                            if (parameter.ParameterMode == ParameterMode.Shared)
                            {
                                InvokeAddSharedParameterFromRequest(request, parameter, familyManager, evaluation.ResolvedGroupTypeId!);
                            }
                            else
                            {
                                if (evaluation.ResolvedSpecTypeId == null)
                                    throw new InvalidOperationException("Family parameter spec type could not be resolved.");
                                InvokeAddFamilyParameter(familyManager, parameter.ParameterName, evaluation.ResolvedGroupTypeId!, parameter.ParameterGroupRaw, evaluation.ResolvedSpecTypeId, parameter.FamilySpecTypeRaw, parameter.IsInstance);
                            }

                            tx.Commit();
                            hadChanges = true;
                            addedCount++;
                            action = "updated";
                            parameterResults.Add(ParameterResult(parameter.ParameterName, true, "added", evaluation.SuccessMessage ?? $"Parameter '{parameter.ParameterName}' added.", parameter.ParameterMode == ParameterMode.Shared, parameter.IsInstance, false));
                            messages.Add(evaluation.SuccessMessage ?? $"Parameter '{parameter.ParameterName}' added.");
                        }
                        catch (Exception ex)
                        {
                            try { tx.RollBack(); } catch { }
                            ok = false;
                            failedCount++;
                            var detail = GetInnermostMessage(ex);
                            parameterResults.Add(ParameterResult(parameter.ParameterName, false, "failed", detail, parameter.ParameterMode == ParameterMode.Shared, parameter.IsInstance, false));
                            messages.Add(detail);
                            if (!request.ContinueOnError) break;
                        }
                    }
                }

                if (!request.DryRun && hadChanges && createdTemporaryType && !string.IsNullOrWhiteSpace(temporaryTypeName))
                {
                    TryCleanupTemporaryFamilyType(familyDoc, familyManager, temporaryTypeName!, messages);
                }

                if (request.DryRun)
                    return FileResult(filePath, ok, "dry_run", false, filePath, null, addedCount, skippedCount, failedCount, sw.ElapsedMilliseconds, messages, parameterResults);

                if (!hadChanges)
                {
                    if (request.SaveMode == SaveMode.SaveAsCopy && !request.CloseWithoutSaveOnNoChange)
                    {
                        savePath = BuildMirroredTargetPath(request.OutputFolder!, request.FolderPath, filePath);
                        if (PathsEqual(savePath, filePath))
                            throw new InvalidOperationException("save_as_copy target path resolves to the original file path.");

                        Directory.CreateDirectory(Path.GetDirectoryName(savePath) ?? request.OutputFolder!);
                        SaveFamilyAsCopy(familyDoc, savePath);
                        saved = true;
                        messages.Add("No parameter changes were required; family was still saved as copy.");
                    }
                    else
                    {
                        action = failedCount > 0 ? "failed" : "unchanged";
                    }

                    return FileResult(filePath, ok, action, saved, savePath, backupPath, addedCount, skippedCount, failedCount, sw.ElapsedMilliseconds, messages, parameterResults);
                }

                if (request.SaveMode == SaveMode.Overwrite)
                {
                    if (request.CreateBackup)
                    {
                        backupPath = BuildMirroredTargetPath(request.BackupFolder, request.FolderPath, filePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(backupPath) ?? request.BackupFolder);
                        try
                        {
                            File.Copy(filePath, backupPath, true);
                            messages.Add($"Backup created: {backupPath}");
                        }
                        catch (Exception ex)
                        {
                            ok = false;
                            failedCount++;
                            messages.Add("Backup creation failed. File was not saved.");
                            parameterResults.Add(ParameterResult(null, false, "failed", $"Backup creation failed: {ex.Message}", false, null, null));
                            return FileResult(filePath, false, "failed", false, filePath, backupPath, addedCount, skippedCount, failedCount, sw.ElapsedMilliseconds, messages, parameterResults);
                        }
                    }

                    try
                    {
                        familyDoc.Save();
                        saved = true;
                    }
                    catch (Exception ex)
                    {
                        ok = false;
                        failedCount++;
                        messages.Add($"The family document could not be saved. Unsaved changes will be discarded. {ex.Message}");
                        parameterResults.Add(ParameterResult(null, false, "failed", $"Save failed: {ex.Message}", false, null, null));
                        return FileResult(filePath, false, "failed", false, filePath, backupPath, addedCount, skippedCount, failedCount, sw.ElapsedMilliseconds, messages, parameterResults);
                    }
                }
                else
                {
                    savePath = BuildMirroredTargetPath(request.OutputFolder!, request.FolderPath, filePath);
                    if (PathsEqual(savePath, filePath))
                    {
                        ok = false;
                        failedCount++;
                        messages.Add("save_as_copy target path resolves to the original file path.");
                        parameterResults.Add(ParameterResult(null, false, "failed", "save_as_copy target path resolves to the original file path.", false, null, null));
                        return FileResult(filePath, false, "failed", false, savePath, backupPath, addedCount, skippedCount, failedCount, sw.ElapsedMilliseconds, messages, parameterResults);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(savePath) ?? request.OutputFolder!);
                    try
                    {
                        SaveFamilyAsCopy(familyDoc, savePath);
                        saved = true;
                    }
                    catch (Exception ex)
                    {
                        ok = false;
                        failedCount++;
                        messages.Add($"The family document could not be saved as copy. Unsaved changes will be discarded. {ex.Message}");
                        parameterResults.Add(ParameterResult(null, false, "failed", $"SaveAs failed: {ex.Message}", false, null, null));
                        return FileResult(filePath, false, "failed", false, savePath, backupPath, addedCount, skippedCount, failedCount, sw.ElapsedMilliseconds, messages, parameterResults);
                    }
                }

                return FileResult(filePath, ok, ok ? "updated" : "failed", saved, savePath, backupPath, addedCount, skippedCount, failedCount, sw.ElapsedMilliseconds, messages, parameterResults);
            }
            catch (Exception ex)
            {
                return FileResult(filePath, false, "failed", false, savePath, backupPath, addedCount, skippedCount, failedCount + 1, sw.ElapsedMilliseconds, new[] { ex.Message }, parameterResults);
            }
            finally
            {
                try { familyDoc?.Close(false); } catch { }
            }
        }

        private EvaluationResult EvaluateParameter(BatchRequest request, FamilyManager familyManager, ParameterSpec parameter)
        {
            var groupTypeId = ResolveGroupTypeIdOrDefault(parameter.ParameterGroupRaw);
            if (groupTypeId == null)
                return EvaluationResult.Fail($"Parameter group '{parameter.ParameterGroupRaw}' is not supported.");

            if (parameter.ParameterMode == ParameterMode.Family)
            {
                var specTypeId = ResolveFamilySpecTypeOrNull(parameter.FamilySpecTypeRaw);
                if (specTypeId == null)
                    return EvaluationResult.Fail($"familySpecType '{parameter.FamilySpecTypeRaw}' is not supported in V1.");

                var existingByName = FindExistingByName(familyManager, parameter.ParameterName);
                if (existingByName != null)
                {
                    if (IsShared(existingByName))
                        return EvaluationResult.Fail($"Parameter '{parameter.ParameterName}' already exists as a shared parameter.", true);
                    if (existingByName.IsInstance != parameter.IsInstance)
                        return EvaluationResult.Fail($"Parameter '{parameter.ParameterName}' already exists, but instance/type does not match.", true);
                    if (!ForgeTypeIdEquals(GetParameterDataType(existingByName), specTypeId))
                        return EvaluationResult.Fail($"Parameter '{parameter.ParameterName}' already exists, but familySpecType does not match.", true);
                    if (!ForgeTypeIdEquals(GetParameterGroup(existingByName), groupTypeId))
                        return EvaluationResult.Fail($"Parameter '{parameter.ParameterName}' already exists, but parameter group does not match.", true);
                    if (parameter.OnExists == OnExistsMode.Error)
                        return EvaluationResult.Fail($"Parameter '{parameter.ParameterName}' already exists and matches the request.", true);
                    return EvaluationResult.Skip($"Parameter '{parameter.ParameterName}' already exists and matches the request.", true);
                }

                return EvaluationResult.Add(groupTypeId, specTypeId, null,
                    request.DryRun ? $"Family parameter '{parameter.ParameterName}' would be added." : $"Family parameter '{parameter.ParameterName}' added.");
            }

            var sharedFilePath = !string.IsNullOrWhiteSpace(parameter.SharedParameterFile) ? parameter.SharedParameterFile : request.DefaultSharedParameterFile;
            if (string.IsNullOrWhiteSpace(sharedFilePath) || !File.Exists(sharedFilePath))
                return EvaluationResult.Fail("Shared parameter file could not be opened.");

            var definitionGroupName = !string.IsNullOrWhiteSpace(parameter.SharedParameterGroupName) ? parameter.SharedParameterGroupName : request.DefaultSharedParameterGroupName;
            if (string.IsNullOrWhiteSpace(definitionGroupName))
                definitionGroupName = "Common";

            var definitionName = !string.IsNullOrWhiteSpace(parameter.SharedParameterDefinitionName) ? parameter.SharedParameterDefinitionName : parameter.ParameterName;

            ExternalDefinition? externalDefinition;
            string? sharedResolveError;
            using (var scope = new SharedParameterFileScope(_app, sharedFilePath))
            {
                externalDefinition = scope.FindDefinition(definitionGroupName, definitionName, parameter.SharedParameterGuid, out sharedResolveError);
            }

            if (externalDefinition == null)
                return EvaluationResult.Fail(sharedResolveError ?? $"Shared parameter definition '{definitionName}' was not found in group '{definitionGroupName}'.");
            if (!string.Equals(externalDefinition.Name, parameter.ParameterName, StringComparison.Ordinal))
                return EvaluationResult.Fail($"Shared parameter definition name '{externalDefinition.Name}' does not match requested parameterName '{parameter.ParameterName}'.");

            var targetGuid = externalDefinition.GUID;
            var exactByGuid = FindExistingSharedByGuid(familyManager, targetGuid);
            if (exactByGuid != null)
            {
                if (exactByGuid.IsInstance != parameter.IsInstance)
                    return EvaluationResult.Fail($"Parameter '{parameter.ParameterName}' already exists, but instance/type does not match.", true);
                if (!ForgeTypeIdEquals(GetParameterGroup(exactByGuid), groupTypeId))
                    return EvaluationResult.Fail($"Parameter '{parameter.ParameterName}' already exists, but parameter group does not match.", true);
                if (!string.Equals(GetParameterName(exactByGuid), parameter.ParameterName, StringComparison.Ordinal))
                    return EvaluationResult.Fail($"Shared parameter GUID matches, but parameter name differs ('{GetParameterName(exactByGuid)}').", true);
                if (parameter.OnExists == OnExistsMode.Error)
                    return EvaluationResult.Fail($"Parameter '{parameter.ParameterName}' already exists and matches the request.", true);
                return EvaluationResult.Skip($"Parameter '{parameter.ParameterName}' already exists and matches the request.", true);
            }

            var existingSharedByName = FindExistingByName(familyManager, parameter.ParameterName);
            if (existingSharedByName != null)
            {
                if (!IsShared(existingSharedByName))
                    return EvaluationResult.Fail($"Parameter '{parameter.ParameterName}' already exists as a non-shared family parameter.", true);
                var existingGuid = TryGetSharedGuid(existingSharedByName);
                if (existingGuid.HasValue && existingGuid.Value != targetGuid)
                    return EvaluationResult.Fail($"Parameter '{parameter.ParameterName}' already exists, but shared GUID does not match.", true);
                if (existingSharedByName.IsInstance != parameter.IsInstance)
                    return EvaluationResult.Fail($"Parameter '{parameter.ParameterName}' already exists, but instance/type does not match.", true);
                if (!ForgeTypeIdEquals(GetParameterGroup(existingSharedByName), groupTypeId))
                    return EvaluationResult.Fail($"Parameter '{parameter.ParameterName}' already exists, but parameter group does not match.", true);
                if (parameter.OnExists == OnExistsMode.Error)
                    return EvaluationResult.Fail($"Parameter '{parameter.ParameterName}' already exists and matches the request.", true);
                return EvaluationResult.Skip($"Parameter '{parameter.ParameterName}' already exists and matches the request.", true);
            }

            return EvaluationResult.Add(groupTypeId, null, externalDefinition,
                request.DryRun ? $"Shared instance/type parameter '{parameter.ParameterName}' would be added." : $"Shared instance/type parameter '{parameter.ParameterName}' added.");
        }
    }
}
