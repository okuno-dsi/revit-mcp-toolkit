#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Input;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.UI.InfoPick
{
    public partial class InfoPickWindow : Window, INotifyPropertyChanged
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;
        private readonly string _docKey;
        private string _statusText = "";
        private CaptureMode _captureMode = CaptureMode.None;
        private bool _captureActive;
        private bool _isPicking;
        private readonly PointPickExternalEventHandler _pointPickHandler;
        private readonly ExternalEvent _pointPickExternalEvent;

        public ObservableCollection<PickItem> Items { get; } = new ObservableCollection<PickItem>();

        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText == value) return;
                _statusText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public InfoPickWindow(UIDocument uidoc, string? docKey)
        {
            _uidoc = uidoc;
            _doc = uidoc.Document;
            _docKey = string.IsNullOrWhiteSpace(docKey) ? "doc" : docKey.Trim();
            InitializeComponent();
            DataContext = this;
            StatusText = "Start capture, pick in Revit. ESC stops capture without closing window.";
            _pointPickHandler = new PointPickExternalEventHandler(this);
            _pointPickExternalEvent = ExternalEvent.Create(_pointPickHandler);
            PreviewKeyDown += InfoPickWindow_PreviewKeyDown;
        }

        private bool EnsurePointViewSupported()
        {
            var view = _doc.ActiveView;
            var vt = view?.ViewType ?? ViewType.Undefined;
            var ok =
                view is ViewPlan ||
                view is ViewSection ||
                vt == ViewType.FloorPlan ||
                vt == ViewType.CeilingPlan ||
                vt == ViewType.AreaPlan ||
                vt == ViewType.EngineeringPlan ||
                vt == ViewType.Section ||
                vt == ViewType.Elevation ||
                vt == ViewType.Detail ||
                vt == ViewType.DraftingView;

            if (ok) return true;

            var detail = $"View='{view?.Name}' Type={vt} Class={view?.GetType().Name}";
            StatusText = "Point capture unsupported: " + detail;
            MessageBox.Show("Point capture supports Plan / Section / Elevation views only.\n" + detail, "Pick Info",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        private void StartPointCapture_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsurePointViewSupported()) return;
            if (_captureActive) return;
            _captureMode = CaptureMode.Points;
            _captureActive = true;
            var view = _doc.ActiveView;
            var vt = view?.ViewType ?? ViewType.Undefined;
            StatusText = $"Point capture started. View='{view?.Name}' Type={vt} Class={view?.GetType().Name}. Click points. ESC stops.";
            RequestPointPick();
        }

        private void StartElementCapture_Click(object sender, RoutedEventArgs e)
        {
            if (_captureActive) return;
            try
            {
                _captureMode = CaptureMode.Elements;
                _captureActive = true;
                StatusText = "Element capture started. Select elements, then click Finish in Revit.";
                MessageBox.Show("選択終了後、Revit左上の「終了」を押してください。", "Pick Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                if (IsVisible) Hide();
                var refs = _uidoc.Selection.PickObjects(ObjectType.Element, "Select elements (finish to stop)");
                if (refs == null) return;
                foreach (var r in refs)
                {
                    if (r == null) continue;
                    var elem = _doc.GetElement(r);
                    if (elem != null)
                        AddElement(elem, r.GlobalPoint);
                }
                FinishCaptureInternal("Element capture stopped.");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                FinishCaptureInternal("Element capture canceled.");
            }
            catch (Exception ex)
            {
                FinishCaptureInternal("Element capture stopped.");
                StatusText = "Pick failed: " + ex.Message;
            }
            finally
            {
                if (!IsVisible) Show();
                Activate();
            }
        }

        private void FinishCapture_Click(object sender, RoutedEventArgs e)
        {
            FinishCaptureInternal("Capture stopped.");
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            Items.Clear();
            StatusText = "Cleared.";
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (_captureActive)
                FinishCaptureInternal("Capture stopped before save.");
            try
            {
                var saved = SaveToTemp();
                StatusText = saved != null ? "Saved: " + saved : "Save skipped.";
                ClearSelectionAndRefresh();
                Close();
            }
            catch (Exception ex)
            {
                StatusText = "Save failed: " + ex.Message;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            ClearSelectionAndRefresh();
            Close();
        }

        private void RequestPointPick()
        {
            if (!_captureActive || _captureMode != CaptureMode.Points || _isPicking)
                return;
            _isPicking = true;
            try
            {
                if (IsVisible) Hide();
                _pointPickExternalEvent.Raise();
            }
            catch
            {
                _isPicking = false;
            }
        }

        internal void DoPointPickFromExternalEvent(UIApplication app)
        {
            try
            {
                if (!_captureActive || _captureMode != CaptureMode.Points)
                    return;

                if (!EnsurePointWorkPlane())
                {
                    FinishCaptureInternal("Point capture canceled.", refreshView: false);
                    return;
                }

                var pt = app.ActiveUIDocument.Selection.PickPoint(ObjectSnapTypes.None, "Pick a point (ESC to finish)");
                AddPoint(pt);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                FinishCaptureInternal("Point capture canceled.", refreshView: false);
            }
            catch (Exception ex)
            {
                var view = _doc.ActiveView;
                var vt = view?.ViewType ?? ViewType.Undefined;
                FinishCaptureInternal($"Pick failed in view '{view?.Name}' Type={vt} Class={view?.GetType().Name}.", refreshView: false);
                StatusText = "Pick failed: " + ex.Message;
            }
            finally
            {
                _isPicking = false;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (!IsVisible) Show();
                        Activate();
                    }
                    catch { }

                    if (_captureActive)
                        RequestPointPick();
                }), DispatcherPriority.Background);
            }
        }

        private void FinishCaptureInternal(string? status = null, bool refreshView = true)
        {
            _captureActive = false;
            _captureMode = CaptureMode.None;
            _isPicking = false;
            if (!string.IsNullOrWhiteSpace(status))
                StatusText = status;
            else
                StatusText = "Capture stopped. OK to save, or start a new capture.";
            EnsureWindowVisible();
            if (refreshView)
                ScheduleRefresh();
        }

        private void EnsureWindowVisible()
        {
            try
            {
                if (!IsVisible) Show();
                Activate();
            }
            catch { /* best-effort */ }
        }

        private bool EnsurePointWorkPlane()
        {
            var view = _doc.ActiveView;
            if (view == null) return false;
            if (view.SketchPlane != null) return true;

            try
            {
                using (var t = new Transaction(_doc, "Set Work Plane"))
                {
                    t.Start();

                    XYZ origin = XYZ.Zero;
                    try { origin = view.Origin; } catch { }
                    if (origin.IsZeroLength())
                    {
                        try
                        {
                            var box = view.CropBox;
                            if (box != null)
                                origin = (box.Min + box.Max) * 0.5;
                        }
                        catch { }
                    }

                    var normal = view.ViewDirection;
                    if (normal == null || normal.IsZeroLength()) normal = XYZ.BasisZ;

                    var plane = Plane.CreateByNormalAndOrigin(normal, origin);
                    var sp = SketchPlane.Create(_doc, plane);
                    view.SketchPlane = sp;

                    t.Commit();
                }
                return true;
            }
            catch (Exception ex)
            {
                StatusText = "Work plane set failed: " + ex.Message;
                MessageBox.Show("No work plane in current view, and setting it failed.\n" + ex.Message, "Pick Info",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        private void InfoPickWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _captureActive)
            {
                e.Handled = true;
                FinishCaptureInternal("Capture stopped (ESC).");
            }
        }

        private void AddPoint(XYZ pt)
        {
            var item = new PickItem
            {
                Index = Items.Count + 1,
                Kind = "Point",
                Xyz = pt,
                XyzMm = FormatXyzMm(pt)
            };
            Items.Add(item);
            StatusText = $"Point added (#{item.Index}).";
        }

        private void AddElement(Element elem, XYZ? pickPoint = null)
        {
            var typeId = elem.GetTypeId();
            var typeName = string.Empty;
            if (typeId != ElementId.InvalidElementId)
            {
                var typeElem = _doc.GetElement(typeId) as ElementType;
                typeName = typeElem?.Name ?? string.Empty;
            }

            var item = new PickItem
            {
                Index = Items.Count + 1,
                Kind = "Element",
                ElementId = elem.Id.IntegerValue,
                Category = elem.Category?.Name ?? string.Empty,
                TypeName = typeName,
                Xyz = pickPoint,
                XyzMm = pickPoint != null ? FormatXyzMm(pickPoint) : string.Empty
            };
            Items.Add(item);
            StatusText = $"Element added (#{item.Index}).";
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var json = BuildPayloadJson();
                Clipboard.SetText(json ?? string.Empty);
                StatusText = "Copied to clipboard.";
            }
            catch (Exception ex)
            {
                StatusText = "Copy failed: " + ex.Message;
            }
        }

        private void SendToCodex_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var json = BuildPayloadJson();
                var pickDir = ResolvePickInfoDir(out _);
                var path = Path.Combine(pickDir, "codexgui_input_inbox.json");
                var obj = new
                {
                    text = json,
                    source = "PickInfo",
                    docTitle = _doc.Title,
                    docKey = _docKey,
                    createdUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                };
                File.WriteAllText(path, JsonConvert.SerializeObject(obj, Formatting.Indented), new UTF8Encoding(false));
                StatusText = "Sent to CodexGUI input.";
            }
            catch (Exception ex)
            {
                StatusText = "Send failed: " + ex.Message;
            }
        }

        private string? SaveToTemp()
        {
            var outDir = ResolvePickInfoDir(out var warn);
            if (!string.IsNullOrWhiteSpace(warn))
                StatusText = warn!;

            var nowUtc = DateTime.UtcNow;
            var safeKey = string.IsNullOrWhiteSpace(_docKey) ? "doc" : _docKey;
            var fileName = $"pick_info_{safeKey}_{nowUtc:yyyyMMdd_HHmmss}.json";
            var fullPath = Path.Combine(outDir, fileName);

            var view = _doc.ActiveView;
            var items = Items.Where(x => !x.Excluded).Select(x => new
            {
                index = x.Index,
                kind = x.Kind,
                elementId = x.ElementId > 0 ? (int?)x.ElementId : null,
                category = string.IsNullOrWhiteSpace(x.Category) ? null : x.Category,
                typeName = string.IsNullOrWhiteSpace(x.TypeName) ? null : x.TypeName,
                xyz = x.Xyz != null ? new
                {
                    x = x.Xyz.X,
                    y = x.Xyz.Y,
                    z = x.Xyz.Z
                } : null,
                xyzMm = x.Xyz != null ? new
                {
                    x = UnitUtils.ConvertFromInternalUnits(x.Xyz.X, UnitTypeId.Millimeters),
                    y = UnitUtils.ConvertFromInternalUnits(x.Xyz.Y, UnitTypeId.Millimeters),
                    z = UnitUtils.ConvertFromInternalUnits(x.Xyz.Z, UnitTypeId.Millimeters)
                } : null
            }).Cast<object>().ToList();

            var payload = BuildPayload(nowUtc, view, items);
            var json = JsonConvert.SerializeObject(payload, Formatting.Indented);
            File.WriteAllText(fullPath, json, new UTF8Encoding(false));

            // Handoff: write a small inbox file for CodexGUI
            try
            {
                var inbox = new
                {
                    path = fullPath,
                    docTitle = _doc.Title,
                    docKey = _docKey,
                    createdUtc = nowUtc.ToString("o", CultureInfo.InvariantCulture)
                };
                var inboxPath = Path.Combine(outDir, "pick_info_inbox.json");
                File.WriteAllText(inboxPath, JsonConvert.SerializeObject(inbox, Formatting.Indented), new UTF8Encoding(false));
            }
            catch { /* best-effort */ }

            return fullPath;
        }

        private string ResolvePickInfoDir(out string? warn)
        {
            warn = null;
            var workProject = TryResolveWorkProjectFolder(_doc.Title, _docKey);
            if (!string.IsNullOrWhiteSpace(workProject))
            {
                var dir = Path.Combine(workProject, "PickInfo");
                Directory.CreateDirectory(dir);
                return dir;
            }

            var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitMCP", "PickInfo");
            Directory.CreateDirectory(fallback);
            warn = "Work folder not found. Saved under AppData.";
            return fallback;
        }

        private static string? TryResolveWorkProjectFolder(string? docTitle, string? docKey)
        {
            var workRoot = ResolveWorkRoot();
            if (string.IsNullOrWhiteSpace(workRoot)) return null;

            var workDir = workRoot;
            if (string.IsNullOrWhiteSpace(workDir) || !Directory.Exists(workDir)) return null;

            var dirs = Directory.GetDirectories(workDir);
            if (!string.IsNullOrWhiteSpace(docKey))
            {
                var keyToken = "_" + docKey.Trim();
                var match = dirs.FirstOrDefault(d =>
                    Path.GetFileName(d).EndsWith(keyToken, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match)) return match;
            }

            var safeTitle = SanitizePathSegment(docTitle);
            if (string.IsNullOrWhiteSpace(safeTitle)) safeTitle = "Project";
            var safeKey = SanitizePathSegment(docKey);
            if (string.IsNullOrWhiteSpace(safeKey)) safeKey = "unknown";

            var created = Path.Combine(workDir, $"{safeTitle}_{safeKey}");
            Directory.CreateDirectory(created);
            return created;
        }

        private static string? ResolveWorkRoot()
        {
            return Paths.ResolveWorkRoot();
        }

        private static string SanitizePathSegment(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return cleaned.Trim();
        }

        private void ScheduleRefresh()
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(ClearSelectionAndRefresh), DispatcherPriority.Background);
            }
            catch
            {
                ClearSelectionAndRefresh();
            }
        }

        private static string FormatXyzMm(XYZ pt)
        {
            var x = UnitUtils.ConvertFromInternalUnits(pt.X, UnitTypeId.Millimeters);
            var y = UnitUtils.ConvertFromInternalUnits(pt.Y, UnitTypeId.Millimeters);
            var z = UnitUtils.ConvertFromInternalUnits(pt.Z, UnitTypeId.Millimeters);
            return string.Format(CultureInfo.InvariantCulture, "{0:0.##}, {1:0.##}, {2:0.##}", x, y, z);
        }

        private object BuildPayload(DateTime nowUtc, View view, IEnumerable<object> items)
        {
            return new
            {
                schema = "revitmcp.pick-info.v1",
                createdUtc = nowUtc.ToString("o", CultureInfo.InvariantCulture),
                docTitle = _doc.Title,
                docPath = _doc.PathName,
                docKey = _docKey,
                viewId = view?.Id.IntegerValue,
                viewName = view?.Name,
                totalCount = Items.Count,
                excludedCount = Items.Count(x => x.Excluded),
                items
            };
        }

        private string BuildPayloadJson()
        {
            var view = _doc.ActiveView;
            var items = Items.Where(x => !x.Excluded).Select(x => new
            {
                index = x.Index,
                kind = x.Kind,
                elementId = x.ElementId > 0 ? (int?)x.ElementId : null,
                category = string.IsNullOrWhiteSpace(x.Category) ? null : x.Category,
                typeName = string.IsNullOrWhiteSpace(x.TypeName) ? null : x.TypeName,
                xyz = x.Xyz != null ? new { x = x.Xyz.X, y = x.Xyz.Y, z = x.Xyz.Z } : null,
                xyzMm = x.Xyz != null ? new
                {
                    x = UnitUtils.ConvertFromInternalUnits(x.Xyz.X, UnitTypeId.Millimeters),
                    y = UnitUtils.ConvertFromInternalUnits(x.Xyz.Y, UnitTypeId.Millimeters),
                    z = UnitUtils.ConvertFromInternalUnits(x.Xyz.Z, UnitTypeId.Millimeters)
                } : null
            }).Cast<object>().ToList();
            var payload = BuildPayload(DateTime.UtcNow, view, items);
            return JsonConvert.SerializeObject(payload, Formatting.Indented);
        }

        private void ClearSelectionAndRefresh()
        {
            try
            {
                _uidoc.Selection.SetElementIds(new List<ElementId>());
            }
            catch { /* best-effort */ }

            try
            {
                _uidoc.RefreshActiveView();
            }
            catch { /* best-effort */ }
        }
    }

    internal sealed class PointPickExternalEventHandler : IExternalEventHandler
    {
        private readonly WeakReference<InfoPickWindow> _windowRef;

        public PointPickExternalEventHandler(InfoPickWindow window)
        {
            _windowRef = new WeakReference<InfoPickWindow>(window);
        }

        public void Execute(UIApplication app)
        {
            if (_windowRef.TryGetTarget(out var window))
                window.DoPointPickFromExternalEvent(app);
        }

        public string GetName() => "RevitMCP PickInfo PointPick ExternalEvent";
    }

    public sealed class PickItem
    {
        public int Index { get; set; }
        public string Kind { get; set; } = string.Empty;
        public bool Excluded { get; set; }
        public int ElementId { get; set; }
        public string Category { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public XYZ? Xyz { get; set; }
        public string XyzMm { get; set; } = string.Empty;
    }

    internal enum CaptureMode
    {
        None,
        Points,
        Elements
    }
}
