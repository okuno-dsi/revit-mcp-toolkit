#nullable enable
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace RevitMCPAddin.Core.Ledger
{
    /// <summary>
    /// Provides a stable per-project DocKey using the existing MCP Ledger DataStorage (ProjectToken).
    /// This is used to prevent cross-project confusion when loading external workspace snapshots.
    /// </summary>
    internal static class LedgerDocKeyProvider
    {
        // IMPORTANT: must match McpLedgerEngine schema GUID (do not change).
        private static readonly Guid LedgerSchemaGuid = new Guid("cffdf490-8b12-423f-8478-2a73147b6512");
        private const string LedgerSchemaName = "McpLedger";

        // Field names must match McpLedgerEngine schema fields.
        private const string F_ProjectToken = "ProjectToken";
        private const string F_Sequence = "Sequence";
        private const string F_LastCommandId = "LastCommandId";
        private const string F_LastCommandName = "LastCommandName";
        private const string F_LastCommandExecutedUtc = "LastCommandExecutedUtc";
        private const string F_LastCommandResult = "LastCommandResult";
        private const string F_ActiveSessionId = "ActiveSessionId";
        private const string F_ActiveSessionMode = "ActiveSessionMode";
        private const string F_ActiveSessionStartedUtc = "ActiveSessionStartedUtc";
        private const string F_CommandLogJson = "CommandLogJson";

        private static Schema? GetOrCreateLedgerSchema()
        {
            try
            {
                var s = Schema.Lookup(LedgerSchemaGuid);
                if (s != null) return s;

                var sb = new SchemaBuilder(LedgerSchemaGuid);
                sb.SetSchemaName(LedgerSchemaName);
                sb.SetDocumentation("MCP Ledger (Project authenticity + command continuity).");
                sb.SetReadAccessLevel(AccessLevel.Public);
                sb.SetWriteAccessLevel(AccessLevel.Public);

                sb.AddSimpleField(F_ProjectToken, typeof(string));
                sb.AddSimpleField(F_Sequence, typeof(int));
                sb.AddSimpleField(F_LastCommandId, typeof(string));
                sb.AddSimpleField(F_LastCommandName, typeof(string));
                sb.AddSimpleField(F_LastCommandExecutedUtc, typeof(string));
                sb.AddSimpleField(F_LastCommandResult, typeof(string));
                sb.AddSimpleField(F_ActiveSessionId, typeof(string));
                sb.AddSimpleField(F_ActiveSessionMode, typeof(string));
                sb.AddSimpleField(F_ActiveSessionStartedUtc, typeof(string));
                sb.AddSimpleField(F_CommandLogJson, typeof(string));

                return sb.Finish();
            }
            catch
            {
                return null;
            }
        }

        private static bool TryFindExistingLedger(Document doc, Schema schema, out DataStorage? storage, out Entity entity, out string? error)
        {
            storage = null;
            entity = default(Entity);
            error = null;

            try
            {
                var matches = new List<DataStorage>();
                var all = new FilteredElementCollector(doc).OfClass(typeof(DataStorage)).ToElements();
                foreach (var e in all)
                {
                    var ds = e as DataStorage;
                    if (ds == null) continue;
                    try
                    {
                        var ent = ds.GetEntity(schema);
                        if (ent.IsValid()) matches.Add(ds);
                    }
                    catch { /* ignore */ }
                }

                if (matches.Count > 1)
                {
                    error = "Multiple MCP Ledger DataStorage elements found. Aborting for safety.";
                    return false;
                }

                if (matches.Count == 0)
                    return true;

                storage = matches[0];
                entity = storage.GetEntity(schema);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryCreateLedger(Document doc, Schema schema, out DataStorage? storage, out Entity entity, out string? error)
        {
            storage = null;
            entity = default(Entity);
            error = null;

            bool isReadOnly = false;
            try { isReadOnly = doc.IsReadOnly; } catch { isReadOnly = false; }
            if (isReadOnly)
            {
                error = "Document is read-only; cannot create MCP Ledger DataStorage.";
                return false;
            }

            try
            {
                using (var t = new Transaction(doc, "Create MCP Ledger"))
                {
                    t.Start();
                    try { TxnUtil.ConfigureProceedWithWarnings(t); } catch { }

                    storage = DataStorage.Create(doc);
                    var entNew = new Entity(schema);

                    SafeSetString(ref entNew, schema, F_ProjectToken, Guid.NewGuid().ToString());
                    SafeSetInt(ref entNew, schema, F_Sequence, 1);
                    SafeSetString(ref entNew, schema, F_LastCommandId, "");
                    SafeSetString(ref entNew, schema, F_LastCommandName, "");
                    SafeSetString(ref entNew, schema, F_LastCommandExecutedUtc, "");
                    SafeSetString(ref entNew, schema, F_LastCommandResult, "");
                    SafeSetString(ref entNew, schema, F_ActiveSessionId, "");
                    SafeSetString(ref entNew, schema, F_ActiveSessionMode, "");
                    SafeSetString(ref entNew, schema, F_ActiveSessionStartedUtc, "");
                    SafeSetString(ref entNew, schema, F_CommandLogJson, "[]");

                    storage.SetEntity(entNew);
                    t.Commit();

                    entity = entNew;
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void SafeSetString(ref Entity ent, Schema schema, string fieldName, string value)
        {
            try
            {
                var f = schema.GetField(fieldName);
                if (f == null) return;
                ent.Set<string>(f, value ?? "");
            }
            catch { }
        }

        private static void SafeSetInt(ref Entity ent, Schema schema, string fieldName, int value)
        {
            try
            {
                var f = schema.GetField(fieldName);
                if (f == null) return;
                ent.Set<int>(f, value);
            }
            catch { }
        }

        private static string SafeGetString(Entity ent, Schema schema, string fieldName)
        {
            try
            {
                var f = schema.GetField(fieldName);
                if (f == null) return "";
                return ent.Get<string>(f) ?? "";
            }
            catch { return ""; }
        }

        public static bool TryGetDocKey(Document doc, out string docKey, out int? dataStorageId, out string? error)
        {
            return TryGetOrCreateDocKey(doc, createIfMissing: false, out docKey, out dataStorageId, out error);
        }

        public static bool TryGetOrCreateDocKey(Document doc, bool createIfMissing, out string docKey, out int? dataStorageId, out string? error)
        {
            docKey = string.Empty;
            dataStorageId = null;
            error = null;

            if (doc == null) { error = "doc is null"; return false; }

            var schema = GetOrCreateLedgerSchema();
            if (schema == null)
            {
                error = "Ledger schema lookup/create failed.";
                return false;
            }

            if (!TryFindExistingLedger(doc, schema, out var storage, out var entity, out error))
                return false;

            if (storage == null || !entity.IsValid())
            {
                if (!createIfMissing)
                {
                    error = "Ledger not found (createIfMissing=false).";
                    return false;
                }

                if (!TryCreateLedger(doc, schema, out storage, out entity, out error))
                    return false;
            }

            if (storage == null || !entity.IsValid())
            {
                error = "Ledger storage/entity is null.";
                return false;
            }

            dataStorageId = storage.Id.IntValue();
            docKey = SafeGetString(entity, schema, F_ProjectToken);
            if (string.IsNullOrWhiteSpace(docKey))
            {
                error = "Ledger ProjectToken is empty.";
                return false;
            }

            docKey = docKey.Trim();
            return true;
        }
    }
}


