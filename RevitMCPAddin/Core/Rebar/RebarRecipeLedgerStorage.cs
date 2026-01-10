#nullable enable
// ================================================================
// File   : Core/Rebar/RebarRecipeLedgerStorage.cs
// Target : .NET Framework 4.8 / C# 8.0
// Purpose: Persist per-host rebar recipe signatures in RVT via
//          DataStorage + ExtensibleStorage (document-local).
// Notes  :
//  - Read commands MUST NOT create DataStorage (read-only safety).
//  - Write commands may create the DataStorage if missing (in a TX).
//  - Ledger is stored as a single JSON string field for easy evolution.
// ================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Newtonsoft.Json;

namespace RevitMCPAddin.Core.Rebar
{
    internal sealed class RebarRecipeLedgerDocument
    {
        public int schemaVersion { get; set; } = 1;
        public Dictionary<string, RebarRecipeLedgerHostRecord> hosts { get; set; } =
            new Dictionary<string, RebarRecipeLedgerHostRecord>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class RebarRecipeLedgerHostRecord
    {
        public int schemaVersion { get; set; } = 1;
        public string engineVersion { get; set; } = string.Empty;
        public string hostUniqueId { get; set; } = string.Empty;
        public int hostElementId { get; set; }
        public string hostCategoryBic { get; set; } = string.Empty;
        public string profile { get; set; } = string.Empty;
        public string signatureSha256 { get; set; } = string.Empty;
        public string createdUtc { get; set; } = string.Empty;
        public object? summary { get; set; } = null;          // small summary DTO/anonymous object
        public object? recipeSnapshot { get; set; } = null;    // optional recipe JSON (for debug)
    }

    internal static class RebarRecipeLedgerStorage
    {
        // IMPORTANT: Do not change once deployed.
        private static readonly Guid SchemaGuid = new Guid("b52d55a4-0b55-4d8b-9cd2-8f0f3b0fa3d6");
        // NOTE: ExtensibleStorage Schema name must be a valid identifier (no dots, etc.).
        // GUID is the true identity; changing the name here does not break existing documents
        // because Schema.Lookup uses SchemaGuid.
        private const string SchemaName = "RevitMcp_RebarRecipeLedger";
        private const string FieldLedgerJson = "LedgerJson";

        public static string SchemaGuidString => SchemaGuid.ToString();

        private static Schema? GetOrCreateSchema(out string error)
        {
            error = string.Empty;
            try
            {
                var s = Schema.Lookup(SchemaGuid);
                if (s != null) return s;

                var sb = new SchemaBuilder(SchemaGuid);
                sb.SetSchemaName(SchemaName);
                sb.SetDocumentation("RevitMcp rebar recipe ledger (per host signature + last run info).");
                sb.SetReadAccessLevel(AccessLevel.Public);
                sb.SetWriteAccessLevel(AccessLevel.Public);

                sb.AddSimpleField(FieldLedgerJson, typeof(string));
                return sb.Finish();
            }
            catch (Exception ex1)
            {
                // Schema name conflicts can occur when a previous build used the same SchemaName with a different GUID.
                // In that case, create the schema with a unique name but keep the GUID stable.
                try
                {
                    var uniqueName = SchemaName + "_" + SchemaGuid.ToString("N").Substring(0, 8);
                    var sb2 = new SchemaBuilder(SchemaGuid);
                    sb2.SetSchemaName(uniqueName);
                    sb2.SetDocumentation("RevitMcp rebar recipe ledger (per host signature + last run info).");
                    sb2.SetReadAccessLevel(AccessLevel.Public);
                    sb2.SetWriteAccessLevel(AccessLevel.Public);

                    sb2.AddSimpleField(FieldLedgerJson, typeof(string));
                    return sb2.Finish();
                }
                catch (Exception ex2)
                {
                    error = "Schema lookup/create failed: " + ex1.Message + " / fallback: " + ex2.Message;
                    return null;
                }
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
                    error = "Multiple RebarRecipeLedger DataStorage elements found. Aborting for safety.";
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
                error = "Document is read-only; cannot create RebarRecipeLedger DataStorage.";
                return false;
            }

            try
            {
                using (var t = new Transaction(doc, "Create RebarRecipeLedger"))
                {
                    t.Start();
                    try { TxnUtil.ConfigureProceedWithWarnings(t); } catch { }

                    storage = DataStorage.Create(doc);

                    var empty = new RebarRecipeLedgerDocument();
                    var json = JsonConvert.SerializeObject(empty, Formatting.None);

                    var entNew = new Entity(schema);
                    var f = schema.GetField(FieldLedgerJson);
                    entNew.Set<string>(f, json);

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

        public static bool TryReadAll(Document doc, out bool hasLedger, out int? dataStorageId, out RebarRecipeLedgerDocument model, out string error)
        {
            hasLedger = false;
            dataStorageId = null;
            model = new RebarRecipeLedgerDocument();
            error = string.Empty;

            if (doc == null) { error = "doc is null"; return false; }

            var schema = GetOrCreateSchema(out var schemaErr);
            if (schema == null)
            {
                error = string.IsNullOrWhiteSpace(schemaErr) ? "Schema lookup/create failed." : schemaErr;
                return false;
            }

            if (!TryFindExistingLedger(doc, schema, out var storage, out var entity, out var findErr))
            {
                error = findErr ?? "Ledger lookup failed.";
                return false;
            }

            if (storage == null || !entity.IsValid())
            {
                hasLedger = false;
                return true;
            }

            hasLedger = true;
            try { dataStorageId = storage.Id.IntValue(); } catch { dataStorageId = null; }

            try
            {
                var f = schema.GetField(FieldLedgerJson);
                var json = entity.Get<string>(f) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(json))
                {
                    model = new RebarRecipeLedgerDocument();
                    return true;
                }

                model = JsonConvert.DeserializeObject<RebarRecipeLedgerDocument>(json) ?? new RebarRecipeLedgerDocument();
                if (model.hosts == null)
                    model.hosts = new Dictionary<string, RebarRecipeLedgerHostRecord>(StringComparer.OrdinalIgnoreCase);
                return true;
            }
            catch
            {
                // Corrupted ledger JSON: treat as empty (safe reset on next write).
                model = new RebarRecipeLedgerDocument();
                return true;
            }
        }

        public static bool TryEnsureLedger(Document doc, out DataStorage storage, out RebarRecipeLedgerDocument model, out string error)
        {
            storage = null!;
            model = new RebarRecipeLedgerDocument();
            error = string.Empty;

            if (doc == null) { error = "doc is null"; return false; }

            var schema = GetOrCreateSchema(out var schemaErr);
            if (schema == null)
            {
                error = string.IsNullOrWhiteSpace(schemaErr) ? "Schema lookup/create failed." : schemaErr;
                return false;
            }

            if (!TryFindExistingLedger(doc, schema, out var existing, out var entity, out var findErr))
            {
                error = findErr ?? "Ledger lookup failed.";
                return false;
            }

            if (existing == null || !entity.IsValid())
            {
                if (!TryCreateLedger(doc, schema, out existing, out entity, out var createErr))
                {
                    error = createErr ?? "Ledger create failed.";
                    return false;
                }
            }

            storage = existing!;

            // Read after ensure (should succeed)
            if (!TryReadAll(doc, out var hasLedger, out var _, out model, out var readErr))
            {
                error = readErr;
                return false;
            }

            if (!hasLedger)
            {
                model = new RebarRecipeLedgerDocument();
            }

            return true;
        }

        public static bool TryWriteAll(Document doc, DataStorage storage, RebarRecipeLedgerDocument model, out string error)
        {
            error = string.Empty;
            if (doc == null) { error = "doc is null"; return false; }
            if (storage == null) { error = "storage is null"; return false; }
            if (model == null) model = new RebarRecipeLedgerDocument();

            var schema = GetOrCreateSchema(out var schemaErr);
            if (schema == null)
            {
                error = string.IsNullOrWhiteSpace(schemaErr) ? "Schema lookup/create failed." : schemaErr;
                return false;
            }

            try
            {
                var json = JsonConvert.SerializeObject(model, Formatting.None);
                var ent = new Entity(schema);
                var f = schema.GetField(FieldLedgerJson);
                ent.Set<string>(f, json);
                storage.SetEntity(ent);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TryGetHostRecord(RebarRecipeLedgerDocument model, string hostUniqueId, out RebarRecipeLedgerHostRecord record)
        {
            record = null!;
            if (model == null || model.hosts == null) return false;
            var key = (hostUniqueId ?? string.Empty).Trim();
            if (key.Length == 0) return false;
            return model.hosts.TryGetValue(key, out record!);
        }

        public static void UpsertHostRecord(RebarRecipeLedgerDocument model, RebarRecipeLedgerHostRecord record)
        {
            if (model == null) return;
            if (model.hosts == null)
                model.hosts = new Dictionary<string, RebarRecipeLedgerHostRecord>(StringComparer.OrdinalIgnoreCase);

            var key = (record != null ? (record.hostUniqueId ?? string.Empty) : string.Empty).Trim();
            if (key.Length == 0) return;
            model.hosts[key] = record;
        }

        public static IEnumerable<RebarRecipeLedgerHostRecord> EnumerateRecords(RebarRecipeLedgerDocument model)
        {
            if (model == null || model.hosts == null) return Enumerable.Empty<RebarRecipeLedgerHostRecord>();
            return model.hosts.Values ?? Enumerable.Empty<RebarRecipeLedgerHostRecord>();
        }
    }
}
