#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using RevitMcpServer.Engine;
using RevitMcpServer.Infra;
using RevitMcpServer.Mcp;

namespace RevitMcpServer.Web
{
    internal static class ScheduleExcelRoundtripRoutes
    {
        private const string MetadataSheetName = "__room_roundtrip_meta";
        private const string DataSheetName = "Rooms";
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        private static readonly ConcurrentDictionary<string, PreviewSession> PreviewSessions = new ConcurrentDictionary<string, PreviewSession>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, ScheduleImportPreviewSession> ScheduleImportPreviewSessions = new ConcurrentDictionary<string, ScheduleImportPreviewSession>(StringComparer.OrdinalIgnoreCase);
        private static readonly string[] DefaultPermissions = new[] { "export", "preview", "apply" };
        private static readonly string[] Truthy = new[] { "true", "yes", "y", "1", "on", "はい", "有", "☑", "✓" };
        private static readonly string[] Falsey = new[] { "false", "no", "n", "0", "off", "いいえ", "無", "☐", "□" };

        public static void MapScheduleExcelRoundtrip(this WebApplication app)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            app.MapGet("/api/room-excel-roundtrip/create-link", (HttpRequest req) =>
            {
                CleanupExpiredPreviewSessions();

                if (!IsLoopbackRequest(req))
                    return Results.Json(new { ok = false, code = "LOCAL_ONLY", msg = "Link generation is available only from the local Revit machine." }, statusCode: 403);

                var docGuid = (req.Query["docGuid"].ToString() ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(docGuid))
                    return Results.BadRequest(new { ok = false, code = "DOC_GUID_REQUIRED", msg = "docGuid is required." });

                var docTitle = (req.Query["docTitle"].ToString() ?? string.Empty).Trim();
                var projectName = (req.Query["projectName"].ToString() ?? string.Empty).Trim();
                var editorUser = (req.Query["editorUser"].ToString() ?? string.Empty).Trim();
                var paramNames = ParseStringList(req.Query["paramNames"].ToString());

                var permissions = ParseStringList(req.Query["permissions"].ToString());
                if (permissions.Count == 0)
                    permissions = DefaultPermissions.ToList();

                var expiresMinutes = 60 * 24 * 3650;
                _ = int.TryParse(req.Query["expiresMinutes"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out expiresMinutes);
                if (expiresMinutes <= 0) expiresMinutes = 60 * 24 * 3650;
                if (expiresMinutes > 60 * 24 * 3650) expiresMinutes = 60 * 24 * 3650;

                var payload = new AccessTokenPayload
                {
                    DocGuid = docGuid,
                    DocTitle = docTitle,
                    ProjectName = projectName,
                    EditorUser = editorUser,
                    ParamNames = paramNames,
                    Permissions = permissions,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(expiresMinutes)
                };

                var token = CreateSignedToken(payload);
                var roomBaseUrl = ResolvePublicBaseUrl(req);
                var roomUrl = $"{roomBaseUrl}/room-excel-roundtrip?token={Uri.EscapeDataString(token)}";
                return Results.Json(new { ok = true, token, expiresUtc = payload.ExpiresUtc, permissions, paramNames, roomUrl });
            });

            app.MapGet("/room-excel-roundtrip", (HttpRequest req) =>
            {
                CleanupExpiredPreviewSessions();
                if (!TryReadAccessToken(req, out var tokenPayload, out var tokenError))
                    return Results.Text(BuildErrorHtml(tokenError ?? "Invalid token."), "text/html", Encoding.UTF8, StatusCodes.Status401Unauthorized);
                return Results.Text(BuildHtmlPage(tokenPayload!), "text/html", Encoding.UTF8);
            });

            app.MapGet("/api/room-excel-roundtrip/schedules", async (HttpRequest req, DurableQueue durable, JobIndex index) =>
            {
                CleanupExpiredPreviewSessions();
                if (!TryReadAccessToken(req, out var tokenPayload, out var tokenError, requiredPermission: "export"))
                    return Results.Json(new { ok = false, code = "UNAUTHORIZED", msg = tokenError ?? "Invalid token." }, statusCode: 401);

                try
                {
                    var payload = await InvokeAddinAsync(durable, index, "get_schedules", new
                    {
                        docGuid = tokenPayload!.DocGuid,
                        docTitle = tokenPayload.DocTitle
                    }, timeoutSeconds: 90);

                    var root = payload as JsonObject ?? new JsonObject();
                    if (root["ok"]?.GetValue<bool>() != true)
                        return Results.Json(new { ok = false, code = "GET_SCHEDULES_FAILED", msg = root["msg"]?.GetValue<string>() ?? "get_schedules failed." }, statusCode: 500);

                    var schedules = (root["schedules"] as JsonArray ?? new JsonArray())
                        .OfType<JsonObject>()
                        .Select(x => new
                        {
                            scheduleViewId = x["scheduleViewId"]?.GetValue<int>() ?? 0,
                            title = x["title"]?.GetValue<string>() ?? string.Empty,
                            categoryName = x["categoryName"]?.GetValue<string>() ?? string.Empty,
                            isActive = x["isActive"]?.GetValue<bool>() ?? false
                        })
                        .Where(x => x.scheduleViewId > 0 && !string.IsNullOrWhiteSpace(x.title))
                        .OrderBy(x => x.title, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    return Results.Json(new { ok = true, schedules });
                }
                catch (Exception ex)
                {
                    Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ROOM_RT schedules fail docGuid={tokenPayload!.DocGuid} msg={ex.Message}");
                    return Results.Json(new { ok = false, code = "GET_SCHEDULES_FAILED", msg = ex.Message }, statusCode: 500);
                }
            });

            app.MapGet("/api/room-excel-roundtrip/export", async (HttpRequest req, DurableQueue durable, JobIndex index) =>
            {
                CleanupExpiredPreviewSessions();
                if (!TryReadAccessToken(req, out var tokenPayload, out var tokenError, requiredPermission: "export"))
                    return Results.Json(new { ok = false, code = "UNAUTHORIZED", msg = tokenError ?? "Invalid token." }, statusCode: 401);

                try
                {
                    var requestedScheduleId = 0;
                    _ = int.TryParse(req.Query["scheduleViewId"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out requestedScheduleId);
                    if (requestedScheduleId > 0)
                    {
                        var tempDir = Path.Combine(Path.GetTempPath(), "RevitMCP", "HtmlScheduleExport");
                        Directory.CreateDirectory(tempDir);
                        var tempPath = Path.Combine(tempDir, $"schedule_{requestedScheduleId}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

                        var payload = await InvokeAddinAsync(durable, index, "export_schedule_roundtrip_excel", new
                        {
                            docGuid = tokenPayload!.DocGuid,
                            docTitle = tokenPayload.DocTitle,
                            viewId = requestedScheduleId,
                            outputPath = tempPath,
                            includeReadonlyColumns = true,
                            mode = "auto"
                        }, timeoutSeconds: 180);

                        var root = payload as JsonObject ?? new JsonObject();
                        if (root["ok"]?.GetValue<bool>() != true)
                            return Results.Json(new { ok = false, code = "SCHEDULE_EXPORT_FAILED", msg = root["msg"]?.GetValue<string>() ?? "export_schedule_roundtrip_excel failed." }, statusCode: 500);

                        var actualPath = root["path"]?.GetValue<string>() ?? tempPath;
                        if (!File.Exists(actualPath))
                            return Results.Json(new { ok = false, code = "SCHEDULE_EXPORT_MISSING", msg = "Exported Excel file was not found." }, statusCode: 500);

                        var scheduleBytes = await File.ReadAllBytesAsync(actualPath);
                        TryDeleteFile(actualPath);
                        Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ROOM_RT schedule export docGuid={tokenPayload.DocGuid} scheduleViewId={requestedScheduleId} bytes={scheduleBytes.Length}");
                        return Results.File(scheduleBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"export_schedule_{requestedScheduleId}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                    }

                    return Results.Json(new { ok = false, code = "SCHEDULE_REQUIRED", msg = "scheduleViewId is required." }, statusCode: 400);
                }
                catch (Exception ex)
                {
                    Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ROOM_RT export fail docGuid={tokenPayload!.DocGuid} msg={ex.Message}");
                    return Results.Json(new { ok = false, code = "EXPORT_FAILED", msg = ex.Message }, statusCode: 500);
                }
            });

            app.MapGet("/api/room-excel-roundtrip/schedule-preview", async (HttpRequest req, DurableQueue durable, JobIndex index) =>
            {
                CleanupExpiredPreviewSessions();
                if (!TryReadAccessToken(req, out var tokenPayload, out var tokenError, requiredPermission: "export"))
                    return Results.Json(new { ok = false, code = "UNAUTHORIZED", msg = tokenError ?? "Invalid token." }, statusCode: 401);

                try
                {
                    var scheduleViewId = 0;
                    _ = int.TryParse(req.Query["scheduleViewId"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out scheduleViewId);
                    if (scheduleViewId <= 0)
                        return Results.Json(new { ok = false, code = "SCHEDULE_REQUIRED", msg = "scheduleViewId is required." }, statusCode: 400);

                    var count = 200;
                    _ = int.TryParse(req.Query["count"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out count);
                    if (count <= 0) count = 200;
                    if (count > 500) count = 500;

                    var payload = await InvokeAddinAsync(durable, index, "get_schedule_data", new
                    {
                        docGuid = tokenPayload!.DocGuid,
                        docTitle = tokenPayload.DocTitle,
                        scheduleViewId,
                        skip = 0,
                        count
                    }, timeoutSeconds: 90);

                    var root = payload as JsonObject ?? new JsonObject();
                    if (root["ok"]?.GetValue<bool>() != true)
                        return Results.Json(new { ok = false, code = "SCHEDULE_PREVIEW_FAILED", msg = root["message"]?.GetValue<string>() ?? root["msg"]?.GetValue<string>() ?? "get_schedule_data failed." }, statusCode: 500);

                    var columns = (root["columns"] as JsonArray ?? new JsonArray())
                        .Select(x => x?.GetValue<string>() ?? string.Empty)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();
                    var rows = (root["rows"] as JsonArray ?? new JsonArray()).ToJsonString();
                    var totalCount = root["totalCount"]?.GetValue<int>() ?? 0;
                    var shownCount = root["shownCount"]?.GetValue<int>() ?? 0;

                    return Results.Content(JsonSerializer.Serialize(new
                    {
                        ok = true,
                        scheduleViewId,
                        scheduleName = (req.Query["scheduleName"].ToString() ?? string.Empty).Trim(),
                        columns,
                        totalCount,
                        shownCount,
                        truncated = totalCount > shownCount,
                        rows = JsonNode.Parse(rows)
                    }, JsonOptions), "application/json", Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ROOM_RT schedule-preview-html fail docGuid={tokenPayload!.DocGuid} msg={ex.Message}");
                    return Results.Json(new { ok = false, code = "SCHEDULE_PREVIEW_FAILED", msg = ex.Message }, statusCode: 500);
                }
            });

            app.MapPost("/api/room-excel-roundtrip/preview", async (HttpRequest req, DurableQueue durable, JobIndex index) =>
            {
                CleanupExpiredPreviewSessions();
                if (!TryReadAccessToken(req, out var tokenPayload, out var tokenError, requiredPermission: "preview"))
                    return Results.Json(new { ok = false, code = "UNAUTHORIZED", msg = tokenError ?? "Invalid token." }, statusCode: 401);

                try
                {
                    var form = await req.ReadFormAsync();
                    var file = form.Files["file"];
                    if (file == null || file.Length <= 0)
                        return Results.Json(new { ok = false, code = "FILE_REQUIRED", msg = "Excel file is required." }, statusCode: 400);
                    if (!IsSupportedExcelExtension(file.FileName))
                        return Results.Json(new { ok = false, code = "INVALID_EXTENSION", msg = "Only .xlsx and .xltx are supported. Macro-enabled Excel files are not accepted." }, statusCode: 400);
                    if (file.Length > 15 * 1024 * 1024)
                        return Results.Json(new { ok = false, code = "FILE_TOO_LARGE", msg = "Excel file is too large." }, statusCode: 400);

                    byte[] bytes;
                    using (var ms = new MemoryStream())
                    {
                        await file.CopyToAsync(ms);
                        bytes = ms.ToArray();
                    }

                    var workbook = ParseWorkbook(bytes);
                    if (!string.Equals(workbook.DocGuid, tokenPayload!.DocGuid, StringComparison.OrdinalIgnoreCase))
                        return Results.Json(new { ok = false, code = "DOC_GUID_MISMATCH", msg = "Workbook docGuid does not match token docGuid." }, statusCode: 409);

                    var snapshot = await LoadRoomSnapshotAsync(durable, index, tokenPayload!);
                    var preview = BuildPreview(tokenPayload!, workbook, snapshot);
                    PreviewSessions[preview.PreviewToken] = preview;
                    Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ROOM_RT preview docGuid={tokenPayload.DocGuid} token={preview.PreviewToken} rows={preview.RowCount} ready={preview.ReadyCount} confirm={preview.ConfirmCount}");
                    return Results.Json(new
                    {
                        ok = true,
                        previewToken = preview.PreviewToken,
                        docGuid = preview.DocGuid,
                        rowCount = preview.RowCount,
                        readyCount = preview.ReadyCount,
                        unchangedCount = preview.UnchangedCount,
                        confirmRequiredCount = preview.ConfirmCount,
                        readOnlyCount = preview.ReadOnlyCount,
                        notFoundCount = preview.NotFoundCount,
                        rows = preview.Rows.Select(r => new
                        {
                            rowNumber = r.RowNumber,
                            roomUniqueId = r.RoomUniqueId,
                            roomName = r.RoomName,
                            roomNumber = r.RoomNumber,
                            level = r.Level,
                            status = r.Status,
                            message = r.Message,
                            cells = r.Cells.Select(c => new { paramName = c.ParamName, status = c.Status, currentValue = c.CurrentDisplay, importedValue = c.ImportedDisplay, message = c.Message })
                        })
                    });
                }
                catch (Exception ex)
                {
                    Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ROOM_RT preview fail docGuid={tokenPayload!.DocGuid} msg={ex.Message}");
                    return Results.Json(new { ok = false, code = "PREVIEW_FAILED", msg = ex.Message }, statusCode: 500);
                }
            });

            app.MapPost("/api/room-excel-roundtrip/import-preview", async (HttpRequest req, DurableQueue durable, JobIndex index) =>
            {
                CleanupExpiredPreviewSessions();
                if (!TryReadAccessToken(req, out var tokenPayload, out var tokenError, requiredPermission: "apply"))
                    return Results.Json(new { ok = false, code = "UNAUTHORIZED", msg = tokenError ?? "Invalid token." }, statusCode: 401);

                string? tempPath = null;
                try
                {
                    var form = await req.ReadFormAsync();
                    var file = form.Files["file"];
                    if (file == null || file.Length <= 0)
                        return Results.Json(new { ok = false, code = "FILE_REQUIRED", msg = "Excel file is required." }, statusCode: 400);
                    if (!IsSupportedExcelExtension(file.FileName))
                        return Results.Json(new { ok = false, code = "INVALID_EXTENSION", msg = "Only .xlsx and .xltx are supported. Macro-enabled Excel files are not accepted." }, statusCode: 400);
                    if (file.Length > 15 * 1024 * 1024)
                        return Results.Json(new { ok = false, code = "FILE_TOO_LARGE", msg = "Excel file is too large." }, statusCode: 400);

                    var projectDir = GetProjectFolder(tokenPayload!);
                    var importDir = Path.Combine(projectDir, "Imports");
                    Directory.CreateDirectory(importDir);

                    var importBaseName = NormalizeWorkbookBaseName(file.FileName);
                    var importExtension = NormalizeWorkbookExtension(file.FileName);
                    tempPath = Path.Combine(importDir, $"{importBaseName}_{DateTime.Now:yyyyMMdd_HHmmss}{importExtension}");
                    using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await file.CopyToAsync(fs);
                    }

                    var payload = await InvokeAddinAsync(durable, index, "preview_schedule_roundtrip_excel", new
                    {
                        docGuid = tokenPayload!.DocGuid,
                        docTitle = tokenPayload.DocTitle,
                        filePath = tempPath
                    }, timeoutSeconds: 180);

                    var root = payload as JsonObject ?? new JsonObject();
                    if (root["ok"]?.GetValue<bool>() != true)
                        return Results.Json(new
                        {
                            ok = false,
                            code = "PREVIEW_FAILED",
                            msg = root["msg"]?.GetValue<string>() ?? "preview_schedule_roundtrip_excel failed.",
                            detail = root["detail"]?.GetValue<string>() ?? string.Empty
                        }, statusCode: 500);

                    var previewToken = Guid.NewGuid().ToString("N");
                    var session = new ScheduleImportPreviewSession
                    {
                        PreviewToken = previewToken,
                        DocGuid = tokenPayload.DocGuid,
                        DocTitle = tokenPayload.DocTitle,
                        ScheduleName = root["scheduleName"]?.GetValue<string>() ?? string.Empty,
                        ScheduleViewId = root["scheduleViewId"]?.GetValue<string>() ?? string.Empty,
                        UploadedFilePath = tempPath,
                        UploadedFileName = Path.GetFileName(tempPath),
                        CreatedUtc = DateTimeOffset.UtcNow,
                        ExpiresUtc = tokenPayload.ExpiresUtc,
                        PreviewPayload = root
                    };
                    ScheduleImportPreviewSessions[previewToken] = session;

                    Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ROOM_RT schedule-preview docGuid={tokenPayload.DocGuid} previewToken={previewToken} file={tempPath}");
                    return Results.Json(new
                    {
                        ok = true,
                        previewToken,
                        uploadedPath = tempPath,
                        uploadedFileName = Path.GetFileName(tempPath),
                        mode = root["mode"]?.GetValue<string>() ?? string.Empty,
                        editableColumnCount = root["editableColumnCount"]?.GetValue<int>() ?? 0,
                        changedCellCount = root["changedCellCount"]?.GetValue<int>() ?? 0,
                        unchangedCellCount = root["unchangedCellCount"]?.GetValue<int>() ?? 0,
                        skippedCellCount = root["skippedCellCount"]?.GetValue<int>() ?? 0,
                        failedCellCount = root["failedCellCount"]?.GetValue<int>() ?? 0,
                        scheduleName = root["scheduleName"]?.GetValue<string>() ?? string.Empty,
                        scheduleViewId = root["scheduleViewId"]?.GetValue<string>() ?? string.Empty,
                        rows = root["rows"]
                    });
                }
                catch (Exception ex)
                {
                    Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ROOM_RT schedule-preview fail docGuid={tokenPayload!.DocGuid} msg={ex.Message}");
                    return Results.Json(new { ok = false, code = "PREVIEW_FAILED", msg = ex.Message }, statusCode: 500);
                }
            });

            app.MapPost("/api/room-excel-roundtrip/import", async (HttpRequest req, DurableQueue durable, JobIndex index) =>
            {
                CleanupExpiredPreviewSessions();
                if (!TryReadAccessToken(req, out var tokenPayload, out var tokenError, requiredPermission: "apply"))
                    return Results.Json(new { ok = false, code = "UNAUTHORIZED", msg = tokenError ?? "Invalid token." }, statusCode: 401);

                string? tempPath = null;
                try
                {
                    var form = await req.ReadFormAsync();
                    var file = form.Files["file"];
                    if (file == null || file.Length <= 0)
                        return Results.Json(new { ok = false, code = "FILE_REQUIRED", msg = "Excel file is required." }, statusCode: 400);
                    if (!IsSupportedExcelExtension(file.FileName))
                        return Results.Json(new { ok = false, code = "INVALID_EXTENSION", msg = "Only .xlsx and .xltx are supported. Macro-enabled Excel files are not accepted." }, statusCode: 400);
                    if (file.Length > 15 * 1024 * 1024)
                        return Results.Json(new { ok = false, code = "FILE_TOO_LARGE", msg = "Excel file is too large." }, statusCode: 400);

                    var projectDir = GetProjectFolder(tokenPayload!);
                    var importDir = Path.Combine(projectDir, "Imports");
                    var reportDir = Path.Combine(projectDir, "Reports");
                    var auditDir = Path.Combine(projectDir, "Audit");
                    Directory.CreateDirectory(importDir);
                    Directory.CreateDirectory(reportDir);
                    Directory.CreateDirectory(auditDir);

                    var importBaseName = NormalizeWorkbookBaseName(file.FileName);
                    var importExtension = NormalizeWorkbookExtension(file.FileName);
                    tempPath = Path.Combine(importDir, $"{importBaseName}_{DateTime.Now:yyyyMMdd_HHmmss}{importExtension}");
                    using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await file.CopyToAsync(fs);
                    }

                    var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                    var reportPath = Path.Combine(reportDir, $"{importBaseName}_import_report_{stamp}.csv");
                    var auditJsonPath = Path.Combine(auditDir, $"{importBaseName}_changes_{stamp}.json");
                    var payload = await InvokeAddinAsync(durable, index, "import_schedule_roundtrip_excel", new
                    {
                        docGuid = tokenPayload!.DocGuid,
                        docTitle = tokenPayload.DocTitle,
                        filePath = tempPath,
                        reportPath = reportPath,
                        auditJsonPath = auditJsonPath
                    }, timeoutSeconds: 180);

                    var root = payload as JsonObject ?? new JsonObject();
                    if (root["ok"]?.GetValue<bool>() != true)
                        return Results.Json(new
                        {
                            ok = false,
                            code = "IMPORT_FAILED",
                            msg = root["msg"]?.GetValue<string>() ?? "import_schedule_roundtrip_excel failed.",
                            detail = root["detail"]?.GetValue<string>() ?? string.Empty
                        }, statusCode: 500);

                    var updatedCount = root["updatedCount"]?.GetValue<int>() ?? 0;
                    var skippedCount = root["skippedCount"]?.GetValue<int>() ?? 0;
                    var failedCount = root["failedCount"]?.GetValue<int>() ?? 0;
                    Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ROOM_RT import docGuid={tokenPayload.DocGuid} updated={updatedCount} skipped={skippedCount} failed={failedCount}");
                    return Results.Json(new
                    {
                        ok = true,
                        path = root["path"]?.GetValue<string>() ?? tempPath,
                        reportPath = root["reportPath"]?.GetValue<string>() ?? reportPath,
                        auditJsonPath = root["auditJsonPath"]?.GetValue<string>() ?? auditJsonPath,
                        mode = root["mode"]?.GetValue<string>() ?? string.Empty,
                        updatedCount = updatedCount,
                        skippedCount = skippedCount,
                        failedCount = failedCount,
                        editableColumnCount = root["editableColumnCount"]?.GetValue<int>() ?? 0
                    });
                }
                catch (Exception ex)
                {
                    Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ROOM_RT import fail docGuid={tokenPayload!.DocGuid} msg={ex.Message}");
                    return Results.Json(new { ok = false, code = "IMPORT_FAILED", msg = ex.Message }, statusCode: 500);
                }
                finally
                {
                }
            });

            app.MapPost("/api/room-excel-roundtrip/import-apply", async (HttpRequest req, DurableQueue durable, JobIndex index) =>
            {
                CleanupExpiredPreviewSessions();
                if (!TryReadAccessToken(req, out var tokenPayload, out var tokenError, requiredPermission: "apply"))
                    return Results.Json(new { ok = false, code = "UNAUTHORIZED", msg = tokenError ?? "Invalid token." }, statusCode: 401);

                JsonObject? body;
                using (var reader = new StreamReader(req.Body, Encoding.UTF8))
                {
                    var raw = await reader.ReadToEndAsync();
                    body = JsonNode.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw) as JsonObject;
                }

                var previewToken = (body?["previewToken"]?.GetValue<string>() ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(previewToken))
                    return Results.Json(new { ok = false, code = "PREVIEW_TOKEN_REQUIRED", msg = "previewToken is required." }, statusCode: 400);
                if (!ScheduleImportPreviewSessions.TryGetValue(previewToken, out var preview))
                    return Results.Json(new { ok = false, code = "PREVIEW_NOT_FOUND", msg = "Preview token not found or expired." }, statusCode: 404);
                if (!string.Equals(preview.DocGuid, tokenPayload!.DocGuid, StringComparison.OrdinalIgnoreCase))
                    return Results.Json(new { ok = false, code = "DOC_GUID_MISMATCH", msg = "Preview token docGuid mismatch." }, statusCode: 409);
                if (!File.Exists(preview.UploadedFilePath))
                    return Results.Json(new { ok = false, code = "UPLOAD_NOT_FOUND", msg = "Uploaded workbook for preview no longer exists." }, statusCode: 410);

                try
                {
                    var confirmPayload = await InvokeAddinAsync(durable, index, "confirm_html_schedule_import", new
                    {
                        docGuid = tokenPayload!.DocGuid,
                        docTitle = tokenPayload.DocTitle,
                        scheduleName = preview.ScheduleName,
                        uploadedFileName = preview.UploadedFileName,
                        changedCellCount = preview.PreviewPayload?["changedCellCount"]?.GetValue<int>() ?? 0
                    }, timeoutSeconds: 600);

                    var confirmRoot = confirmPayload as JsonObject ?? new JsonObject();
                    var decision = confirmRoot["decision"]?.GetValue<string>() ?? string.Empty;
                    if (!string.Equals(decision, "approved", StringComparison.OrdinalIgnoreCase))
                    {
                        Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ROOM_RT import-apply rejected docGuid={tokenPayload.DocGuid} previewToken={previewToken}");
                        return Results.Json(new
                        {
                            ok = false,
                            code = "REJECTED_BY_REVIT",
                            msg = "Revit 側で拒否されました。",
                            queueEligible = true,
                            previewToken,
                            scheduleName = preview.ScheduleName
                        }, statusCode: 409);
                    }

                    var projectDir = GetProjectFolder(tokenPayload!);
                    var reportDir = Path.Combine(projectDir, "Reports");
                    var auditDir = Path.Combine(projectDir, "Audit");
                    Directory.CreateDirectory(reportDir);
                    Directory.CreateDirectory(auditDir);

                    var importBaseName = NormalizeWorkbookBaseName(preview.UploadedFileName);
                    var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                    var reportPath = Path.Combine(reportDir, $"{importBaseName}_import_report_{stamp}.csv");
                    var auditJsonPath = Path.Combine(auditDir, $"{importBaseName}_changes_{stamp}.json");
                    var payload = await InvokeAddinAsync(durable, index, "import_schedule_roundtrip_excel", new
                    {
                        docGuid = tokenPayload!.DocGuid,
                        docTitle = tokenPayload.DocTitle,
                        filePath = preview.UploadedFilePath,
                        reportPath = reportPath,
                        auditJsonPath = auditJsonPath
                    }, timeoutSeconds: 180);

                    var root = payload as JsonObject ?? new JsonObject();
                    if (root["ok"]?.GetValue<bool>() != true)
                        return Results.Json(new
                        {
                            ok = false,
                            code = "IMPORT_FAILED",
                            msg = root["msg"]?.GetValue<string>() ?? "import_schedule_roundtrip_excel failed.",
                            detail = root["detail"]?.GetValue<string>() ?? string.Empty
                        }, statusCode: 500);

                    ScheduleImportPreviewSessions.TryRemove(previewToken, out _);
                    Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ROOM_RT import-apply docGuid={tokenPayload.DocGuid} previewToken={previewToken} updated={root["updatedCount"]?.GetValue<int>() ?? 0}");
                    return Results.Json(new
                    {
                        ok = true,
                        previewToken,
                        path = root["path"]?.GetValue<string>() ?? preview.UploadedFilePath,
                        reportPath = root["reportPath"]?.GetValue<string>() ?? reportPath,
                        auditJsonPath = root["auditJsonPath"]?.GetValue<string>() ?? auditJsonPath,
                        mode = root["mode"]?.GetValue<string>() ?? string.Empty,
                        updatedCount = root["updatedCount"]?.GetValue<int>() ?? 0,
                        changedCount = root["changedCount"]?.GetValue<int>() ?? 0,
                        unchangedCount = root["unchangedCount"]?.GetValue<int>() ?? 0,
                        skippedCount = root["skippedCount"]?.GetValue<int>() ?? 0,
                        failedCount = root["failedCount"]?.GetValue<int>() ?? 0,
                        editableColumnCount = root["editableColumnCount"]?.GetValue<int>() ?? 0
                    });
                }
                catch (Exception ex)
                {
                    Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ROOM_RT import-apply fail docGuid={tokenPayload!.DocGuid} previewToken={previewToken} msg={ex.Message}");
                    return Results.Json(new { ok = false, code = "IMPORT_FAILED", msg = ex.Message }, statusCode: 500);
                }
            });

            app.MapPost("/api/room-excel-roundtrip/import-verify", async (HttpRequest req, DurableQueue durable, JobIndex index) =>
            {
                CleanupExpiredPreviewSessions();
                if (!TryReadAccessToken(req, out var tokenPayload, out var tokenError, requiredPermission: "apply"))
                    return Results.Json(new { ok = false, code = "UNAUTHORIZED", msg = tokenError ?? "Invalid token." }, statusCode: 401);

                JsonObject? body;
                using (var reader = new StreamReader(req.Body, Encoding.UTF8))
                {
                    var json = await reader.ReadToEndAsync();
                    body = string.IsNullOrWhiteSpace(json) ? new JsonObject() : JsonNode.Parse(json) as JsonObject;
                }

                var filePath = (body?["filePath"]?.GetValue<string>() ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(filePath))
                    return Results.Json(new { ok = false, code = "FILE_REQUIRED", msg = "filePath is required." }, statusCode: 400);

                try
                {
                    var projectDir = GetProjectFolder(tokenPayload!);
                    if (!IsPathUnderDirectory(filePath, projectDir))
                        return Results.Json(new { ok = false, code = "INVALID_FILE_PATH", msg = "Verification file must be under the project folder." }, statusCode: 403);
                    if (!File.Exists(filePath))
                        return Results.Json(new { ok = false, code = "FILE_NOT_FOUND", msg = "Verification file was not found." }, statusCode: 404);

                    var payload = await InvokeAddinAsync(durable, index, "preview_schedule_roundtrip_excel", new
                    {
                        docGuid = tokenPayload!.DocGuid,
                        docTitle = tokenPayload.DocTitle,
                        filePath = filePath
                    }, timeoutSeconds: 180);

                    var root = payload as JsonObject ?? new JsonObject();
                    if (root["ok"]?.GetValue<bool>() != true)
                        return Results.Json(new
                        {
                            ok = false,
                            code = "VERIFY_FAILED",
                            msg = root["msg"]?.GetValue<string>() ?? "preview_schedule_roundtrip_excel failed.",
                            detail = root["detail"]?.GetValue<string>() ?? string.Empty
                        }, statusCode: 500);

                    var changedCellCount = root["changedCellCount"]?.GetValue<int>() ?? 0;
                    var failedCellCount = root["failedCellCount"]?.GetValue<int>() ?? 0;
                    var reflected = changedCellCount == 0 && failedCellCount == 0;

                    return Results.Json(new
                    {
                        ok = true,
                        reflected,
                        filePath,
                        mode = root["mode"]?.GetValue<string>() ?? string.Empty,
                        editableColumnCount = root["editableColumnCount"]?.GetValue<int>() ?? 0,
                        changedCellCount = changedCellCount,
                        unchangedCellCount = root["unchangedCellCount"]?.GetValue<int>() ?? 0,
                        skippedCellCount = root["skippedCellCount"]?.GetValue<int>() ?? 0,
                        failedCellCount = failedCellCount,
                        scheduleName = root["scheduleName"]?.GetValue<string>() ?? string.Empty,
                        scheduleViewId = root["scheduleViewId"]?.GetValue<string>() ?? string.Empty,
                        rows = root["rows"]
                    });
                }
                catch (Exception ex)
                {
                    Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ROOM_RT import-verify fail docGuid={tokenPayload!.DocGuid} msg={ex.Message}");
                    return Results.Json(new { ok = false, code = "VERIFY_FAILED", msg = ex.Message }, statusCode: 500);
                }
            });

            app.MapPost("/api/room-excel-roundtrip/import-queue", async (HttpRequest req) =>
            {
                CleanupExpiredPreviewSessions();
                if (!TryReadAccessToken(req, out var tokenPayload, out var tokenError, requiredPermission: "apply"))
                    return Results.Json(new { ok = false, code = "UNAUTHORIZED", msg = tokenError ?? "Invalid token." }, statusCode: 401);

                JsonObject? body;
                using (var reader = new StreamReader(req.Body, Encoding.UTF8))
                {
                    var raw = await reader.ReadToEndAsync();
                    body = JsonNode.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw) as JsonObject;
                }

                var previewToken = (body?["previewToken"]?.GetValue<string>() ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(previewToken))
                    return Results.Json(new { ok = false, code = "PREVIEW_TOKEN_REQUIRED", msg = "previewToken is required." }, statusCode: 400);
                if (!ScheduleImportPreviewSessions.TryGetValue(previewToken, out var preview))
                    return Results.Json(new { ok = false, code = "PREVIEW_NOT_FOUND", msg = "Preview token not found or expired." }, statusCode: 404);
                if (!string.Equals(preview.DocGuid, tokenPayload!.DocGuid, StringComparison.OrdinalIgnoreCase))
                    return Results.Json(new { ok = false, code = "DOC_GUID_MISMATCH", msg = "Preview token docGuid mismatch." }, statusCode: 409);
                if (!File.Exists(preview.UploadedFilePath))
                    return Results.Json(new { ok = false, code = "UPLOAD_NOT_FOUND", msg = "Uploaded workbook for preview no longer exists." }, statusCode: 410);

                try
                {
                    var projectDir = GetProjectFolder(tokenPayload!);
                    var queueDir = Path.Combine(projectDir, "Queue");
                    Directory.CreateDirectory(queueDir);

                    var queueId = Guid.NewGuid().ToString("N");
                    var entry = new ScheduleImportQueueEntry
                    {
                        QueueId = queueId,
                        QueueFilePath = Path.Combine(queueDir, $"{queueId}.json"),
                        PreviewToken = previewToken,
                        DocGuid = tokenPayload.DocGuid,
                        DocTitle = tokenPayload.DocTitle,
                        ScheduleName = preview.ScheduleName,
                        UploadedFilePath = preview.UploadedFilePath,
                        UploadedFileName = preview.UploadedFileName,
                        RequestedBy = tokenPayload.EditorUser,
                        ProjectFolderPath = projectDir,
                        ChangedCellCount = preview.PreviewPayload?["changedCellCount"]?.GetValue<int>() ?? 0,
                        EditableColumnCount = preview.PreviewPayload?["editableColumnCount"]?.GetValue<int>() ?? 0,
                        Status = "queued",
                        CreatedUtc = DateTimeOffset.UtcNow,
                        NextPromptUtc = DateTimeOffset.UtcNow.AddMinutes(5)
                    };
                    SaveScheduleImportQueueEntry(entry);

                    Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ROOM_RT import-queue docGuid={tokenPayload.DocGuid} previewToken={previewToken} queueId={queueId}");
                    return Results.Json(new
                    {
                        ok = true,
                        queueId,
                        status = "queued",
                        msg = "キューに入れました。後でRevit に反映されます。",
                        nextPromptUtc = entry.NextPromptUtc
                    });
                }
                catch (Exception ex)
                {
                    Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ROOM_RT import-queue fail docGuid={tokenPayload!.DocGuid} previewToken={previewToken} msg={ex.Message}");
                    return Results.Json(new { ok = false, code = "QUEUE_FAILED", msg = ex.Message }, statusCode: 500);
                }
            });

            app.MapPost("/api/room-excel-roundtrip/queue-delete", async (HttpRequest req) =>
            {
                CleanupExpiredPreviewSessions();
                if (!TryReadAccessToken(req, out var tokenPayload, out var tokenError, requiredPermission: "apply"))
                    return Results.Json(new { ok = false, code = "UNAUTHORIZED", msg = tokenError ?? "Invalid token." }, statusCode: 401);

                JsonObject? body;
                using (var reader = new StreamReader(req.Body, Encoding.UTF8))
                {
                    var raw = await reader.ReadToEndAsync();
                    body = JsonNode.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw) as JsonObject;
                }

                var queueId = (body?["queueId"]?.GetValue<string>() ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(queueId))
                    return Results.Json(new { ok = false, code = "QUEUE_ID_REQUIRED", msg = "queueId is required." }, statusCode: 400);

                var projectDir = GetProjectFolder(tokenPayload!);
                var queueFilePath = Path.Combine(projectDir, "Queue", $"{queueId}.json");
                var entry = LoadScheduleImportQueueEntry(queueFilePath);
                if (entry == null)
                    return Results.Json(new { ok = false, code = "QUEUE_NOT_FOUND", msg = "Queued request not found." }, statusCode: 404);

                try
                {
                    entry.Status = "deleted";
                    entry.DeletedUtc = DateTimeOffset.UtcNow;
                    entry.LastMessage = "deleted-from-html";
                    SaveScheduleImportQueueEntry(entry);
                    Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ROOM_RT queue-delete docGuid={tokenPayload.DocGuid} queueId={queueId}");
                    return Results.Json(new
                    {
                        ok = true,
                        queueId,
                        status = "deleted",
                        msg = "キューを削除しました。反映しなかったことを送信者に別途連絡してください。"
                    });
                }
                catch (Exception ex)
                {
                    Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ROOM_RT queue-delete fail docGuid={tokenPayload.DocGuid} queueId={queueId} msg={ex.Message}");
                    return Results.Json(new { ok = false, code = "QUEUE_DELETE_FAILED", msg = ex.Message }, statusCode: 500);
                }
            });

            app.MapPost("/api/room-excel-roundtrip/apply", async (HttpRequest req, DurableQueue durable, JobIndex index) =>
            {
                CleanupExpiredPreviewSessions();
                if (!TryReadAccessToken(req, out var tokenPayload, out var tokenError, requiredPermission: "apply"))
                    return Results.Json(new { ok = false, code = "UNAUTHORIZED", msg = tokenError ?? "Invalid token." }, statusCode: 401);

                JsonObject? body;
                using (var reader = new StreamReader(req.Body, Encoding.UTF8))
                {
                    var raw = await reader.ReadToEndAsync();
                    body = JsonNode.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw) as JsonObject;
                }

                var previewToken = (body?["previewToken"]?.GetValue<string>() ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(previewToken))
                    return Results.Json(new { ok = false, code = "PREVIEW_TOKEN_REQUIRED", msg = "previewToken is required." }, statusCode: 400);
                if (!PreviewSessions.TryGetValue(previewToken, out var preview))
                    return Results.Json(new { ok = false, code = "PREVIEW_NOT_FOUND", msg = "Preview token not found or expired." }, statusCode: 404);
                if (!string.Equals(preview.DocGuid, tokenPayload!.DocGuid, StringComparison.OrdinalIgnoreCase))
                    return Results.Json(new { ok = false, code = "DOC_GUID_MISMATCH", msg = "Preview token docGuid mismatch." }, statusCode: 409);

                var allowConfirmed = body?["allowConfirmedAmbiguous"]?.GetValue<bool>() ?? false;
                try
                {
                    var liveSnapshot = await LoadRoomSnapshotAsync(durable, index, tokenPayload!);
                    var liveByUniqueId = liveSnapshot.ToDictionary(x => x.UniqueId, StringComparer.OrdinalIgnoreCase);
                    var applyItems = new List<object>();
                    var skipped = new List<object>();
                    var conflicts = new List<object>();

                    foreach (var row in preview.Rows)
                    {
                        if (!liveByUniqueId.TryGetValue(row.RoomUniqueId, out var liveRoom))
                        {
                            conflicts.Add(new { rowNumber = row.RowNumber, roomUniqueId = row.RoomUniqueId, code = "ROOM_NOT_FOUND" });
                            continue;
                        }

                        foreach (var cell in row.Cells)
                        {
                            if (string.Equals(cell.Status, "UNCHANGED", StringComparison.OrdinalIgnoreCase))
                            {
                                skipped.Add(new { rowNumber = row.RowNumber, roomUniqueId = row.RoomUniqueId, paramName = cell.ParamName, code = "UNCHANGED" });
                                continue;
                            }
                            if (string.Equals(cell.Status, "CONFIRM_REQUIRED", StringComparison.OrdinalIgnoreCase) && !allowConfirmed)
                            {
                                skipped.Add(new { rowNumber = row.RowNumber, roomUniqueId = row.RoomUniqueId, paramName = cell.ParamName, code = "CONFIRM_REQUIRED" });
                                continue;
                            }
                            if (!liveRoom.Params.TryGetValue(cell.ParamName, out var liveParam))
                            {
                                conflicts.Add(new { rowNumber = row.RowNumber, roomUniqueId = row.RoomUniqueId, paramName = cell.ParamName, code = "PARAM_NOT_FOUND" });
                                continue;
                            }
                            if (liveParam.IsReadOnly)
                            {
                                skipped.Add(new { rowNumber = row.RowNumber, roomUniqueId = row.RoomUniqueId, paramName = cell.ParamName, code = "READ_ONLY" });
                                continue;
                            }

                            var liveComparable = BuildComparableValue(liveParam);
                            if (!string.Equals(liveComparable, cell.CurrentComparable, StringComparison.Ordinal))
                            {
                                conflicts.Add(new { rowNumber = row.RowNumber, roomUniqueId = row.RoomUniqueId, paramName = cell.ParamName, code = "CURRENT_VALUE_CHANGED", previewValue = cell.CurrentDisplay, liveValue = liveParam.Display });
                                continue;
                            }

                            applyItems.Add(new { elementId = liveRoom.ElementId, paramName = cell.ParamName, value = cell.TypedValue });
                        }
                    }

                    JsonNode? addinResult = null;
                    if (applyItems.Count > 0)
                    {
                        addinResult = await InvokeAddinAsync(durable, index, "set_room_params_bulk", new { docGuid = tokenPayload.DocGuid, docTitle = tokenPayload.DocTitle, items = applyItems }, timeoutSeconds: 180);
                    }

                    PreviewSessions.TryRemove(previewToken, out _);
                    Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ROOM_RT apply docGuid={tokenPayload.DocGuid} previewToken={previewToken} applyItems={applyItems.Count} skipped={skipped.Count} conflicts={conflicts.Count}");
                    return Results.Json(new { ok = true, previewToken, appliedCount = applyItems.Count, skippedCount = skipped.Count, conflictCount = conflicts.Count, addinResult, skipped, conflicts });
                }
                catch (Exception ex)
                {
                    Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ROOM_RT apply fail docGuid={tokenPayload!.DocGuid} previewToken={previewToken} msg={ex.Message}");
                    return Results.Json(new { ok = false, code = "APPLY_FAILED", msg = ex.Message }, statusCode: 500);
                }
            });
        }

        private static string BuildErrorHtml(string message)
        {
            return "<!doctype html><html><head><meta charset=\"utf-8\"><title>Schedule Excel Roundtrip</title></head><body><h1>Schedule Excel Roundtrip</h1><p style=\"color:#b00020;\">" + System.Net.WebUtility.HtmlEncode(message) + "</p></body></html>";
        }

        private static string GetProjectFolder(AccessTokenPayload token)
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Revit_MCP", "Projects");
            var name = SanitizeFileName($"{token.DocTitle}_{token.DocGuid}");
            var path = Path.Combine(root, name);
            Directory.CreateDirectory(path);
            return path;
        }

        private static bool IsPathUnderDirectory(string path, string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directoryPath))
                return false;

            try
            {
                var fullPath = Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var fullDirectory = Path.GetFullPath(directoryPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fullPath, fullDirectory, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeWorkbookBaseName(string fileName)
        {
            var raw = Path.GetFileNameWithoutExtension(fileName) ?? "schedule_import";
            var semicolon = raw.IndexOf(';');
            if (semicolon >= 0)
                raw = raw.Substring(0, semicolon);
            raw = raw.Replace("filename_=", string.Empty).Trim();
            raw = SanitizeFileName(raw);
            return string.IsNullOrWhiteSpace(raw) ? "schedule_import" : raw;
        }

        private static string NormalizeWorkbookExtension(string fileName)
        {
            var ext = Path.GetExtension(fileName);
            if (string.IsNullOrWhiteSpace(ext))
                return ".xlsx";
            return ext;
        }

        private static string BuildHtmlPage(AccessTokenPayload token)
        {
            var tokenJson = JsonSerializer.Serialize(new
            {
                docGuid = token.DocGuid,
                docTitle = token.DocTitle,
                projectName = token.ProjectName,
                editorUser = token.EditorUser,
                paramNames = token.ParamNames,
                permissions = token.Permissions,
                expiresUtc = token.ExpiresUtc
            }, new JsonSerializerOptions(JsonOptions) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            return $$"""
<!doctype html>
<html>
<head>
  <meta charset="utf-8"/>
  <meta name="viewport" content="width=device-width, initial-scale=1"/>
  <title>Schedule Excel Roundtrip</title>
  <style>
    body{font-family:Segoe UI,sans-serif;margin:24px;background:#f7f4ec;color:#1d1d1d}
    .card{background:#fff;padding:16px 18px;border:1px solid #d8d2c5;border-radius:12px;margin-bottom:16px}
    .row{display:flex;gap:12px;align-items:center;flex-wrap:wrap}
    button{background:#355c4d;color:#fff;border:none;border-radius:8px;padding:10px 14px;cursor:pointer}
    button.warn{background:#a55534}
    button:disabled{opacity:.55;cursor:default}
    input[type=file]{padding:8px;background:#faf8f1;border:1px solid #d8d2c5;border-radius:8px}
    table{border-collapse:collapse;width:100%;font-size:12px}
    th,td{border:1px solid #ddd4c3;padding:6px 8px;text-align:left;vertical-align:top}
    th{background:#ede7d8}
    .mono{font-family:Consolas,monospace}
    .ok{color:#24613a}
    .warnText{color:#9a6500}
    .err{color:#b00020}
    .changedCell{color:#b00020;font-weight:700;background:#fff1f1}
  </style>
</head>
<body>
    <h1>Schedule Excel Roundtrip</h1>
  <div class="card"><div id="meta"></div></div>
  <div class="card">
    <h2 style="margin-top:0">Schedule export</h2>
    <div class="row">
      <button id="btnRefreshSchedules" type="button">Refresh Schedules</button>
      <select id="scheduleSelect" style="min-width:360px;padding:8px;border:1px solid #d8d2c5;border-radius:8px;background:#faf8f1">
        <option value="">Loading schedules…</option>
      </select>
      <button id="btnExportSchedule" type="button">Export Selected Schedule</button>
      <button id="btnPreviewSchedule" type="button">Preview Selected Schedule</button>
    </div>
    <p class="mono" id="scheduleStatus"></p>
  </div>
  <div class="card">
    <h2 style="margin-top:0">Schedule Preview</h2>
    <div id="schedulePreviewSummary"></div>
    <div style="overflow:auto;max-height:45vh"><table id="schedulePreviewTable"><thead></thead><tbody></tbody></table></div>
  </div>
  <div class="card">
    <h2 style="margin-top:0">After Apply Preview</h2>
    <div id="afterSchedulePreviewSummary"></div>
    <div style="overflow:auto;max-height:45vh"><table id="afterSchedulePreviewTable"><thead></thead><tbody></tbody></table></div>
  </div>
  <div class="card">
    <h2 style="margin-top:0">Excel Import</h2>
      <div class="row">
        <input id="file" type="file" accept=".xlsx,.xltx"/>
        <button id="btnPreviewImport" class="warn">変更内容を確認</button>
        <button id="btnBack" type="button" style="display:none">戻る</button>
        <button id="btnApplyImport" class="warn" type="button" style="display:none">変更を実行</button>
        <button id="btnVerifyImport" type="button" style="display:none">反映確認</button>
        <button id="btnQueueImport" type="button" style="display:none">キューに入れる</button>
        <button id="btnDeleteQueue" type="button" style="display:none">キューを削除</button>
      </div>
      <p id="status" class="mono"></p>
      <p id="queueHint" class="mono warnText" style="display:none"></p>
  </div>
  <div class="card">
    <div id="summary"></div>
    <div style="overflow:auto;max-height:60vh"><table id="tbl"><thead></thead><tbody></tbody></table></div>
  </div>
  <script>
    const token = new URLSearchParams(location.search).get('token') || '';
    const info = {{JsonEncodedJs(tokenJson)}};
    const meta = document.getElementById('meta');
    const statusEl = document.getElementById('status');
    const scheduleStatusEl = document.getElementById('scheduleStatus');
    const schedulePreviewSummary = document.getElementById('schedulePreviewSummary');
    const schedulePreviewHead = document.querySelector('#schedulePreviewTable thead');
    const schedulePreviewBody = document.querySelector('#schedulePreviewTable tbody');
    const afterSchedulePreviewSummary = document.getElementById('afterSchedulePreviewSummary');
    const afterSchedulePreviewHead = document.querySelector('#afterSchedulePreviewTable thead');
    const afterSchedulePreviewBody = document.querySelector('#afterSchedulePreviewTable tbody');
    const summary = document.getElementById('summary');
    const tblHead = document.querySelector('#tbl thead');
    const tblBody = document.querySelector('#tbl tbody');
    const scheduleSelect = document.getElementById('scheduleSelect');
    const fileInput = document.getElementById('file');
    const btnPreviewSchedule = document.getElementById('btnPreviewSchedule');
    const btnPreviewImport = document.getElementById('btnPreviewImport');
    const btnApplyImport = document.getElementById('btnApplyImport');
    const btnVerifyImport = document.getElementById('btnVerifyImport');
    const btnBack = document.getElementById('btnBack');
    const btnQueueImport = document.getElementById('btnQueueImport');
    const btnDeleteQueue = document.getElementById('btnDeleteQueue');
    const queueHintEl = document.getElementById('queueHint');
    let activeImportPreviewToken = '';
    let activeQueueId = '';
    let activeImportedFilePath = '';
    let activeChangedCellKeys = new Set();
    let activeSchedulePreviewData = null;

    meta.innerHTML = `<div><b>Document:</b> ${escapeHtml(info.docTitle || '(untitled)')}</div>
      <div><b>Project:</b> ${escapeHtml(info.projectName || '(not set)')}</div>
      <div><b>Revit editor:</b> ${escapeHtml(info.editorUser || '(unknown)')}</div>
      <div><b>docGuid:</b> <span class="mono">${escapeHtml(info.docGuid || '')}</span></div>
      <div><b>Permissions:</b> ${escapeHtml((info.permissions || []).join(', '))}</div>
      <div><b>Expires:</b> ${escapeHtml(info.expiresUtc || '')}</div>`;

    document.getElementById('btnRefreshSchedules').addEventListener('click', async () => {
      await loadSchedules();
    });

    document.getElementById('btnExportSchedule').addEventListener('click', async () => {
      const scheduleViewId = scheduleSelect.value || '';
      if (!scheduleViewId) {
        scheduleStatus('Select a schedule first.', true);
        return;
      }
      scheduleStatus('Exporting selected schedule…');
      const res = await fetch('/api/room-excel-roundtrip/export?token=' + encodeURIComponent(token) + '&scheduleViewId=' + encodeURIComponent(scheduleViewId));
      if (!res.ok) {
        const t = await tryJson(res);
        scheduleStatus(t.msg || ('HTTP ' + res.status), true);
        return;
      }
      await downloadResponseBlob(res, 'schedule_export.xlsx');
      scheduleStatus('Schedule export completed.');
    });

    btnPreviewSchedule.addEventListener('click', async () => {
      const scheduleViewId = scheduleSelect.value || '';
      const selectedText = scheduleSelect.options[scheduleSelect.selectedIndex]?.text || '';
      if (!scheduleViewId) {
        scheduleStatus('Select a schedule first.', true);
        return;
      }
      scheduleStatus('Loading selected schedule preview…');
      const url = '/api/room-excel-roundtrip/schedule-preview?token=' + encodeURIComponent(token)
        + '&scheduleViewId=' + encodeURIComponent(scheduleViewId)
        + '&scheduleName=' + encodeURIComponent(selectedText)
        + '&count=200';
      const res = await fetch(url);
      const data = await tryJson(res);
      if (!res.ok || !data.ok) {
        scheduleStatus(data.msg || ('HTTP ' + res.status), true);
        return;
      }
      renderSchedulePreview(data);
      scheduleStatus('Schedule preview loaded.');
    });

    btnPreviewImport.addEventListener('click', async () => {
      const file = fileInput.files[0];
      if (!file) {
        status('Select an Excel file first.', true);
        return;
      }
      const fd = new FormData();
      fd.append('file', file);
      status('Comparing workbook with current Revit values…');
      const res = await fetch('/api/room-excel-roundtrip/import-preview?token=' + encodeURIComponent(token), { method: 'POST', body: fd });
      const data = await tryJson(res);
      if (!res.ok || !data.ok) {
        status(data.msg || ('HTTP ' + res.status), true);
        return;
      }
      activeImportPreviewToken = data.previewToken || '';
      activeImportedFilePath = data.uploadedPath || activeImportedFilePath;
      activeChangedCellKeys = buildChangedCellKeySet(data.rows);
      renderImportPreview(data);
      setImportMode(true);
      clearAfterSchedulePreview();
      status(`Preview ready: changed=${data.changedCellCount || 0}, unchanged=${data.unchangedCellCount || 0}, skipped=${data.skippedCellCount || 0}, failed=${data.failedCellCount || 0}`);
    });

    btnApplyImport.addEventListener('click', async () => {
      if (!activeImportPreviewToken) {
        status('No preview session. Upload and preview the workbook again.', true);
        return;
      }
      btnApplyImport.disabled = true;
      status('Applying confirmed changes…');
      const res = await fetch('/api/room-excel-roundtrip/import-apply?token=' + encodeURIComponent(token), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ previewToken: activeImportPreviewToken })
      });
      const data = await tryJson(res);
      btnApplyImport.disabled = false;
      if (!res.ok || !data.ok) {
        if (String(data.code || '') === 'REJECTED_BY_REVIT') {
          btnQueueImport.style.display = '';
          queueHint((data.msg || 'Revit 側で拒否されました。') + ' 下の「キューに入れる」を押すと、5分後に Revit 側で再確認します。');
          status((data.msg || 'Revit 側で拒否されました。') + ' 下のボタンから次の操作を選んでください。', true);
          return;
        }
        status(data.msg || ('HTTP ' + res.status), true);
        return;
      }
      renderImportResult(data);
      activeImportPreviewToken = '';
      activeQueueId = '';
      activeImportedFilePath = data.path || activeImportedFilePath;
      setImportMode(false);
      status(`Imported: changed=${data.changedCount || 0}, unchanged=${data.unchangedCount || 0}, skipped=${data.skippedCount || 0}, failed=${data.failedCount || 0}`);
    });

    btnVerifyImport.addEventListener('click', async () => {
      if (!activeImportedFilePath) {
        status('確認対象の Excel ファイルがありません。再度プレビューしてください。', true);
        return;
      }
      btnVerifyImport.disabled = true;
      status('現在の Revit 値と再比較しています…');
      const res = await fetch('/api/room-excel-roundtrip/import-verify?token=' + encodeURIComponent(token), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ filePath: activeImportedFilePath })
      });
      const data = await tryJson(res);
      btnVerifyImport.disabled = false;
      if (!res.ok || !data.ok) {
        status(data.msg || ('HTTP ' + res.status), true);
        return;
      }
      renderImportPreview(data, true);
      await renderVerifiedSchedulePreview(data);
      if (data.reflected) {
        status('反映確認: Excel の内容は現在の Revit 集計表に反映されています。');
      } else {
        status(`反映確認: まだ差分があります。changed=${data.changedCellCount || 0}, failed=${data.failedCellCount || 0}`, true);
      }
    });

    btnQueueImport.addEventListener('click', async () => {
      if (!activeImportPreviewToken) {
        status('No preview session. Upload and preview the workbook again.', true);
        return;
      }
      btnQueueImport.disabled = true;
      const res = await fetch('/api/room-excel-roundtrip/import-queue?token=' + encodeURIComponent(token), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ previewToken: activeImportPreviewToken })
      });
      const data = await tryJson(res);
      btnQueueImport.disabled = false;
      if (!res.ok || !data.ok) {
        status(data.msg || ('HTTP ' + res.status), true);
        return;
      }
      activeQueueId = data.queueId || '';
      btnDeleteQueue.style.display = activeQueueId ? '' : 'none';
      queueHint('キューに入れました。Revit 側では 5 分ごとに確認ダイアログを表示します。不要になった場合は「キューを削除」を押してください。');
      status(data.msg || 'キューに入れました。後でRevit に反映されます。');
    });

    btnDeleteQueue.addEventListener('click', async () => {
      if (!activeQueueId) {
        status('No queued request.', true);
        return;
      }
      btnDeleteQueue.disabled = true;
      const res = await fetch('/api/room-excel-roundtrip/queue-delete?token=' + encodeURIComponent(token), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ queueId: activeQueueId })
      });
      const data = await tryJson(res);
      btnDeleteQueue.disabled = false;
      if (!res.ok || !data.ok) {
        status(data.msg || ('HTTP ' + res.status), true);
        return;
      }
      activeQueueId = '';
      btnDeleteQueue.style.display = 'none';
      btnQueueImport.style.display = 'none';
      queueHint('');
      status(data.msg || 'キューを削除しました。反映しなかったことを送信者に別途連絡してください。', true);
    });

    btnBack.addEventListener('click', () => {
      activeImportPreviewToken = '';
      activeQueueId = '';
      activeImportedFilePath = '';
      setImportMode(false);
      btnQueueImport.style.display = 'none';
      btnDeleteQueue.style.display = 'none';
      queueHint('');
      summary.innerHTML = '';
      tblHead.innerHTML = '';
      tblBody.innerHTML = '';
      clearAfterSchedulePreview();
      status('');
    });

    setImportMode(false);
    clearAfterSchedulePreview();
    loadSchedules();

    function renderImportResult(data) {
      summary.innerHTML = `<p><b>Mode:</b> ${escapeHtml(data.mode || '')} / <b>Changed:</b> ${data.changedCount || data.updatedCount || 0} / <b>Unchanged:</b> ${data.unchangedCount || 0} / <b>Skipped:</b> ${data.skippedCount || 0} / <b>Failed:</b> ${data.failedCount || 0} / <b>Editable columns:</b> ${data.editableColumnCount || 0}</p>` +
        (data.reportPath ? `<p><b>Report:</b> <span class="mono">${escapeHtml(data.reportPath)}</span></p>` : '') +
        (data.auditJsonPath ? `<p><b>Change JSON:</b> <span class="mono">${escapeHtml(data.auditJsonPath)}</span></p>` : '');
      tblHead.innerHTML = '<tr><th>Item</th><th>Value</th></tr>';
      tblBody.innerHTML = '';
      [
        ['Imported file', data.path || ''],
        ['Report path', data.reportPath || ''],
        ['Change JSON', data.auditJsonPath || ''],
        ['Changed count', String(data.changedCount || data.updatedCount || 0)],
        ['Unchanged count', String(data.unchangedCount || 0)],
        ['Skipped count', String(data.skippedCount || 0)],
        ['Failed count', String(data.failedCount || 0)]
      ].forEach(r => {
        const tr = document.createElement('tr');
        tr.innerHTML = `<td>${escapeHtml(r[0])}</td><td class="mono">${escapeHtml(r[1])}</td>`;
        tblBody.appendChild(tr);
      });
      updateVerifyButton();
    }

    function clearAfterSchedulePreview() {
      afterSchedulePreviewSummary.innerHTML = '<p class="mono">Apply 後に「反映確認」を押すと、変更後の集計表をここに表示します。</p>';
      afterSchedulePreviewHead.innerHTML = '';
      afterSchedulePreviewBody.innerHTML = '';
    }

    function renderImportPreview(data, isVerification = false) {
      summary.innerHTML = (isVerification
        ? `<p><b>Verification:</b> ${data.reflected ? '<span class="ok">Reflected</span>' : '<span class="err">Differences remain</span>'}</p>`
        : `<p><b>Preview token:</b> <span class="mono">${escapeHtml(data.previewToken || '')}</span></p>`) +
        `<p><b>Mode:</b> ${escapeHtml(data.mode || '')} / <b>Changed cells:</b> ${data.changedCellCount || 0} / <b>Unchanged cells:</b> ${data.unchangedCellCount || 0} / <b>Skipped cells:</b> ${data.skippedCellCount || 0} / <b>Failed cells:</b> ${data.failedCellCount || 0}</p>` +
        ((data.uploadedPath || data.filePath) ? `<p><b>${isVerification ? 'Verification workbook' : 'Uploaded workbook'}:</b> <span class="mono">${escapeHtml(data.uploadedPath || data.filePath || '')}</span></p>` : '');

      tblHead.innerHTML = '<tr><th>Excel Row</th><th>Item</th><th>ElementId</th><th>Parameter</th><th>Before</th><th>After</th><th>Status</th><th>Message</th></tr>';
      tblBody.innerHTML = '';

      const rows = Array.isArray(data.rows) ? data.rows : [];
      rows.forEach(r => {
        const cells = Array.isArray(r.cells) ? r.cells : [];
        cells.forEach(c => {
          const elements = Array.isArray(c.elements) ? c.elements : [];
          elements.forEach(e => {
            if (String(e.status || '').toUpperCase() === 'UNCHANGED') return;
            const tr = document.createElement('tr');
            tr.innerHTML =
              `<td>${escapeHtml(r.row)}</td>` +
              `<td>${escapeHtml(r.label || '')}</td>` +
              `<td class="mono">${escapeHtml(e.elementId || '')}</td>` +
              `<td>${escapeHtml(c.parameterName || c.header || '')}</td>` +
              `<td>${escapeHtml(e.before || '')}</td>` +
              `<td>${escapeHtml(e.after || e.imported || '')}</td>` +
              `<td>${escapeHtml(e.status || c.status || '')}</td>` +
              `<td>${escapeHtml(e.message || c.message || r.message || '')}</td>`;
            tblBody.appendChild(tr);
          });
        });
      });

      if (!tblBody.children.length) {
        const tr = document.createElement('tr');
        tr.innerHTML = `<td colspan="8">${isVerification ? 'No remaining differences detected.' : 'No changed cells detected.'}</td>`;
        tblBody.appendChild(tr);
      }
    }

    function setImportMode(isPreview) {
      btnPreviewImport.style.display = isPreview ? 'none' : '';
      btnBack.style.display = isPreview ? '' : 'none';
      btnApplyImport.style.display = isPreview ? '' : 'none';
      btnQueueImport.style.display = 'none';
      btnDeleteQueue.style.display = activeQueueId ? '' : 'none';
      btnVerifyImport.style.display = isPreview ? 'none' : (activeImportedFilePath ? '' : 'none');
      if (!isPreview) queueHint('');
      fileInput.disabled = !!isPreview;
    }

    function updateVerifyButton() {
      btnVerifyImport.style.display = activeImportedFilePath ? '' : 'none';
    }

    function status(text, isErr = false) {
      statusEl.className = 'mono ' + (isErr ? 'err' : '');
      statusEl.textContent = text || '';
    }

    function queueHint(text) {
      if (!queueHintEl) return;
      queueHintEl.textContent = text || '';
      queueHintEl.style.display = text ? '' : 'none';
    }

    function scheduleStatus(text, isErr = false) {
      scheduleStatusEl.className = 'mono ' + (isErr ? 'err' : '');
      scheduleStatusEl.textContent = text || '';
    }

    async function loadSchedules() {
      scheduleStatus('Loading schedules…');
      scheduleSelect.innerHTML = '<option value=\"\">Loading schedules…</option>';
      const res = await fetch('/api/room-excel-roundtrip/schedules?token=' + encodeURIComponent(token));
      const data = await tryJson(res);
      if (!res.ok || !data.ok) {
        scheduleSelect.innerHTML = '<option value=\"\">Schedules unavailable</option>';
        scheduleStatus(data.msg || ('HTTP ' + res.status), true);
        return;
      }

      const schedules = Array.isArray(data.schedules) ? data.schedules : [];
      if (!schedules.length) {
        scheduleSelect.innerHTML = '<option value=\"\">No schedules found</option>';
        scheduleStatus('No schedules found.', true);
        return;
      }

      scheduleSelect.innerHTML = '';
      schedules.forEach(s => {
        const opt = document.createElement('option');
        opt.value = String(s.scheduleViewId || '');
        const suffix = s.categoryName ? ` [${s.categoryName}]` : '';
        opt.textContent = `${s.title || '(untitled)'}${suffix}`;
        if (s.isActive) opt.selected = true;
        scheduleSelect.appendChild(opt);
      });

      if (!scheduleSelect.value && scheduleSelect.options.length > 0)
        scheduleSelect.selectedIndex = 0;

      scheduleStatus(`Loaded ${schedules.length} schedules.`);
    }

    function renderSchedulePreview(data) {
      activeSchedulePreviewData = cloneSchedulePreviewData(data);
      renderSchedulePreviewInto(schedulePreviewSummary, schedulePreviewHead, schedulePreviewBody, data, null, 'Selected Schedule');
    }

    function renderSchedulePreviewInto(summaryEl, headEl, bodyEl, data, changedCellKeys, title) {
      const columns = Array.isArray(data.columns) ? data.columns : [];
      const rows = Array.isArray(data.rows) ? data.rows : [];
      const scheduleName = data.scheduleName || scheduleSelect.options[scheduleSelect.selectedIndex]?.text || '';
      summaryEl.innerHTML =
        `<p><b>${escapeHtml(title || 'Schedule')}:</b> ${escapeHtml(scheduleName)}</p>` +
        `<p><b>Rows shown:</b> ${data.shownCount || rows.length} / <b>Total rows:</b> ${data.totalCount || rows.length}` +
        (data.truncated ? ' <span class="warnText">(truncated)</span>' : '') +
        (changedCellKeys ? ` / <b>Highlighted changed cells:</b> ${changedCellKeys.size}` : '') + '</p>';
      headEl.innerHTML = '';
      bodyEl.innerHTML = '';

      const headTr = document.createElement('tr');
      columns.forEach(col => {
        const th = document.createElement('th');
        th.textContent = col;
        headTr.appendChild(th);
      });
      headEl.appendChild(headTr);

      rows.forEach((row, index) => {
        const excelRow = index + 2;
        const tr = document.createElement('tr');
        columns.forEach(col => {
          const td = document.createElement('td');
          td.textContent = String((row && row[col]) ?? '');
          if (changedCellKeys && changedCellKeys.has(buildChangedCellKey(excelRow, col)))
            td.classList.add('changedCell');
          tr.appendChild(td);
        });
        bodyEl.appendChild(tr);
      });

      if (!bodyEl.children.length) {
        const tr = document.createElement('tr');
        tr.innerHTML = `<td colspan="${Math.max(columns.length, 1)}">No schedule rows returned.</td>`;
        bodyEl.appendChild(tr);
      }
    }

    function buildChangedCellKey(rowNumber, header) {
      return String(rowNumber) + '|' + String(header || '').trim().toLowerCase();
    }

    function buildChangedCellKeySet(rows) {
      const keys = new Set();
      const srcRows = Array.isArray(rows) ? rows : [];
      srcRows.forEach(r => {
        const rowNumber = Number(r?.row || 0);
        if (!rowNumber) return;
        const cells = Array.isArray(r?.cells) ? r.cells : [];
        cells.forEach(c => {
          const header = String(c?.header || c?.parameterName || '').trim();
          if (!header) return;
          const elements = Array.isArray(c?.elements) ? c.elements : [];
          if (elements.some(e => String(e?.status || '').toUpperCase() === 'CHANGED'))
            keys.add(buildChangedCellKey(rowNumber, header));
        });
      });
      return keys;
    }

    async function renderVerifiedSchedulePreview(verifyData) {
      if (!activeSchedulePreviewData) {
        clearAfterSchedulePreview();
        afterSchedulePreviewSummary.innerHTML = '<p class="err">変更前の集計表プレビューがありません。先に「Preview Selected Schedule」を実行してください。</p>';
        return;
      }
      const afterData = cloneSchedulePreviewData(activeSchedulePreviewData);
      applyVerificationToScheduleData(afterData, verifyData.rows);
      renderSchedulePreviewInto(afterSchedulePreviewSummary, afterSchedulePreviewHead, afterSchedulePreviewBody, afterData, activeChangedCellKeys, 'Schedule After Apply');
    }

    function cloneSchedulePreviewData(data) {
      const columns = Array.isArray(data?.columns) ? [...data.columns] : [];
      const rows = Array.isArray(data?.rows) ? data.rows.map(r => ({ ...(r || {}) })) : [];
      return {
        scheduleViewId: data?.scheduleViewId || '',
        scheduleName: data?.scheduleName || '',
        columns,
        rows,
        totalCount: data?.totalCount || rows.length,
        shownCount: data?.shownCount || rows.length,
        truncated: !!data?.truncated
      };
    }

    function applyVerificationToScheduleData(scheduleData, verifyRows) {
      const rows = Array.isArray(verifyRows) ? verifyRows : [];
      rows.forEach(r => {
        const rowNumber = Number(r?.row || 0);
        if (!rowNumber || rowNumber < 2) return;
        const rowIndex = rowNumber - 2;
        if (!Array.isArray(scheduleData.rows) || rowIndex < 0 || rowIndex >= scheduleData.rows.length) return;
        const targetRow = scheduleData.rows[rowIndex];
        const cells = Array.isArray(r?.cells) ? r.cells : [];
        cells.forEach(c => {
          const header = String(c?.header || c?.parameterName || '').trim();
          if (!header) return;
          const currentValue = pickVerificationDisplayValue(c);
          if (currentValue === null) return;
          targetRow[header] = currentValue;
        });
      });
    }

    function pickVerificationDisplayValue(cell) {
      const elements = Array.isArray(cell?.elements) ? cell.elements : [];
      if (elements.length > 0) {
        const first = elements.find(e => e && e.before !== undefined && e.before !== null);
        if (first) return String(first.before || '');
      }
      if (cell && cell.importedValue !== undefined && cell.importedValue !== null)
        return String(cell.importedValue || '');
      return null;
    }

    async function downloadResponseBlob(res, fallbackName) {
      const blob = await res.blob();
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      const cd = res.headers.get('Content-Disposition') || '';
      a.download = getDownloadFileName(cd, fallbackName);
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(a.href), 1000);
    }

    function getDownloadFileName(contentDisposition, fallbackName) {
      const cd = String(contentDisposition || '');

      const star = /filename\*\s*=\s*([^;]+)/i.exec(cd);
      if (star && star[1]) {
        let value = star[1].trim();
        value = value.replace(/^UTF-8''/i, '').replace(/^"(.*)"$/, '$1');
        try {
          return decodeURIComponent(value);
        } catch {
          return value;
        }
      }

      const normal = /filename\s*=\s*"([^"]+)"/i.exec(cd) || /filename\s*=\s*([^;]+)/i.exec(cd);
      if (normal && normal[1]) {
        return String(normal[1]).trim().replace(/^"(.*)"$/, '$1');
      }

      return fallbackName;
    }

    function escapeHtml(s) {
      return String(s || '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] || c));
    }

    async function tryJson(res) {
      try { return await res.json(); } catch { return {}; }
    }
  </script>
</body>
</html>
""";
        }

        private static byte[] BuildExportWorkbook(List<RoomSnapshot> rooms, AccessTokenPayload token)
        {
            using (var package = new ExcelPackage())
            {
                var ws = package.Workbook.Worksheets.Add(DataSheetName);
                var headers = new List<string> { "RoomUniqueId", "ElementId", "Level", "Number", "Name" };
                headers.AddRange(token.ParamNames);

                for (int i = 0; i < headers.Count; i++)
                {
                    ws.Cells[1, i + 1].Value = headers[i];
                    ws.Cells[1, i + 1].Style.Font.Bold = true;
                    ws.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    ws.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(230, 238, 247));
                }

                var row = 2;
                var booleanColumns = new HashSet<int>();
                foreach (var room in rooms.OrderBy(x => x.LevelName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Number, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    ws.Cells[row, 1].Value = room.UniqueId;
                    ws.Cells[row, 2].Value = room.ElementId;
                    ws.Cells[row, 3].Value = room.LevelName;
                    ws.Cells[row, 4].Value = room.Number;
                    ws.Cells[row, 5].Value = room.Name;
                    for (int i = 0; i < token.ParamNames.Count; i++)
                    {
                        room.Params.TryGetValue(token.ParamNames[i], out var param);
                        var colIndex = i + 6;
                        ws.Cells[row, colIndex].Value = FormatExportValue(param);
                        if (param != null && IsBooleanLike(param))
                            booleanColumns.Add(colIndex);
                    }
                    row++;
                }

                ws.View.FreezePanes(2, 1);
                if (ws.Dimension != null)
                {
                    ws.Cells[ws.Dimension.Address].AutoFitColumns();
                    foreach (var colIndex in booleanColumns)
                    {
                        var col = ws.Column(colIndex);
                        col.Width = 4.2d;
                        col.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        if (row > 2)
                        {
                            var validation = ws.DataValidations.AddListValidation(ExcelCellBase.GetAddress(2, colIndex, row - 1, colIndex));
                            validation.ShowErrorMessage = true;
                            validation.ErrorTitle = "☑ / ☐";
                            validation.Error = "☑ または ☐ を入力してください。";
                            validation.Formula.Values.Add("☑");
                            validation.Formula.Values.Add("☐");
                        }
                    }
                }

                var meta = package.Workbook.Worksheets.Add(MetadataSheetName);
                meta.Hidden = eWorkSheetHidden.Hidden;
                meta.Cells[1, 1].Value = "SchemaVersion";
                meta.Cells[1, 2].Value = "room_excel_roundtrip.v1";
                meta.Cells[2, 1].Value = "DocGuid";
                meta.Cells[2, 2].Value = token.DocGuid;
                meta.Cells[3, 1].Value = "DocTitle";
                meta.Cells[3, 2].Value = token.DocTitle;
                meta.Cells[4, 1].Value = "ExportedAtUtc";
                meta.Cells[4, 2].Value = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                meta.Cells[5, 1].Value = "ParamNamesJson";
                meta.Cells[5, 2].Value = JsonSerializer.Serialize(token.ParamNames, JsonOptions);
                meta.Cells[6, 1].Value = "PermissionsJson";
                meta.Cells[6, 2].Value = JsonSerializer.Serialize(token.Permissions, JsonOptions);
                return package.GetAsByteArray();
            }
        }

        private static ParsedWorkbook ParseWorkbook(byte[] bytes)
        {
            using (var package = new ExcelPackage(new MemoryStream(bytes)))
            {
                var meta = package.Workbook.Worksheets.FirstOrDefault(x => string.Equals(x.Name, MetadataSheetName, StringComparison.OrdinalIgnoreCase));
                if (meta == null) throw new InvalidOperationException("Metadata sheet not found.");

                var docGuid = meta.Cells[2, 2].Text?.Trim() ?? string.Empty;
                var docTitle = meta.Cells[3, 2].Text?.Trim() ?? string.Empty;
                var paramNamesJson = meta.Cells[5, 2].Text?.Trim() ?? "[]";
                var paramNames = JsonSerializer.Deserialize<List<string>>(paramNamesJson, JsonOptions) ?? new List<string>();
                var ws = package.Workbook.Worksheets.FirstOrDefault(x => string.Equals(x.Name, DataSheetName, StringComparison.OrdinalIgnoreCase))
                         ?? package.Workbook.Worksheets.FirstOrDefault(x => !string.Equals(x.Name, MetadataSheetName, StringComparison.OrdinalIgnoreCase));
                if (ws == null) throw new InvalidOperationException("Data sheet not found.");

                var rows = new List<ImportedRoomRow>();
                var lastRow = ws.Dimension?.End.Row ?? 1;
                for (int row = 2; row <= lastRow; row++)
                {
                    var uniqueId = (ws.Cells[row, 1].Text ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(uniqueId)) continue;

                    var imported = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < paramNames.Count; i++)
                        imported[paramNames[i]] = ws.Cells[row, i + 6].Text ?? string.Empty;

                    rows.Add(new ImportedRoomRow
                    {
                        RowNumber = row,
                        RoomUniqueId = uniqueId,
                        ElementIdText = (ws.Cells[row, 2].Text ?? string.Empty).Trim(),
                        Level = (ws.Cells[row, 3].Text ?? string.Empty).Trim(),
                        Number = (ws.Cells[row, 4].Text ?? string.Empty).Trim(),
                        Name = (ws.Cells[row, 5].Text ?? string.Empty).Trim(),
                        ImportedValues = imported
                    });
                }

                return new ParsedWorkbook { DocGuid = docGuid, DocTitle = docTitle, ParamNames = paramNames, Rows = rows };
            }
        }

        private static async Task<List<RoomSnapshot>> LoadRoomSnapshotAsync(DurableQueue durable, JobIndex index, AccessTokenPayload token)
        {
            var payload = await InvokeAddinAsync(durable, index, "get_spatial_params_bulk", new { docGuid = token.DocGuid, docTitle = token.DocTitle, kind = "room", all = true }, timeoutSeconds: 180);
            var root = payload as JsonObject ?? throw new InvalidOperationException("Invalid snapshot payload.");
            if (root["ok"]?.GetValue<bool>() != true)
                throw new InvalidOperationException(root["msg"]?.GetValue<string>() ?? "get_spatial_params_bulk failed.");

            var items = root["items"] as JsonArray ?? new JsonArray();
            var rooms = new List<RoomSnapshot>(items.Count);
            foreach (var node in items.OfType<JsonObject>())
            {
                var room = new RoomSnapshot
                {
                    ElementId = node["elementId"]?.GetValue<int>() ?? 0,
                    UniqueId = node["uniqueId"]?.GetValue<string>() ?? string.Empty,
                    Name = node["name"]?.GetValue<string>() ?? string.Empty,
                    LevelName = node["levelName"]?.GetValue<string>() ?? string.Empty
                };

                var paramsArray = node["parameters"] as JsonArray ?? new JsonArray();
                foreach (var p in paramsArray.OfType<JsonObject>())
                {
                    var paramName = p["name"]?.GetValue<string>() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(paramName)) continue;
                    room.Params[paramName] = new RoomParamSnapshot
                    {
                        Name = paramName,
                        StorageType = p["storageType"]?.GetValue<string>() ?? string.Empty,
                        DataType = p["dataType"]?.GetValue<string>() ?? string.Empty,
                        IsReadOnly = p["isReadOnly"]?.GetValue<bool>() ?? false,
                        Display = p["display"]?.GetValue<string>() ?? string.Empty,
                        ValueNode = p["value"]?.DeepClone()
                    };
                }

                room.Number = TryResolveRoomNumber(room.Params);
                rooms.Add(room);
            }
            return rooms;
        }

        private static string TryResolveRoomNumber(Dictionary<string, RoomParamSnapshot> map)
        {
            foreach (var key in new[] { "Number", "番号", "Room Number", "部屋番号" })
            {
                if (map.TryGetValue(key, out var snap))
                    return snap.Display ?? string.Empty;
            }
            return string.Empty;
        }

        private static PreviewSession BuildPreview(AccessTokenPayload token, ParsedWorkbook workbook, List<RoomSnapshot> snapshot)
        {
            var snapshotByUniqueId = snapshot.ToDictionary(x => x.UniqueId, StringComparer.OrdinalIgnoreCase);
            var rows = new List<PreviewRow>();

            foreach (var importedRow in workbook.Rows)
            {
                if (!snapshotByUniqueId.TryGetValue(importedRow.RoomUniqueId, out var room))
                {
                    rows.Add(new PreviewRow { RowNumber = importedRow.RowNumber, RoomUniqueId = importedRow.RoomUniqueId, RoomName = importedRow.Name, RoomNumber = importedRow.Number, Level = importedRow.Level, Status = "NOT_FOUND", Message = "Room uniqueId not found in current document." });
                    continue;
                }

                var previewRow = new PreviewRow { RowNumber = importedRow.RowNumber, RoomUniqueId = room.UniqueId, RoomName = room.Name, RoomNumber = room.Number, Level = room.LevelName, Status = "UNCHANGED", Message = string.Empty };
                foreach (var paramName in workbook.ParamNames)
                {
                    room.Params.TryGetValue(paramName, out var current);
                    importedRow.ImportedValues.TryGetValue(paramName, out var importedText);
                    previewRow.Cells.Add(BuildPreviewCell(paramName, importedText ?? string.Empty, current));
                }

                if (previewRow.Cells.Any(c => string.Equals(c.Status, "CONFIRM_REQUIRED", StringComparison.OrdinalIgnoreCase)))
                {
                    previewRow.Status = "CONFIRM_REQUIRED";
                    previewRow.Message = "Some values need confirmation before apply.";
                }
                else if (previewRow.Cells.Any(c => string.Equals(c.Status, "READY", StringComparison.OrdinalIgnoreCase)))
                {
                    previewRow.Status = "READY";
                }
                else if (previewRow.Cells.All(c => string.Equals(c.Status, "READ_ONLY", StringComparison.OrdinalIgnoreCase)))
                {
                    previewRow.Status = "READ_ONLY";
                    previewRow.Message = "All edited columns are read-only.";
                }
                else if (previewRow.Cells.All(c => string.Equals(c.Status, "NOT_FOUND", StringComparison.OrdinalIgnoreCase)))
                {
                    previewRow.Status = "NOT_FOUND";
                    previewRow.Message = "All target parameters were missing.";
                }
                else
                {
                    previewRow.Status = "UNCHANGED";
                    previewRow.Message = "No changes detected.";
                }

                rows.Add(previewRow);
            }

            return new PreviewSession
            {
                PreviewToken = Guid.NewGuid().ToString("N"),
                DocGuid = token.DocGuid,
                DocTitle = token.DocTitle,
                CreatedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2),
                ParamNames = workbook.ParamNames,
                Rows = rows
            };
        }

        private static PreviewCell BuildPreviewCell(string paramName, string importedText, RoomParamSnapshot? current)
        {
            if (current == null)
                return new PreviewCell { ParamName = paramName, Status = "NOT_FOUND", CurrentDisplay = string.Empty, ImportedDisplay = importedText, CurrentComparable = string.Empty, Message = "Parameter not found on room." };
            if (current.IsReadOnly)
                return new PreviewCell { ParamName = paramName, Status = "READ_ONLY", CurrentDisplay = current.Display, ImportedDisplay = importedText, CurrentComparable = BuildComparableValue(current), Message = "Parameter is read-only." };

            if (TryNormalizeImportedValue(importedText, current, out var typedValue, out var comparable, out var status, out var message))
            {
                var currentComparable = BuildComparableValue(current);
                if (string.Equals(currentComparable, comparable, StringComparison.Ordinal))
                    return new PreviewCell { ParamName = paramName, Status = "UNCHANGED", CurrentDisplay = current.Display, ImportedDisplay = importedText, CurrentComparable = currentComparable, TypedValue = typedValue, Message = "No change." };
                return new PreviewCell { ParamName = paramName, Status = status, CurrentDisplay = current.Display, ImportedDisplay = importedText, CurrentComparable = currentComparable, TypedValue = typedValue, Message = message };
            }

            return new PreviewCell { ParamName = paramName, Status = status, CurrentDisplay = current.Display, ImportedDisplay = importedText, CurrentComparable = BuildComparableValue(current), Message = message };
        }

        private static bool TryNormalizeImportedValue(string text, RoomParamSnapshot current, out object? typedValue, out string comparable, out string status, out string message)
        {
            typedValue = null;
            comparable = string.Empty;
            status = "READY";
            message = "Ready to apply.";
            var raw = (text ?? string.Empty).Trim();

            if (string.Equals(current.StorageType, "String", StringComparison.OrdinalIgnoreCase))
            {
                typedValue = raw;
                comparable = raw;
                return true;
            }

            if (string.Equals(current.StorageType, "ElementId", StringComparison.OrdinalIgnoreCase))
            {
                status = "CONFIRM_REQUIRED";
                message = "ElementId parameters are not supported in v1.";
                return false;
            }

            if (string.Equals(current.StorageType, "Integer", StringComparison.OrdinalIgnoreCase))
            {
                if (IsBooleanLike(current))
                {
                    if (TryParseBoolean(raw, out var boolValue))
                    {
                        typedValue = boolValue;
                        comparable = boolValue ? "true" : "false";
                        return true;
                    }
                    status = "CONFIRM_REQUIRED";
                    message = "Expected a Yes/No style value.";
                    return false;
                }

                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                {
                    typedValue = iv;
                    comparable = iv.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

                if (double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var dv)
                    && Math.Abs(dv - Math.Round(dv)) < 0.0000001d)
                {
                    var normalized = Convert.ToInt32(Math.Round(dv));
                    typedValue = normalized;
                    comparable = normalized.ToString(CultureInfo.InvariantCulture);
                    status = "CONFIRM_REQUIRED";
                    message = "Numeric input was normalized to an integer. Confirm before apply.";
                    return true;
                }

                status = "CONFIRM_REQUIRED";
                message = "Expected an integer value.";
                return false;
            }

            if (string.Equals(current.StorageType, "Double", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var d)
                    || double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out d))
                {
                    typedValue = d;
                    comparable = d.ToString("0.###############", CultureInfo.InvariantCulture);
                    return true;
                }

                status = "CONFIRM_REQUIRED";
                message = "Expected a numeric value.";
                return false;
            }

            status = "CONFIRM_REQUIRED";
            message = $"Unsupported storage type '{current.StorageType}'.";
            return false;
        }

        private static string FormatExportValue(RoomParamSnapshot? param)
        {
            if (param == null) return string.Empty;
            if (IsBooleanLike(param))
            {
                var current = BuildComparableValue(param);
                return string.Equals(current, "true", StringComparison.OrdinalIgnoreCase) ? "☑" : "☐";
            }
            if (!string.IsNullOrWhiteSpace(param.Display)) return param.Display;
            return param.ValueNode?.ToJsonString() ?? string.Empty;
        }

        private static bool IsBooleanLike(RoomParamSnapshot param)
        {
            if (!string.Equals(param.StorageType, "Integer", StringComparison.OrdinalIgnoreCase)) return false;
            var display = (param.Display ?? string.Empty).Trim();
            if (Truthy.Contains(display, StringComparer.OrdinalIgnoreCase) || Falsey.Contains(display, StringComparer.OrdinalIgnoreCase)) return true;
            var raw = param.ValueNode?.ToJsonString().Trim('"');
            return raw == "0" || raw == "1";
        }

        private static bool TryParseBoolean(string raw, out bool value)
        {
            var s = (raw ?? string.Empty).Trim();
            if (Truthy.Contains(s, StringComparer.OrdinalIgnoreCase)) { value = true; return true; }
            if (Falsey.Contains(s, StringComparer.OrdinalIgnoreCase)) { value = false; return true; }
            value = false;
            return false;
        }

        private static string BuildComparableValue(RoomParamSnapshot param)
        {
            if (param.ValueNode == null) return string.Empty;
            if (IsBooleanLike(param))
            {
                var raw = param.ValueNode.ToJsonString().Trim('"');
                return raw == "1" ? "true" : "false";
            }
            if (string.Equals(param.StorageType, "String", StringComparison.OrdinalIgnoreCase)) return param.Display ?? string.Empty;
            return param.ValueNode.ToJsonString().Trim('"');
        }

        private static async Task<JsonNode?> InvokeAddinAsync(DurableQueue durable, JobIndex index, string method, object payload, int timeoutSeconds)
        {
            var rpcId = Guid.NewGuid().ToString("N");
            var paramsJson = JsonSerializer.Serialize(payload, JsonOptions);
            var jobId = await durable.EnqueueAsync(method, paramsJson, null, rpcId, 100, timeoutSeconds);
            index.Put(rpcId, jobId);

            var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds + 5);
            while (DateTimeOffset.UtcNow < deadline)
            {
                var row = await durable.GetAsync(jobId);
                if (row is IDictionary<string, object?> dict)
                {
                    var state = Convert.ToString(dict.TryGetValue("state", out var stateObj) ? stateObj : null) ?? string.Empty;
                    if (string.Equals(state, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                    {
                        var resultJson = Convert.ToString(dict.TryGetValue("result_json", out var resultObj) ? resultObj : null);
                        return McpAdapter.UnwrapRpcResult(resultJson);
                    }

                    if (string.Equals(state, "FAILED", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(state, "TIMEOUT", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(state, "DEAD", StringComparison.OrdinalIgnoreCase))
                    {
                        var code = Convert.ToString(dict.TryGetValue("error_code", out var codeObj) ? codeObj : null) ?? "ERROR";
                        var msg = Convert.ToString(dict.TryGetValue("error_msg", out var msgObj) ? msgObj : null) ?? state;
                        throw new InvalidOperationException($"{method} failed ({code}): {msg}");
                    }
                }

                await Task.Delay(250);
            }

            throw new TimeoutException($"{method} timed out.");
        }

        private static bool TryReadAccessToken(HttpRequest req, out AccessTokenPayload? payload, out string? error, string? requiredPermission = null)
        {
            payload = null;
            error = null;

            var token = (req.Query["token"].ToString() ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(token))
                token = (req.Headers["X-Room-Roundtrip-Token"].ToString() ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                error = "token is required.";
                return false;
            }
            if (!TryVerifySignedToken(token, out payload, out error))
                return false;
            if (!string.IsNullOrWhiteSpace(requiredPermission)
                && !payload!.Permissions.Contains(requiredPermission, StringComparer.OrdinalIgnoreCase))
            {
                error = $"Missing permission '{requiredPermission}'.";
                return false;
            }
            return true;
        }

        private static bool IsLoopbackRequest(HttpRequest req)
        {
            try
            {
                var ip = req.HttpContext?.Connection?.RemoteIpAddress;
                if (ip == null) return false;
                if (IPAddress.IsLoopback(ip)) return true;
                if (ip.IsIPv4MappedToIPv6) return IPAddress.IsLoopback(ip.MapToIPv4());
            }
            catch { /* ignore */ }
            return false;
        }

        private static string ResolvePublicBaseUrl(HttpRequest req)
        {
            var requested = (req.Query["baseUrl"].ToString() ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(requested)
                && Uri.TryCreate(requested, UriKind.Absolute, out var uri)
                && (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
            {
                return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
            }

            return $"{req.Scheme}://{req.Host}";
        }

        private static string CreateSignedToken(AccessTokenPayload payload)
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var data = Base64UrlEncode(Encoding.UTF8.GetBytes(json));
            var sig = ComputeSignature(data);
            return $"{data}.{sig}";
        }

        private static bool TryVerifySignedToken(string token, out AccessTokenPayload? payload, out string? error)
        {
            payload = null;
            error = null;

            var parts = token.Split('.');
            if (parts.Length != 2)
            {
                error = "Malformed token.";
                return false;
            }

            var expected = ComputeSignature(parts[0]);
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(parts[1]), Encoding.UTF8.GetBytes(expected)))
            {
                error = "Invalid token signature.";
                return false;
            }

            try
            {
                var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
                payload = JsonSerializer.Deserialize<AccessTokenPayload>(json, JsonOptions);
                if (payload == null)
                {
                    error = "Invalid token payload.";
                    return false;
                }
                if (payload.ExpiresUtc <= DateTimeOffset.UtcNow)
                {
                    error = "Token expired.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(payload.DocGuid))
                {
                    error = "Token docGuid missing.";
                    return false;
                }
                payload.ParamNames ??= new List<string>();
                payload.Permissions ??= DefaultPermissions.ToList();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string ComputeSignature(string data)
        {
            using (var hmac = new HMACSHA256(GetOrCreateSecret()))
            {
                return Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(data)));
            }
        }

        private static byte[] GetOrCreateSecret()
        {
            var configured = Environment.GetEnvironmentVariable("REVIT_MCP_ROOM_RT_SECRET");
            if (!string.IsNullOrWhiteSpace(configured))
                return Encoding.UTF8.GetBytes(configured);

            var dir = Path.Combine(AppContext.BaseDirectory, "Results");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "room_roundtrip_secret.txt");
            if (File.Exists(path))
                return Encoding.UTF8.GetBytes(File.ReadAllText(path, Encoding.UTF8));

            var secret = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            File.WriteAllText(path, secret, Encoding.UTF8);
            return Encoding.UTF8.GetBytes(secret);
        }

        private static void CleanupExpiredPreviewSessions()
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var kv in PreviewSessions)
            {
                if (kv.Value.ExpiresUtc <= now)
                    PreviewSessions.TryRemove(kv.Key, out _);
            }

            foreach (var kv in ScheduleImportPreviewSessions)
            {
                if (kv.Value.ExpiresUtc <= now)
                {
                    if (ScheduleImportPreviewSessions.TryRemove(kv.Key, out var removed))
                        TryDeleteFile(removed?.UploadedFilePath);
                }
            }
        }

        private static List<string> ParseStringList(string raw)
        {
            return (raw ?? string.Empty)
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsSupportedExcelExtension(string fileName)
        {
            var ext = Path.GetExtension(fileName ?? string.Empty);
            return string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(ext, ".xltx", StringComparison.OrdinalIgnoreCase);
        }

        private static string SanitizeFileName(string? value)
        {
            var text = string.IsNullOrWhiteSpace(value) ? "room_roundtrip" : value!;
            foreach (var ch in Path.GetInvalidFileNameChars())
                text = text.Replace(ch, '_');
            return text;
        }

        private static void TryDeleteFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // best-effort cleanup only
            }
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static byte[] Base64UrlDecode(string value)
        {
            var s = value.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }

        private static string JsonEncodedJs(string json)
        {
            return json.Replace("</", "<\\/");
        }

        private sealed class AccessTokenPayload
        {
            public string DocGuid { get; set; } = string.Empty;
            public string DocTitle { get; set; } = string.Empty;
            public string ProjectName { get; set; } = string.Empty;
            public string EditorUser { get; set; } = string.Empty;
            public List<string> ParamNames { get; set; } = new List<string>();
            public List<string> Permissions { get; set; } = new List<string>();
            public DateTimeOffset ExpiresUtc { get; set; }
        }

        private sealed class ParsedWorkbook
        {
            public string DocGuid { get; set; } = string.Empty;
            public string DocTitle { get; set; } = string.Empty;
            public List<string> ParamNames { get; set; } = new List<string>();
            public List<ImportedRoomRow> Rows { get; set; } = new List<ImportedRoomRow>();
        }

        private sealed class ImportedRoomRow
        {
            public int RowNumber { get; set; }
            public string RoomUniqueId { get; set; } = string.Empty;
            public string ElementIdText { get; set; } = string.Empty;
            public string Level { get; set; } = string.Empty;
            public string Number { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public Dictionary<string, string> ImportedValues { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class RoomSnapshot
        {
            public int ElementId { get; set; }
            public string UniqueId { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Number { get; set; } = string.Empty;
            public string LevelName { get; set; } = string.Empty;
            public Dictionary<string, RoomParamSnapshot> Params { get; set; } = new Dictionary<string, RoomParamSnapshot>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class RoomParamSnapshot
        {
            public string Name { get; set; } = string.Empty;
            public string StorageType { get; set; } = string.Empty;
            public string DataType { get; set; } = string.Empty;
            public bool IsReadOnly { get; set; }
            public string Display { get; set; } = string.Empty;
            public JsonNode? ValueNode { get; set; }
        }

        private sealed class PreviewSession
        {
            public string PreviewToken { get; set; } = string.Empty;
            public string DocGuid { get; set; } = string.Empty;
            public string DocTitle { get; set; } = string.Empty;
            public DateTimeOffset CreatedUtc { get; set; }
            public DateTimeOffset ExpiresUtc { get; set; }
            public List<string> ParamNames { get; set; } = new List<string>();
            public List<PreviewRow> Rows { get; set; } = new List<PreviewRow>();
            public int RowCount => Rows.Count;
            public int ReadyCount => Rows.Count(x => string.Equals(x.Status, "READY", StringComparison.OrdinalIgnoreCase));
            public int UnchangedCount => Rows.Count(x => string.Equals(x.Status, "UNCHANGED", StringComparison.OrdinalIgnoreCase));
            public int ConfirmCount => Rows.Count(x => string.Equals(x.Status, "CONFIRM_REQUIRED", StringComparison.OrdinalIgnoreCase));
            public int ReadOnlyCount => Rows.Count(x => string.Equals(x.Status, "READ_ONLY", StringComparison.OrdinalIgnoreCase));
            public int NotFoundCount => Rows.Count(x => string.Equals(x.Status, "NOT_FOUND", StringComparison.OrdinalIgnoreCase));
        }

        private sealed class ScheduleImportPreviewSession
        {
            public string PreviewToken { get; set; } = string.Empty;
            public string DocGuid { get; set; } = string.Empty;
            public string DocTitle { get; set; } = string.Empty;
            public string ScheduleName { get; set; } = string.Empty;
            public string ScheduleViewId { get; set; } = string.Empty;
            public string UploadedFilePath { get; set; } = string.Empty;
            public string UploadedFileName { get; set; } = string.Empty;
            public DateTimeOffset CreatedUtc { get; set; }
            public DateTimeOffset ExpiresUtc { get; set; }
            public JsonObject? PreviewPayload { get; set; }
        }

        private sealed class ScheduleImportQueueEntry
        {
            public string QueueId { get; set; } = string.Empty;
            public string QueueFilePath { get; set; } = string.Empty;
            public string PreviewToken { get; set; } = string.Empty;
            public string DocGuid { get; set; } = string.Empty;
            public string DocTitle { get; set; } = string.Empty;
            public string ScheduleName { get; set; } = string.Empty;
            public string UploadedFilePath { get; set; } = string.Empty;
            public string UploadedFileName { get; set; } = string.Empty;
            public string RequestedBy { get; set; } = string.Empty;
            public string ProjectFolderPath { get; set; } = string.Empty;
            public int ChangedCellCount { get; set; }
            public int EditableColumnCount { get; set; }
            public string Status { get; set; } = "queued";
            public DateTimeOffset CreatedUtc { get; set; }
            public DateTimeOffset NextPromptUtc { get; set; }
            public DateTimeOffset DeletedUtc { get; set; }
            public string LastMessage { get; set; } = string.Empty;
        }

        private static ScheduleImportQueueEntry? LoadScheduleImportQueueEntry(string path)
        {
            try
            {
                var entry = JsonSerializer.Deserialize<ScheduleImportQueueEntry>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
                if (entry != null)
                    entry.QueueFilePath = path;
                return entry;
            }
            catch
            {
                return null;
            }
        }

        private static void SaveScheduleImportQueueEntry(ScheduleImportQueueEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.QueueFilePath))
                throw new InvalidOperationException("QueueFilePath is required.");
            Directory.CreateDirectory(Path.GetDirectoryName(entry.QueueFilePath) ?? string.Empty);
            File.WriteAllText(entry.QueueFilePath, JsonSerializer.Serialize(entry, JsonOptions), Encoding.UTF8);
        }

        private sealed class PreviewRow
        {
            public int RowNumber { get; set; }
            public string RoomUniqueId { get; set; } = string.Empty;
            public string RoomName { get; set; } = string.Empty;
            public string RoomNumber { get; set; } = string.Empty;
            public string Level { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public List<PreviewCell> Cells { get; set; } = new List<PreviewCell>();
        }

        private sealed class PreviewCell
        {
            public string ParamName { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string CurrentDisplay { get; set; } = string.Empty;
            public string ImportedDisplay { get; set; } = string.Empty;
            public string CurrentComparable { get; set; } = string.Empty;
            public object? TypedValue { get; set; }
            public string Message { get; set; } = string.Empty;
        }
    }
}
