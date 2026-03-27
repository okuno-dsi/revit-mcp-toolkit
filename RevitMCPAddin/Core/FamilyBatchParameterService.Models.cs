#nullable enable
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Core
{
    internal sealed partial class FamilyBatchParameterService
    {
        private sealed class BatchRequest
        {
            public string FolderPath { get; set; } = string.Empty;
            public string SearchPattern { get; set; } = "*.rfa";
            public bool Recursive { get; set; }
            public bool DryRun { get; set; }
            public bool ContinueOnError { get; set; }
            public SaveMode SaveMode { get; set; }
            public string OutputFolder { get; set; } = string.Empty;
            public bool CreateBackup { get; set; }
            public string BackupFolder { get; set; } = string.Empty;
            public bool CloseWithoutSaveOnNoChange { get; set; }
            public string DefaultSharedParameterFile { get; set; } = string.Empty;
            public string DefaultSharedParameterGroupName { get; set; } = "Common";
            public System.Collections.Generic.List<ParameterSpec> Parameters { get; set; } = new System.Collections.Generic.List<ParameterSpec>();
        }

        private sealed class ParameterSpec
        {
            public ParameterMode ParameterMode { get; set; }
            public string ParameterName { get; set; } = string.Empty;
            public string ParameterGroupRaw { get; set; } = "PG_DATA";
            public bool IsInstance { get; set; }
            public OnExistsMode OnExists { get; set; }
            public string SharedParameterFile { get; set; } = string.Empty;
            public string SharedParameterGroupName { get; set; } = string.Empty;
            public string SharedParameterDefinitionName { get; set; } = string.Empty;
            public System.Guid? SharedParameterGuid { get; set; }
            public string FamilySpecTypeRaw { get; set; } = string.Empty;
        }

        private sealed class EvaluationResult
        {
            public bool Ok { get; private set; }
            public bool Existing { get; private set; }
            public bool CanAttemptAdd { get; private set; }
            public string Action { get; private set; } = "failed";
            public string Message { get; private set; } = string.Empty;
            public string? SuccessMessage { get; private set; }
            public ForgeTypeId? ResolvedGroupTypeId { get; private set; }
            public ForgeTypeId? ResolvedSpecTypeId { get; private set; }
            public ExternalDefinition? ExternalDefinition { get; private set; }

            public static EvaluationResult Fail(string message, bool existing = false)
            {
                return new EvaluationResult { Ok = false, Existing = existing, CanAttemptAdd = false, Action = "failed", Message = message };
            }

            public static EvaluationResult Skip(string message, bool existing = false)
            {
                return new EvaluationResult { Ok = true, Existing = existing, CanAttemptAdd = false, Action = "skipped", Message = message };
            }

            public static EvaluationResult Add(ForgeTypeId groupTypeId, ForgeTypeId? specTypeId, ExternalDefinition? externalDefinition, string successMessage)
            {
                return new EvaluationResult
                {
                    Ok = true,
                    Existing = false,
                    CanAttemptAdd = true,
                    Action = "added",
                    Message = successMessage,
                    SuccessMessage = successMessage,
                    ResolvedGroupTypeId = groupTypeId,
                    ResolvedSpecTypeId = specTypeId,
                    ExternalDefinition = externalDefinition
                };
            }
        }

        private enum SaveMode
        {
            Overwrite,
            SaveAsCopy
        }

        private enum ParameterMode
        {
            Shared,
            Family
        }

        private enum OnExistsMode
        {
            Skip,
            Error
        }
    }
}
