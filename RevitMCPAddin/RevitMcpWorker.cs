// ================================================================
// File: RevitMcpWorker.cs  – robust版（JSONパース失敗時にpost_resultで掃除する）
// ================================================================
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Commands;
using RevitMCPAddin.Commands.AnalysisOps;
using RevitMCPAddin.Commands.AnnotationOps;
using RevitMCPAddin.Commands.Area;
using RevitMCPAddin.Commands.ConstraintOps;
using RevitMCPAddin.Commands.CurtainOps;
using RevitMCPAddin.Commands.DatumOps;
using RevitMCPAddin.Commands.Debug;
using RevitMCPAddin.Commands.DocOps;
using RevitMCPAddin.Commands.DocumentOps;
using RevitMCPAddin.Commands.DoorOps;
using RevitMCPAddin.Commands.DxfOps;
using RevitMCPAddin.Commands.ElementOps;
using RevitMCPAddin.Commands.ElementOps.ArchitecturalColumn;
using RevitMCPAddin.Commands.ElementOps.Ceiling;
using RevitMCPAddin.Commands.ElementOps.CurtainWall;
using RevitMCPAddin.Commands.ElementOps.Door;
using RevitMCPAddin.Commands.ElementOps.Face;
using RevitMCPAddin.Commands.ElementOps.FaceHost;
using RevitMCPAddin.Commands.ElementOps.FamilyInstanceOps;
using RevitMCPAddin.Commands.ElementOps.FloorOps;
using RevitMCPAddin.Commands.ElementOps.Foundation;
using RevitMCPAddin.Commands.ElementOps.Mass;
using RevitMCPAddin.Commands.ElementOps.Material;
using RevitMCPAddin.Commands.ElementOps.Paint;
using RevitMCPAddin.Commands.ElementOps.Railing;
using RevitMCPAddin.Commands.ElementOps.Roof;
using RevitMCPAddin.Commands.ElementOps.SanitaryFixture;
using RevitMCPAddin.Commands.ElementOps.StairOps;
using RevitMCPAddin.Commands.ElementOps.StructuralColumn;
using RevitMCPAddin.Commands.ElementOps.StructuralFrame;
using RevitMCPAddin.Commands.ElementOps.Wall;
using RevitMCPAddin.Commands.ElementOps.Window;
using RevitMCPAddin.Commands.ExcelPlan;
using RevitMCPAddin.Commands.Export;
using RevitMCPAddin.Commands.FamilyOps;
using RevitMCPAddin.Commands.FireProtection;
using RevitMCPAddin.Commands.FloorOps;
using RevitMCPAddin.Commands.GeneralOps;
using RevitMCPAddin.Commands.Geometry;
using RevitMCPAddin.Commands.LookupOps;
using RevitMCPAddin.Commands.GridOps;
using RevitMCPAddin.Commands.GroupOps;
using RevitMCPAddin.Commands.LightingOps;
using RevitMCPAddin.Commands.LinkOps;
using RevitMCPAddin.Commands.MassOps;
using RevitMCPAddin.Commands.MEPOps;
using RevitMCPAddin.Commands.MetaOps;
using RevitMCPAddin.Commands.Misc;
using RevitMCPAddin.Commands.Materials;
using RevitMCPAddin.Commands.ParamOps;
using RevitMCPAddin.Commands.Rebar;
using RevitMCPAddin.Commands.Revision;
using RevitMCPAddin.Commands.RevisionCloud;
using RevitMCPAddin.Commands.RoofOps;
using RevitMCPAddin.Commands.Room;
using RevitMCPAddin.Commands.RoomOps;
using RevitMCPAddin.Commands.Rooms;
using RevitMCPAddin.Commands.Rpc;
using RevitMCPAddin.Commands.ScheduleOps;
using RevitMCPAddin.Commands.Schedules;
using RevitMCPAddin.Commands.SiteOps;
using RevitMCPAddin.Commands.Space;
using RevitMCPAddin.Commands.Spatial;
using RevitMCPAddin.Commands.SurfaceOps;
using RevitMCPAddin.Commands.TypeOps;
using RevitMCPAddin.Commands.ViewOps;
using RevitMCPAddin.Commands.Visualization;
using RevitMCPAddin.Commands.VisualizationOps;
using RevitMCPAddin.Commands.WindowOps;
using RevitMCPAddin.Commands.WorksetOps;
using RevitMCPAddin.Commands.ZoneOps;
using RevitMCPAddin.Core;
using RevitMCPAddin.ExternalEvents;
using RevitMCPAddin.RevitUI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using ListDupHandler = RevitMcpAddin.Commands.ListDuplicateElementsHandler;

namespace RevitMCPAddin
{
    public class RevitMcpWorker
    {
        private readonly ExternalEvent _extEvent;
        private readonly RevitCommandExecutor _executor;
        private readonly HttpClient _client;
        private readonly int _port;
        private bool _isRunning;

        private readonly System.Threading.CancellationTokenSource _cts = new System.Threading.CancellationTokenSource();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.CancellationTokenSource> _heartbeatMap;

        private void StartHeartbeat(string rpcId, string jobId)
        {
            try
            {
                var key = !string.IsNullOrWhiteSpace(rpcId) ? rpcId : jobId;
                if (string.IsNullOrWhiteSpace(key)) return;
                if (_heartbeatMap.ContainsKey(key)) return;
                var cts = new System.Threading.CancellationTokenSource();
                if (_heartbeatMap.TryAdd(key, cts))
                {
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            int maxBeats = 6;
                            for (int i = 0; i < maxBeats && !cts.IsCancellationRequested; i++)
                            {
                                string qs = !string.IsNullOrWhiteSpace(jobId) ? ("jobId=" + Uri.EscapeDataString(jobId))
                                            : (!string.IsNullOrWhiteSpace(rpcId) ? ("rpcId=" + Uri.EscapeDataString(rpcId)) : null);
                                if (!string.IsNullOrEmpty(qs))
                                {
                                    try { using var _ = await _client.GetAsync("heartbeat?" + qs).ConfigureAwait(false); }
                                    catch { }
                                }
                                await System.Threading.Tasks.Task.Delay(10000, cts.Token).ConfigureAwait(false);
                            }
                        }
                        catch { }
                        finally { _heartbeatMap.TryRemove(key, out _); cts.Dispose(); }
                    });
                }
            }
            catch { }
        }

        private void StopHeartbeat(string key)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key)) return;
                if (_heartbeatMap.TryRemove(key, out var cts))
                {
                    try { cts.Cancel(); } catch { }
                    cts.Dispose();
                }
            }
            catch { }
        }

        public RevitMcpWorker(UIControlledApplication application, int port)
            : this(application, /*logPath:*/ null, port) { }

        public RevitMcpWorker(UIControlledApplication application, string logPath, int port)
        {
            _port = port;

            Register("list_duplicate_elements", new RevitMcpAddin.Commands.ListDuplicateElementsHandler());

            SafeLog($"[{DateTime.Now:HH:mm:ss}] Worker initialized for port {_port}");

            // Global HTTP knobs (best-effort; ignore if not supported)
            try
            {
                ServicePointManager.Expect100Continue = false;
                ServicePointManager.DefaultConnectionLimit = Math.Max(100, ServicePointManager.DefaultConnectionLimit);
                ServicePointManager.UseNagleAlgorithm = false;
            }
            catch { /* ignore */ }

            // Prefer a tuned handler for local loopback to reduce latency
            HttpMessageHandler handler;
            try
            {
                var h = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    UseCookies = false,
                    Proxy = null,
                    UseProxy = false,
                    AllowAutoRedirect = false
                };
                handler = h;
            }
            catch
            {
                handler = new HttpClientHandler();
            }

            _client = new HttpClient(handler)
            {
                BaseAddress = new Uri($"http://localhost:{_port}/"),
                Timeout = TimeSpan.FromSeconds(120)
            };
            try
            {
                _client.DefaultRequestHeaders.ConnectionClose = false;
                if (_client.DefaultRequestHeaders.AcceptEncoding != null)
                {
                    if (!_client.DefaultRequestHeaders.AcceptEncoding.Contains(new StringWithQualityHeaderValue("gzip")))
                        _client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
                    if (!_client.DefaultRequestHeaders.AcceptEncoding.Contains(new StringWithQualityHeaderValue("deflate")))
                        _client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
                }
            }
            catch { /* ignore */ }

            _heartbeatMap = new System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.CancellationTokenSource>(StringComparer.OrdinalIgnoreCase);


            // --- コマンドハンドラ登録（元の一覧を維持） ---
            var batchHandler = new RevitBatchCommand();
            var handlerList = new List<IRevitCommandHandler>
            {
                new SearchCommandsHandler(),
                new DescribeCommandHandler(),
                new GetContextCommand(),
                batchHandler,

                // 記録開始/停止
                new StartCommandLoggingCommand(),
                new StopCommandLoggingCommand(),

                // Rpc
                new GetOpenDocumentsCommand(),
                // CompareOps (multi-port summary)
                new RevitMCPAddin.Commands.CompareOps.ValidateCompareContextCommand(),
                new RevitMCPAddin.Commands.CompareOps.CompareProjectsSummary(),
                new PingServerCommand(),

                // DocumentOps
                new GetProjectInfoCommand(),
                new RevitMCPAddin.Commands.DocumentOps.UpdateProjectInfoCommand(),
                new GetProjectCategoriesCommand(),
                new GetProjectSummaryCommand(),
                new GetFillPatternsCommand(),
                new SaveSnapshotCommand(),

                // 選択要素
                new GetSelectedElementIdsCommand(),
                new StashSelectionCommand(),
                new RestoreSelectionCommand(),
                new GetLastSelectionCommand(),
                new UnpinElementCommand(),
                new UnpinElementsCommand(),

                // 3D Bounding Box
                new GetOrientedBoundingBoxHandler(),

                // ビュー情報
                new GetCurrentViewCommand(),
                new GetViewInfoCommand(),
                new SaveViewStateCommand(),
                new RestoreViewStateCommand(),
                new CreateViewPlanCommand(),
                new CreateSectionViewCommand(),
                new CreateElevationViewCommand(),
                new CompareViewStatesCommand(),
                new GetViewsCommand(),
                new GetCategoryVisibilityCommand(),
                new SetCategoryOverrideCommand(),
                new SetCategoryVisibilityBulkCommand(),
                new ShowAllInViewCommand(),
                new GetCategoriesUsedInViewCommand(),
                new SetSectionBoxByElementsCommand(),
                new CreateFocus3DViewFromSelectionCommand(),
                new SetCategoryVisibilityCommand(),
                new ResizeSectionBoxCommand(),
                new DeleteViewCommand(),
                new SetViewTemplateCommand(),
                new SaveViewAsTemplateCommand(),
                new RenameViewTemplateCommand(),
                new SetViewTypeCommand(),
                new SetViewParameterCommand(),
                new CreateLegendViewFromTemplateCommand(),
                new CopyLegendComponentsBetweenViewsCommand(),
                new LayoutLegendComponentsInViewCommand(),
                new SetLegendComponentTypeCommand(),
                new AuditHiddenInViewHandler(),
                new DiagnoseViewVisibilityCommand(),
                new HideElementsInViewCommand(),
                new ClearViewTemplateCommand(),
                new ApplyConditionalColoringCommand(),
                new ClearConditionalColoringCommand(),
                new ColorizeTagsByParamCommand(),
                new ResetTagColorsCommand(),
                new GetFilledRegionTypesCommand(),
                new GetFilledRegionsInViewCommand(),
                new CreateFilledRegionCommand(),
                new SetFilledRegionTypeCommand(),
                new ReplaceFilledRegionBoundaryCommand(),
                new DeleteFilledRegionCommand(),
                new RenameViewCommand(),
                new DuplicateViewSimpleCommand(),
                new IsolateByFilterInViewCommand(),
                new SyncViewStateCommand(),

                new BuildViewsByRulesetCommand(),
                new ViewZoomHandler(),
                new BuildViewsByParamValuesCommand(),
                new VisibilityProfilePreviewCommand(),
                new CreateElementElevationViewsCommand(),
                new DeleteOrphanElevationMarkersCommand(),
                new CreateElementSectionBoxDebugCommand(),
                new DebugSectionViewSectionBoxCommand(),
                new DebugElementViewCropCommand(),
                new ViewOrbitHandler(),
                new ViewPanHandler(),
                new ViewResetOriginHandler(),
                new ViewFitHandler(),
                new ViewZoomToElementHandler(),
                new RefreshViewHandler(),
                new RegenAndRefreshHandler(),

                new SetGestureZoomEnabledHandler(),
                new SetGestureEnabledHandler(),
                new GestureZoomHandler(),
                new GestureOrbitHandler(),
                new GesturePanHandler(),

                //-- ScopeBox
                new ListScopeBoxesHandler(),
                new GetViewScopeBoxHandler(),
                new AssignScopeBoxHandler(),
                new ClearScopeBoxHandler(),
                new CreateScopeBoxHandler(),
                new UpdateScopeBoxHandler(),
                new RenameScopeBoxHandler(),

                new Save3DViewSettingsHandler(),
                new Apply3DViewSettingsHandler(),

                // RevitUI
                new ListOpenViewsCommand(),
                new ActivateViewCommand(),
                new OpenViewsCommand(),
                new CloseInactiveViewsCommand(),
                new CloseViewsCommand(),
                new CloseViewsExceptCommand(),
                new TileWindowsCommand(),
                new ListDockablePanesCommand(), 
                new ShowDockablePaneCommand(),
                new HideDockablePaneCommand(),
                new DockablePaneSequenceCommand(),
                new DebugPaneResolveCommand(),
                new DebugPaneIntrospectionCommand(),
                new ToggleDockablePaneCommand(),
                new ArrangeViewsCommand(),

                // View Workspace (save/restore with Ledger DocKey)
                new SaveViewWorkspaceCommand(),
                new RestoreViewWorkspaceCommand(),
                new SetViewWorkspaceAutosaveCommand(),
                new GetViewWorkspaceRestoreStatusCommand(),
 
                // 太陽光・採光
                new SimulateSunlightCommand(),
                new PrepareSunstudyViewCommand(),

                new CreateSpatialVolumeOverlayCommand(),
                new DeleteSpatialVolumeOverlaysCommand(),
                new GetSpatialContextForElementCommand(),
                new GetSpatialContextForElementsCommand(),

                // 3DViewOps
                new Create3DViewCommand(),
                new CreatePerspectiveViewCommand(),
                new CreateWalkthroughCommand(),

                // ViewSheet Ops
                new CreateSheetCommand(),
                new GetSheetsCommand(),
                new SheetInspectCommand(),
                new DeleteSheetCommand(),
                new PlaceViewOnSheetCommand(),
                new PlaceViewOnSheetAutoCommand(),
                new RemoveViewFromSheetCommand(),
                new ReplaceViewOnSheetCommand(),

                new GetViewPlacementsCommand(),
                new RemoveTitleblocksAutoCommand(),
                new ViewportMoveToSheetCenterCommand(),

                // 展開図ツール群
                new GetElevationViewTypesCommand(),
                new CreateInteriorElevationCommand(),
                new CreateRoomInteriorElevationsCommand(),
                new AutoCropElevationToRoomCommand(),
                new AutoCropElevationsForRoomCommand(),
                new CreateInteriorElevationFacingWallCommand(),

                // ビジュアルオーバーライド
                new SetVisualOverrideCommand(),
                new ClearVisualOverrideCommand(),
                new GetVisualOverridesInViewCommand(),

                new GetElementVisualOverrideCommand(),


                // カラースキーム
                new ListViewColorFillCategoriesCommand(),
                new ListColorFillSupportedParamsCommand(),
                new ListColorSchemesCommand(),
                new ApplyQuickColorSchemeCommand(),
                new ListColorfillBuiltinParamSuggestionsCommand(),

                // General Ops
                new GetElementsInViewCommand(),
                new GetTypesInViewCommand(),
                new SelectElementsByFilterByIdCommand(),
                new SelectElementsCommand(),
                new SummarizeElementsByCategoryCommand(),
                new SummarizeFamilyTypesByCategoryCommand(),

                new ExportDashboardHtmlCommand(),
                new ExportSchedulesToHtmlCommand(),

                // Level
                new CreateLevelCommand(),
                new GetLevelsCommand(),
                new ListLevelsSimpleCommand(),
                new UpdateLevelNameCommand(),
                new UpdateLevelElevationCommand(),
                new GetLevelParametersCommand(),
                new UpdateLevelParameterCommand(),

                // Grid
                new GetGridsCommand(),
                new CreateGridsCommand(),
                new UpdateGridNameCommand(),
                new MoveGridCommand(),
                new DeleteGridCommand(),
                new AdjustGridExtentsCommand(),

                // Material
                new GetMaterialsCommand(),
                new GetMaterialParametersCommand(),
                new ListMaterialParametersCommand(),
                new UpdateMaterialParameterCommand(),
                new DuplicateMaterialCommand(),
                new RenameMaterialCommand(),
                new DeleteMaterialCommand(),
                new CreateMaterialCommand(),
                new ApplyMaterialToElementCommand(),
                new GetElementMaterialCommand(),
                new GetMaterialAssetsCommand(),
                new ListPhysicalAssetsCommand(),
                new ListThermalAssetsCommand(),
                new SetMaterialAssetCommand(),
                new SetMaterialThermalConductivityCommand(),
                new GetMaterialAssetPropertiesCommand(),
                new SetMaterialAssetNameCommand(),
                new DuplicateMaterialAssetCommand(),
                new SetMaterialThermalPropertiesCommand(),
                new UpdatePropertySetElementParameterCommand(),
                new ScanPaintAndSplitRegionsHandler(),
                new GetDoorWindowTypesForScheduleCommand(),
                new PopulateDoorWindowLegendFromTemplateCommand(),

                // Paint
                new GetPaintInfoCommand(),
                new ApplyPaintCommand(),
                new RemovePaintCommand(),
                new ApplyPaintByReferenceCommand(),
                new RemovePaintByReferenceCommand(),

                // Face
                new GetFaceRegionsCommand(),
                new GetFaceRegionTakeoffCommand(),
                new GetFaceRegionDetailCommand(),

                // FaceHost
                new CreateFamilyOnFaceCommand(),

                // Surface
                new GetSurfaceRegionsHandler(),

                // Rooms / Regions discovery
                new FindRoomPlaceableRegionsCommand(),
                new SummarizeRoomsByLevelCommand(),

                // Detail Line
                new GetDetailLineStylesCommand(),
                new GetDetailLinesInViewCommand(),
                new CreateDetailLineCommand(),
                new CreateDetailArcCommand(),
                new MoveDetailLineCommand(),
                new RotateDetailLineCommand(),
                new DeleteDetailLineCommand(),
                new GetLineStylesCommand(),
                new SetDetailLineStyleCommand(),
                new SetDetailLinesStyleCommand(),
                new ExplodeModelLineToDetailCommand(),

                // Tag
                new GetTagSymbolsCommand(),
                new GetTagsInViewCommand(),
                new CreateTagCommand(),
                new GetTagBoundsInViewCommand(),
                new MoveTagCommand(),
                new RotateTagCommand(),
                new GetTagParametersCommand(),
                new UpdateTagParameterCommand(),
                new DeleteTagCommand(),

                // Dimension
                new GetDimensionsInViewCommand(),
                new CreateDimensionCommand(),
                new AddDoorSizeDimensionsCommand(),
                new DeleteDimensionCommand(),
                new MoveDimensionCommand(),
                new AlignDimensionCommand(),
                new UpdateDimensionFormatCommand(),
                new GetDimensionTypesCommand(),

                // Workset Ops
                new GetWorksetsCommand(),
                new GetElementWorksetCommand(),
                new CreateWorksetCommand(),
                new SetElementWorksetCommand(),

                // Schedule Ops
                new GetSchedulesCommand(),
                new CreateScheduleViewCommand(),
                new GetScheduleDataCommand(),
                new ListSchedulableFieldsCommand(),
                new UpdateScheduleFieldsCommand(),
                new ReorderScheduleFieldsCommand(),
                new UpdateScheduleFiltersCommand(),
                new UpdateScheduleSortingCommand(),
                new ExportScheduleToCsvCommand(),
                new DeleteScheduleCommand(),
                new InspectScheduleFieldsCommand(),
                new ExportScheduleToExcelCommand(),

                // DXF Export
                new GetCurvesByCategoryHandler(),
                new GetGridsWithBubblesHandler(),
                new ExportCurvesToDxfHandler(),

                // DWG Export
                new ExportDwgCommand(),
                new ExportDwgWithWorksetBucketingCommand(),
                new ExportDwgByParamGroupsCommand(),

                // Element General
                new GetBoundingBoxCommand(),
                new GetElementInfoHandler(),
                new GetInstanceGeometryCommand(),
                new GetInstancesGeometryCommand(),
                new ApplyTransformDeltaCommand(),
                new JoinElementsCommand(),
                new UnjoinElementsCommand(),
                new AreElementsJoinedCommand(),
                new SwitchJoinOrderCommand(),
                new GetJoinedElementsCommand(),

                // Rhino
                new ExportViewMeshCommand(),
                new ExportView3dmCommand(),
                new ExportView3dmBrepCommand(),


                // TypeOps
                new DeleteTypeIfUnusedCommand(),
                new RenameTypesBulkCommand(),
                new RenameTypesByParameterCommand(),

                // Revision
                new ExportSnapshotCommand(),
                // new CreateRevisionCloudFromCurvesCommand(), // deprecated: replaced by CreateRevisionCloudCommand (plane-projected)
                new ExportSnapshotBundleCommand(),

                // Revision Cloud
                new CreateDefaultRevisionCommand(),
                new CreateRevisionCloudCommand(),
                new CreateRevisionCircleCommand(),
                new MoveRevisionCloudCommand(),
                new DeleteRevisionCloudCommand(),
                new UpdateRevisionCommand(),
                new GetRevisionCloudTypesCommand(),
                new GetRevisionCloudTypeParametersCommand(),
                new SetRevisionCloudTypeParameterCommand(),
                new ChangeRevisionCloudTypeCommand(),
                new GetRevisionCloudParametersCommand(),
                new SetRevisionCloudParameterCommand(),
                new ListRevisionsCommand(),
                new GetRevisionCloudSpacingCommand(),
                new SetRevisionCloudSpacingCommand(),
                new ListSheetRevisionsCommand(),
                new CreateRevisionCloudForElementProjectionCommand(),
                new CreateObbCloudForSelectionCommand(),
                new GetRevisionCloudGeometryCommand(),

                // Room
                new ValidateCreateRoomCommand(),
                new GetRoomsCommand(),
                new GetRoomParamsCommand(),
                new SetRoomParamCommand(),
                new GetRoomBoundaryCommand(),
                new GetRoomPerimeterWithColumnsAndWallsCommand(),
                new GetRoomFinishTakeoffContextCommand(),
                new GetRoomPerimeterWithColumnsCommand(),
                new GetRoomInnerWallsByBaselineCommand(),
                new CreateRoomCommand(),
                new DeleteRoomCommand(),
                new GetRoomBoundaryWallsCommand(),
                new GetRoomCentroidCommand(),
                new CreateRoomBoundaryLineCommand(),
                new DeleteRoomBoundaryLineCommand(),
                new MoveRoomBoundaryLineCommand(),
                new TrimRoomBoundaryLineCommand(),
                new ExtendRoomBoundaryLineCommand(),
                new CleanRoomBoundariesCommand(),
                new GetRoomBoundaryLinesInViewCommand(),
                new PlaceRoomInCircuitCommand(),
                new FindRoomPlaceableRegionsCommand(),

                new GetAreaVolumeSettingsCommand(),
                new SetAreaVolumeSettingsCommand(),
                new GetRoomNeighborsCommand(),
                new GetRoomOpeningsCommand(),
                new GetRoomPlanarCentroidCommand(),
                new GetRoomLabelPointCommand(),

                new ClassifyPointsInRoomHandler(),
                new MapRoomAreaSpaceCommand(),
                new GetCompareFactorsCommand(),
                new FindSimilarByFactorsCommand(),
                new GetRoomBoundariesCommand(),
                new CreateRoomMassesCommand(),

                // Space
                new CreateSpaceCommand(),
                new DeleteSpaceCommand(),
                new GetSpaceParamsCommand(),
                new GetSpacesCommand(),
                new MoveSpaceCommand(),
                new UpdateSpaceCommand(),
                new GetAreaBoundaryCommand(),
                new GetSpaceBoundaryCommand(),
                new GetSpaceBoundaryWallsCommand(),
                new GetSpaceCentroidCommand(),
                new GetSpaceMetricsCommand(),
                new GetSpaceGeometryCommand(),

                // Zone
                new GetZonesCommand(),
                new CreateZoneCommand(),
                new AddSpacesToZoneCommand(),
                new RemoveSpacesFromZoneCommand(),
                new DeleteZoneCommand(),
                new ListZoneMembersCommand(),
                new GetZoneParamsCommand(),
                new SetZoneParamCommand(),
                new ComputeZoneMetricsCommand(),

                // Area
                new CreateAreaCommand(),
                new GetAreasCommand(),
                new GetAreaParamsCommand(),
                new UpdateAreaCommand(),
                new MoveAreaCommand(),
                new DeleteAreaCommand(),
                new GetAreaBoundaryWallsCommand(),
                new GetAreaCentroidCommand(),
                new GetAreaMetricsCommand(),
                new GetAreaGeometryCommand(),
                new CreateAreaPlanCommand(),
                new ListAreaSchemesCommand(),
                new GetAreaSchemesCommand(),
                new CreateAreaSchemeCommand(),
                new GetAreasBySchemeCommand(),
                // Area Boundaries
                new AutoAreaBoundariesFromWallsCommand(),
                new CopyAreaBoundariesFromRoomsCommand(),
                new CreateAreasFromRoomsByMaterialCoreCenterCommand(),
                new MergeAreasCommand(),
                new AreaBoundaryCreateByMaterialCoreCenterCommand(),
                new AreaBoundaryAdjustByMaterialCoreCenterCommand(),
                // Area Boundary editing
                new CreateAreaBoundaryLineCommand(),
                new DeleteAreaBoundaryLineCommand(),
                new MoveAreaBoundaryLineCommand(),
                new TrimAreaBoundaryLineCommand(),
                new ExtendAreaBoundaryLineCommand(),
                new CleanAreaBoundariesCommand(),
                new GetAreaBoundaryLinesInViewCommand(),
                new GetAreaBoundaryIntersectionsCommand(),
                new GetAreaBoundaryGapsCommand(),
                new GetAreaBoundaryDuplicatesCommand(),

                // Mass
                new CreateMassByExtrusionCommand(),
                new ChangeMassInstanceTypeCommand(),
                new CreateMassInstanceCommand(),
                new DeleteMassInstanceCommand(),
                new DuplicateMassTypeCommand(),
                new GetMassInstanceParametersCommand(),
                new GetMassInstancesCommand(),
                new GetMassTypeParametersCommand(),
                new GetMassTypesCommand(),
                new MoveMassInstanceCommand(),
                new RenameMassTypeCommand(),
                new RotateMassInstanceCommand(),
                new UpdateMassInstanceParameterCommand(),
                new UpdateDirectShapeParameterCommand(),

                // ArchitecturalColumn
                new CreateArchitecturalColumnCommand(),
                new GetArchitecturalColumnsAndParametersCommand(),
                new GetArchitecturalColumnParametersCommand(),
                new UpdateArchitecturalColumnParameterCommand(),
                new MoveArchitecturalColumnCommand(),
                new DeleteArchitecturalColumnCommand(),
                new ChangeArchitecturalColumnTypeCommand(),
                new GetArchitecturalColumnTypesCommand(),
                new DuplicateArchitecturalColumnTypeCommand(),
                new UpdateArchitecturalColumnGeometryCommand(),

                // Door
                new GetDoorsCommand(),
                new CreateDoorCommand(),
                new MoveDoorCommand(),
                new UpdateDoorParameterCommand(),
                new DeleteDoorCommand(),
                new GetDoorParametersCommand(),
                new GetDoorTypesCommand(),
                new GetDoorTypeParametersCommand(),
                new DuplicateDoorTypeCommand(),
                new RenameDoorTypeCommand(),
                new CreateDoorOnWallCommand(),
                new GetDoorOrientationHandler(),
                new FlipDoorOrientationCommand(),

                // Window
                new GetWindowsCommand(),
                new GetWindowParametersCommand(),
                new GetWindowTypesCommand(),
                new GetWindowTypeParametersCommand(),
                new DuplicateWindowTypeCommand(),
                new RenameWindowTypeCommand(),
                new CreateWindowCommand(),
                new MoveWindowCommand(),
                new UpdateWindowParameterCommand(),
                new DeleteWindowCommand(),
                new CreateWindowOnWallCommand(),
                new GetWindowOrientationHandler(),
                new FlipWindowOrientationCommand(),

                // Curtain Wall
                new GetCurtainWallsCommand(),
                new GetCurtainWallTypesCommand(),
                new CreateCurtainWallCommand(),
                new UpdateCurtainWallGeometryCommand(),
                new ChangeCurtainWallTypeCommand(),
                new UpdateCurtainWallParameterCommand(),
                new DeleteCurtainWallCommand(),

                new ListCurtainWallPanelsCommand(),
                new GetCurtainWallPanelGeometryCommand(),
                new SetCurtainWallPanelTypeCommand(),
                new AddCurtainWallPanelCommand(),
                new RemoveCurtainWallPanelCommand(),
                new ListMullionsCommand(),
                new CreateMullionCommand(),
                new UpdateMullionTypeCommand(),
                new DeleteMullionCommand(),

                new CheckPanelSizeCommand(),
                new CheckMullionConnectivityCommand(),
                new GetCurtainWallScheduleCommand(),

                new CreateCurtainWallElevationViewCommand(),
                new HighlightOverlargePanelsCommand(),
                new ExportCurtainWallReportCommand(),

                new FlipCurtainPanelOrientationCommand(),

                // Lookup / Debugging
                new LookupElementCommand(),

                // FloorOperations
                new CreateFloorCommand(),
                new GetFloorsCommand(),
                new MoveFloorCommand(),
                new UpdateFloorBoundaryCommand(),
                new DeleteFloorCommand(),
                new GetFloorParametersCommand(),
                new SetFloorParameterCommand(),
                new GetFloorTypesCommand(),
                new GetFloorTypeParametersCommand(),
                new SetFloorTypeParameterCommand(),
                new DuplicateFloorTypeCommand(),
                new RenameFloorTypeCommand(),
                new GetFloorBoundaryCommand(),
                new GetCandidateExteriorFloorsCommand(),
                //// Floor Layer
                new GetFloorTypeInfoCommand(),
                new GetFloorLayersCommand(),
                new UpdateFloorLayerCommand(),
                new AddFloorLayerCommand(),
                new RemoveFloorLayerCommand(),
                new SetFloorVariableLayerCommand(),
                new SwapFloorLayerMaterialsCommand(),

                // Wall Operations
                new GetWallsCommand(),
                new GetWallParametersCommand(),
                new GetWallTypesCommand(),
                new CreateWallCommand(),
                new CreateFlushWallsCommand(),
                new UpdateWallGeometryCommand(),
                new ChangeWallTypeCommand(),
                new UpdateWallParameterCommand(),
                new DuplicateWallTypeCommand(),
                new RenameWallTypeCommand(),
                new GetWallParameterCommand(),
                new GetWallTypeParametersCommand(),
                new ListWallParametersCommand(),
                new DeleteWallCommand(),
                new UpdateWallTypeParameterCommand(),
                new GetWallFacesCommand(),
                new ClassifyWallFacesBySideCommand(),
                new GetFaceFinishDataCommand(),
                new GetFacePaintDataCommand(),
                new GetWallFinishSummaryCommand(),
                new GetCandidateExteriorWallsCommand(),
                new GetWallBaselineCommand(),
                new SetWallTopToOverheadCommand(),
                new DisallowWallJoinAtEndCommand(),
                new SetWallJoinTypeCommand(),
                new RejoinWallsCommand(),
                new FindWallsNearSegmentsCommand(),
                new GetElementsByCategoryAndLevelCommand(),

                // Wall Layers (Basic Wall)
                new GetWallTypeInfoCommand(),
                new GetWallLayersCommand(),
                new UpdateWallLayerCommand(),
                new AddWallLayerCommand(),
                new RemoveWallLayerCommand(),
                new SetWallVariableLayerCommand(),
                new SwapWallLayerMaterialsCommand(),

                // Stacked Walls
                new GetStackedWallPartsCommand(),
                new UpdateStackedWallPartCommand(),
                new ReplaceStackedWallPartTypeCommand(),
                new FlattenStackedWallToBasicCommand(),

                // Ceiling
                new CreateCeilingCommand(),
                new DuplicateCeilingTypeCommand(),
                new DeleteCeilingTypeCommand(),
                new GetCeilingTypesCommand(),
                new ChangeCeilingTypeCommand(),
                new DeleteCeilingCommand(),
                new GetCeilingBoundariesCommand(),
                new GetCeilingsCommand(),
                new MoveCeilingCommand(),
                new GetCeilingInstanceParametersCommand(),
                new SetCeilingInstanceParameterCommand(),
                new GetCeilingTypeParametersCommand(),
                new SetCeilingTypeParameterCommand(),

                // StructuralColumn Operations
                new CreateStructuralColumnCommand(),
                new GetStructuralColumnsCommand(),
                new UpdateStructuralColumnParameterCommand(),
                new MoveStructuralColumnCommand(),
                new DeleteStructuralColumnCommand(),
                new UpdateStructuralColumnGeometryCommand(),
                new GetStructuralColumnParameterCommand(),
                new GetStructuralColumnParametersCommand(),
                new GetStructuralColumnTypeParametersCommand(),
                new ChangeStructuralColumnTypeCommand(),
                new GetStructuralColumnTypesCommand(),
                new DuplicateStructuralColumnTypeCommand(),
                new ListStructuralColumnParametersCommand(),
                new UpdateStructuralColumnTypeParameterCommand(),
                new GetCandidateExteriorColumnsCommand(),
                new ClassifyColumnsByRoomArmsCommand(),

                // StructuralFraming Operations
                new CreateStructuralFrameCommand(),
                new DuplicateStructuralFrameTypeCommand(),
                new GetStructuralFramesCommand(),
                new UpdateStructuralFrameGeometryCommand(),
                new MoveStructuralFrameCommand(),
                new DeleteStructuralFrameCommand(),
                new GetStructuralFrameParameterCommand(),
                new UpdateStructuralFrameParameterCommand(),
                new GetStructuralFrameParametersCommand(),
                new GetStructuralFrameTypeParametersCommand(),
                new ChangeStructuralFrameTypeCommand(),
                new GetStructuralFrameTypesCommand(),
                new ListStructuralFrameParametersCommand(),
                new UpdateStructuralFrameTypeParameterCommand(),
                new DisallowStructuralFrameJoinAtEndCommand(),

                // Rebar layout + mapping (shape-driven rebar sets; v1)
                new RebarLayoutInspectCommand(),
                new RebarLayoutUpdateCommand(),
                new RebarLayoutUpdateByHostCommand(),
                new RebarMappingResolveCommand(),
                new RebarPlanAutoCommand(),
                new RebarApplyPlanCommand(),
                new RebarSyncStatusCommand(),
                new RebarRegenerateDeleteRecreateCommand(),
                new DeleteRebarsCommand(),
                new MoveRebarsCommand(),

                // StructuralFoundation Operations
                new CreateStructuralFoundationCommand(),
                new GetStructuralFoundationsCommand(),
                new UpdateStructuralFoundationParameterCommand(),
                new MoveStructuralFoundationCommand(),
                new DeleteStructuralFoundationCommand(),
                new UpdateStructuralFoundationGeometryCommand(),
                new GetStructuralFoundationParameterCommand(),
                new GetStructuralFoundationParametersCommand(),
                new GetStructuralFoundationTypeParametersCommand(),
                new ChangeStructuralFoundationTypeCommand(),
                new GetStructuralFoundationTypesCommand(),
                new DuplicateStructuralFoundationTypeCommand(),
                new ListStructuralFoundationParametersCommand(),
                new UpdateStructuralFoundationTypeParameterCommand(),

                // SanitaryFixture Operations
                new GetSanitaryFixturesCommand(),
                new UpdateSanitaryFixtureCommand(),
                new MoveSanitaryFixtureCommand(),
                new DeleteSanitaryFixtureCommand(),
                new GetSanitaryFixtureTypesCommand(),
                new ChangeSanitaryFixtureTypeCommand(),
                new DeleteSanitaryFixtureTypeCommand(),

                // Railing Operations
                new CreateRailingCommand(),
                new GetRailingsCommand(),
                new DeleteRailingCommand(),
                new GetRailingParametersCommand(),
                new SetRailingParameterCommand(),
                new GetRailingTypesCommand(),
                new ChangeRailingTypeCommand(),
                new DuplicateRailingTypeCommand(),
                new DeleteRailingTypeCommand(),
                new GetRailingTypeParametersCommand(),
                new SetRailingTypeParameterCommand(),

                // Stairs Operations
                new GetStairsCommand(),
                new DuplicateStairInstanceCommand(),
                new MoveStairInstanceCommand(),
                new DeleteStairInstanceCommand(),
                new GetStairParametersCommand(),
                new SetStairParameterCommand(),
                new GetStairTypesCommand(),
                new DuplicateStairTypeCommand(),
                new DeleteStairTypeCommand(),
                new ChangeStairTypeCommand(),
                new GetStairTypeParametersCommand(),
                new SetStairTypeParameterCommand(),
                new GetStairFlightsCommand(),
                new SetStairFlightParametersCommand(),

                // Fire Protection Operations
                new GetFireProtectionInstancesCommand(),
                new CreateFireProtectionInstanceCommand(),
                new MoveFireProtectionInstanceCommand(),
                new DeleteFireProtectionInstanceCommand(),
                new GetFireProtectionParametersCommand(),
                new SetFireProtectionParameterCommand(),
                new GetFireProtectionTypesCommand(),
                new DuplicateFireProtectionTypeCommand(),
                new DeleteFireProtectionTypeCommand(),
                new ChangeFireProtectionTypeCommand(),
                new CheckFireRatingComplianceCommand(),
                new GenerateFireProtectionScheduleCommand(),

                // Group Get Commands
                new GetGroupsHandler(),
                new GetGroupTypesHandler(),
                new GetGroupInfoHandler(),
                new GetElementGroupMembershipHandler(),
                new GetGroupsInViewHandler(),
                new GetGroupMembersHandler(),
                new GetGroupConstraintsReportHandler(),

                // Family Instances
                new GetFamilyInstancesCommand(),
                new GetFamilyTypesCommand(),
                new CreateFamilyInstanceCommand(),
                new MoveFamilyInstanceCommand(),
                new DeleteFamilyInstanceCommand(),
                new GetFamilyInstanceParametersCommand(),
                new GetFamilyInstanceReferencesCommand(),
                new UpdateFamilyInstanceParameterCommand(),
                new GetFamilyTypeParametersCommand(),
                new SetFamilyTypeParameterCommand(),
                new ChangeFamilyInstanceTypeCommand(),
                new FlipFamilyInstanceOrientationCommand(),

                // Inplace Families
                new GetInplaceFamiliesCommand(),

                // Roof
                new CreateRoofCommand(),
                new DeleteRoofCommand(),
                new MoveRoofCommand(),
                new UpdateRoofBoundaryCommand(),
                new SetRoofParameterCommand(),
                new SetRoofTypeParameterCommand(),
                new GetRoofSlopeCommand(),
                new SetRoofSlopeCommand(),
                new ChangeRoofTypeCommand(),
                new PlaceRoofBraceFromPromptCommand(),
                //// Roof Layer
                new GetRoofTypeInfoCommand(),
                new GetRoofLayersCommand(),
                new UpdateRoofLayerCommand(),
                new AddRoofLayerCommand(),
                new RemoveRoofLayerCommand(),
                new SetRoofVariableLayerCommand(),
                new SwapRoofLayerMaterialsCommand(),

                // Site
                new CreateToposurfaceHandler(),
                new EditToposurfacePointsHandler(),
                new PlaceBuildingPadHandler(),
                new PropertyLineHandler(),
                new SiteCoordinatesHandler(),
                new PropertyLineEditDeleteHandler(),
                new SiteComponentHandler(),
                new SiteSubRegionHandler(),
                new ParkingSpotHandler(),
                new TopographyInfoHandler(),

                // copy_family_type_between_docs
                new CopyFamilyTypeBetweenDocsCommand(),

                // MEP Ops
                new CreateDuctCommand(),
                new CreatePipeCommand(),
                new CreateCableTrayCommand(),
                new CreateConduitCommand(),
                new GetMepElementsCommand(),
                new MoveMepElementCommand(),
                new DeleteMepElementCommand(),
                new ChangeMepElementTypeCommand(),
                new GetMepParametersCommand(),
                new SetMepParameterCommand(),

                // LightingFixtures
                new ListLightingFixturesCommand(),
                new GetLightingPowerSummaryCommand(),
                new CheckLightingEnergyCommand(),
                new EstimateIlluminanceInRoomCommand(),
                new ExportLightingReportCommand(),

                // AutoCAD / DWG script generation (no execution)
                new GenerateDwgMergeScriptCommand(),

                // ModelLineOperations
                new GetModelLinesInViewCommand(),
                new CreateModelLineCommand(),
                new CreateModelArcCommand(),
                new MoveModelLineCommand(),
                new RotateModelLineCommand(),
                new DeleteModelLineCommand(),
                new DeleteModelLinesCommand(),
                new SetModelLineStyleCommand(),
                new SetModelLinesStyleCommand(),

                // TextNoteCommands
                new GetTextNotesInViewCommand(),
                new CreateTextNoteCommand(),
                new MoveTextNoteCommand(),
                new DeleteTextNoteCommand(),
                new UpdateTextNoteParameterCommand(),

                // LinkOperations
                new ListLinksCommand(),
                new ReloadLinkCommand(),
                new UnloadLinkCommand(),
                new ReloadLinkFromCommand(),
                new BindLinkCommand(),
                new DetachLinkCommand(),

                // Excel Plan
                new ExcelPlanImporterCommand(),

                // Parameter Operations
                new GetParamValuesCommand(),
                new GetParamMetaCommand(),
                new GetParameterIdentityCommand(),
                new GetTypeParametersBulkCommand(),
                new GetInstanceParametersBulkCommand(),
                new UpdateParametersBatchCommand(),
                new SetParameterForElementsCommand(),
                new AddSharedProjectParameterCommand(),
                new RemoveProjectParameterBindingCommand(),

                // CheckClashesCommand
                new CheckClashesCommand(),

                // ConstraintCommands
                new ConstraintCommands(),

                new AnalyzeSegmentsCommand(),

                // AgentBootstrapHandler
                new AgentBootstrapHandler(),
                new GetMcpLedgerSummaryCommand(),

                // Smoke Test
                new SmokeTestNoopHandler(),

                // ---- MCP Robust Diff/Snapshot/View Enhancements ----
                new SnapshotViewElementsCommand(),
                new DiffElementsCommand(),
                new EnsureCompareViewCommand(),
                new BatchSetVisualOverrideCommand(),
                new CreateRevisionCloudsForElementsCommand(),
                new ListRevisionCloudsInViewCommand()

            };


            handlerList.Insert(0, new RevitMCPAddin.Commands.MetaOps.ListCommandsHandler(handlerList));
            // Step 2: build command metadata registry from the actual registered handlers (best-effort; non-fatal)
            try { RevitMCPAddin.Core.CommandMetadataRegistry.InitializeFromHandlers(handlerList); } catch { }

            var router = new CommandRouter(handlerList);
            try { batchHandler.BindRouter(router); } catch { /* best-effort */ }
            _executor = new RevitMCPAddin.ExternalEvents.RevitCommandExecutor(router, _client, RevitMCPAddin.Core.RevitLogger.LogPath);
            _executor.StopHeartbeatCallback = this.StopHeartbeat;
            _extEvent = ExternalEvent.Create(_executor);
        }


        public void Start()
        {
            _isRunning = true;
            LongOpEngine.Initialize(uiapp: null, baseAddress: new Uri($"http://127.0.0.1:{_port}/"));

            Task.Run(async () =>
            {
                while (_isRunning)
                {
                    var swLoop = System.Diagnostics.Stopwatch.StartNew();
                    bool gotWork = false;
                    int parsedCommandCount = 0;
                    bool usedLongPoll = true;

            try
            {
                // Use server-side long-polling to reduce latency and spin
                using var res = await _client.GetAsync("pending_request?waitMs=30000", HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                        string jobIdHeader = null;
                        try
                        {
                            if (res.Headers.TryGetValues("X-Job-Id", out var __vals))
                            {
                                foreach (var v in __vals) { jobIdHeader = v; break; }
                            }
                        }
                        catch { }

                        // “仕事なし”判定
                        if (!res.IsSuccessStatusCode || res.StatusCode == HttpStatusCode.NoContent)
                        {
                            AdaptiveWaitController.Instance.ReportPoll(false);

                            // Fallback diagnostic: if NoContent but jobs exist, rebind and retry sooner
                            if (res.StatusCode == HttpStatusCode.NoContent)
                            {
                                try
                                {
                                    using var peek = await _client.GetAsync("jobs?state=ENQUEUED&limit=1", HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                                    if (peek.IsSuccessStatusCode)
                                    {
                                        var s = await peek.Content.ReadAsStringAsync().ConfigureAwait(false);
                                        // Very light check to avoid JSON parse cost
                                        if (!string.IsNullOrWhiteSpace(s) && s.TrimStart().StartsWith("["))
                                        {
                                            // If there seems to be at least one job, log and attempt a quick rebind
                                            SafeLog($"[{DateTime.Now:HH:mm:ss}] WARN: pending_request returned 204 but jobs appear ENQUEUED. Will attempt quick rebind.");
                                            TryRebindPortFromLock();
                                        }
                                    }
                                }
                                catch { /* best-effort diagnostic */ }
                            }
                            goto AfterExec;
                        }

                        // 仕事あり
                        gotWork = true;
                        var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                        SafeLog($"[{DateTime.Now:HH:mm:ss}] POLL BATCH: {TruncateForLog(body, 500)}");

                        var json = NormalizeIncomingJson(body);
                        parsedCommandCount = CountCommandsFromJson(json);

                        if (json.StartsWith("["))
                        {
                            List<RequestCommand> cmdList = null;
                            try
                            {
                                cmdList = JsonConvert.DeserializeObject<List<RequestCommand>>(json)
                                        ?? new List<RequestCommand>();
                            }
                            catch (JsonException je)
                            {
                                // JSON不正は post_result で掃除
                                await PostParseErrorAndContinue(json, je).ConfigureAwait(false);
                                goto AfterExec;
                            }

                            foreach (var cmd in cmdList)
                            {
                                if (cmd == null) continue;
                                _executor.SetCommand(cmd);
                                var __opMs = ResolveOpTimeoutMsFromParams(cmd, 120_000);
                                _executor.SetNextTimeoutMs(__opMs);
                                // start heartbeat only for long ops (>=15s)
                                if (__opMs >= 15_000)
                                {
                                    string __rpcId = null; try { __rpcId = cmd.Id != null ? cmd.Id.ToString() : null; } catch { }
                                    StartHeartbeat(__rpcId, jobIdHeader);
                                }

                                SafeLog($"[{DateTime.Now:HH:mm:ss}] SET&RAISE (batch): {cmd.Command} id={cmd.Id}");
                                _extEvent.Raise();
                            }
                        }
                        else
                        {
                            RequestCommand cmd = null;
                            try
                            {
                                cmd = JsonConvert.DeserializeObject<RequestCommand>(json);
                            }
                            catch (JsonException je)
                            {
                                await PostParseErrorAndContinue(json, je).ConfigureAwait(false);
                                goto AfterExec;
                            }

                            if (cmd != null)
                            {
                                _executor.SetCommand(cmd);
                                var __opMs2 = ResolveOpTimeoutMsFromParams(cmd, 120_000);
                                TrySetNextTimeoutMs(_executor, __opMs2);
                                if (__opMs2 >= 15_000)
                                {
                                    string __rpcId2 = null; try { __rpcId2 = cmd.Id != null ? cmd.Id.ToString() : null; } catch { }
                                    StartHeartbeat(__rpcId2, jobIdHeader);
                                }

                                SafeLog($"[{DateTime.Now:HH:mm:ss}] SET&RAISE: {cmd.Command} id={cmd.Id}");
                                _extEvent.Raise();
                            }
                            else
                            {
                                await PostParseErrorAndContinue(json, new JsonException("Empty command after deserialization")).ConfigureAwait(false);
                            }
                        }
            }
            catch (TaskCanceledException)
            {
                // HTTPタイムアウト
                AdaptiveWaitController.Instance.ReportTimeout();
                SafeLog($"[{DateTime.Now:HH:mm:ss}] WORKER WARNING: Request timeout.");
                TryRebindPortFromLock();
            }
            catch (Exception ex)
            {
                SafeLog($"[{DateTime.Now:HH:mm:ss}] WORKER ERROR: {ex}");
                TryRebindPortFromLock();
            }
                    finally
                    {
                        swLoop.Stop();
                    }

                AfterExec:
                    // 観測値をレポート
                    if (gotWork)
                    {
                        // ここでは“キュー投入までの時間”を代理実行時間として記録（実行自体は外部イベントで非同期）
                        AdaptiveWaitController.Instance.ReportPoll(true);
                        AdaptiveWaitController.Instance.ReportProcessed(Math.Max(1, parsedCommandCount), (int)swLoop.Elapsed.TotalMilliseconds);
                    }

                    // パラメータ調整
                    AdaptiveWaitController.Instance.AdjustIfNeeded();

                    // 5秒に1回サマリをログ
                    var now = DateTime.UtcNow;
                    if ((now - _lastPerfLog).TotalSeconds >= 5)
                    {
                        var (ms, mode, empty, pps, exec) = AdaptiveWaitController.Instance.Snapshot();
                        SafeLog($"[WAIT] mode={mode} delayMs={ms} empty={empty:0.00} avgExecMs={exec:0} thput={pps:0.0}/s");
                        _lastPerfLog = now;
                    }

                    // 次回までの待機（Auto/Manual両対応）
                    if (_isRunning)
                    {
                        // If long-polling was used, the server already provided the wait; skip extra delay
                        if (!usedLongPoll)
                        {
                            var delay = AdaptiveWaitController.Instance.GetCurrentDelayMs();
                            try { await Task.Delay(delay, _cts.Token).ConfigureAwait(false); }
                            catch (TaskCanceledException) { break; }
                        }
                    }
                }
            });
        }

        private void TryRebindPortFromLock()
        {
            try
            {
                int me = System.Diagnostics.Process.GetCurrentProcess().Id;
                string baseDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitMCP", "locks");
                if (!System.IO.Directory.Exists(baseDir)) return;
                foreach (var lockPath in System.IO.Directory.GetFiles(baseDir, "server_*.lock"))
                {
                    string txt;
                    try { txt = System.IO.File.ReadAllText(lockPath, Encoding.UTF8); } catch { continue; }
                    int owner = 0, port = 0;
                    foreach (var line in txt.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (line.StartsWith("ownerPid=") && int.TryParse(line.Substring(9), out var o)) owner = o;
                        if (line.StartsWith("port=") && int.TryParse(line.Substring(5), out var p)) port = p;
                    }
                    if (owner == me && port > 0)
                    {
                        _client.BaseAddress = new Uri($"http://localhost:{port}/");
                        try { RevitMCPAddin.AppServices.CurrentPort = port; } catch { }
                        SafeLog($"[{DateTime.Now:HH:mm:ss}] WORKER rebind to port {port} via lock.");
                        return;
                    }
                }
            }
            catch { /* ignore */ }
        }

        public void Stop()
        {
            _isRunning = false;
            try { _cts.Cancel(); } catch { }
        }

        private DateTime _lastPerfLog = DateTime.MinValue;


        // ------------------------------------------------------------
        //  ユーティリティ
        // ------------------------------------------------------------
        private static string TruncateForLog(string input, int maxLen)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return input.Length <= maxLen ? input : input.Substring(0, maxLen) + "...(truncated)";
        }

        private void SafeLog(string line)
        {
            RevitMCPAddin.Core.RevitLogger.Info(line);
        }

        private static string StripOuterSingleQuotes(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '\'' && s[s.Length - 1] == '\'')
                return s.Substring(1, s.Length - 2);
            return s;
        }

        private static string NormalizeIncomingJson(string body)
        {
            var json = (body ?? string.Empty).TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
            if (json.Length >= 2 && json[0] == '"' && json[json.Length - 1] == '"')
            {
                try
                {
                    var inner = JsonConvert.DeserializeObject<string>(json);
                    if (!string.IsNullOrEmpty(inner))
                        json = inner;
                }
                catch { /* ignore */ }
            }
            json = StripOuterSingleQuotes(json);
            return json;
        }

        private async Task PostParseErrorAndContinue(string rawJson, JsonException je)
        {
            var now = DateTime.Now;
            SafeLog($"[{now:HH:mm:ss}] WORKER ERROR: Invalid JSON format. {je.Message}");

            var idToken = TryExtractIdToken(rawJson);
            var errorPayload = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = idToken ?? JValue.CreateNull(),
                ["error"] = new JObject
                {
                    ["code"] = -32700,
                    ["message"] = "Parse error: invalid JSON",
                    ["data"] = new JObject
                    {
                        ["ok"] = false,
                        ["code"] = "INVALID_JSON",
                        ["hint"] = "送信側は必ずダブルクォートの正しいJSONを送ってください（Content-Type: application/json）。curlでは --data-raw と単一/二重引用符の扱いに注意。",
                        ["example"] = JObject.FromObject(new { jsonrpc = "2.0", method = "get_open_documents", @params = new { }, id = 1 }),
                        ["raw"] = TruncateForLog(rawJson, 200)
                    }
                },

            };

            try
            {
                var jsonBody = JsonConvert.SerializeObject(errorPayload, Formatting.None);
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                var res = await _client.PostAsync("post_result", content).ConfigureAwait(false);
                SafeLog($"[{now:HH:mm:ss}] WORKER post_result(parse-error) => {(int)res.StatusCode}");
            }
            catch (Exception postEx)
            {
                SafeLog($"[{now:HH:mm:ss}] WORKER ERROR: post_result failed: {postEx}");
            }
        }

        // ============================================================
        // per-request timeout: params.opTimeoutMs (ms) を読む（無ければ default）
        // ============================================================
        private static int ResolveOpTimeoutMsFromParams(object requestCmd, int @default = 120_000)
        {
            // 妥当域（UI待機の現実的な幅）
            const int MIN = 10_000;     // 10秒
            const int MAX = 3_600_000;  // 1時間

            // 共通パーサ
            int Clamp(int ms)
            {
                if (ms < MIN) ms = MIN;
                if (ms > MAX) ms = MAX;
                return ms;
            }
            bool TryParseAny(object obj, out int ms)
            {
                ms = 0;
                if (obj == null) return false;

                // JToken なら中身へ
                if (obj is Newtonsoft.Json.Linq.JToken tok)
                {
                    // 数値トークン系
                    if (tok.Type == Newtonsoft.Json.Linq.JTokenType.Integer) { ms = Clamp((int)tok); return true; }
                    if (tok.Type == Newtonsoft.Json.Linq.JTokenType.Float) { ms = Clamp((int)tok); return true; }
                    // 文字列なら数値に
                    if (tok.Type == Newtonsoft.Json.Linq.JTokenType.String)
                    {
                        int v; if (int.TryParse((string)tok, out v)) { ms = Clamp(v); return true; }
                    }
                    // それ以外は ToObject<int> を試す
                    try { var v = tok.ToObject<int>(); ms = Clamp(v); return v > 0; } catch { }
                    return false;
                }

                // 素の型
                if (obj is int i) { ms = Clamp(i); return i > 0; }
                if (obj is long l) { ms = Clamp(checked((int)l)); return l > 0; }
                if (obj is double d) { ms = Clamp((int)d); return d > 0; }
                if (obj is string s)
                {
                    int v; if (int.TryParse(s, out v)) { ms = Clamp(v); return v > 0; }
                    return false;
                }

                // 最後の手段：変換
                try { var v = Convert.ToInt32(obj); ms = Clamp(v); return v > 0; } catch { return false; }
            }

            try
            {
                // ---- ① params 内（JObject） ----
                var pProp = requestCmd?.GetType().GetProperty("Params");
                var pVal = pProp != null ? pProp.GetValue(requestCmd, null) : null;

                var jo = pVal as Newtonsoft.Json.Linq.JObject;
                if (jo != null)
                {
                    // 通常キー
                    var tok = jo["opTimeoutMs"];
                    int ms;
                    if (TryParseAny(tok, out ms)) return ms;

                    // 別名キー（snake_case 対応）
                    tok = jo["op_timeout_ms"];
                    if (TryParseAny(tok, out ms)) return ms;

                    // 文字列で来るケース（"900000"）を SelectToken で拾うことも可能
                    var sel = jo.SelectToken("opTimeoutMs");
                    if (TryParseAny(sel, out ms)) return ms;
                    sel = jo.SelectToken("op_timeout_ms");
                    if (TryParseAny(sel, out ms)) return ms;
                }

                // ---- ② トップレベル（RequestCommand に直でプロパティがある場合）----
                // 例: public int? OpTimeoutMs { get; set; }
                var topProp = requestCmd?.GetType().GetProperty("OpTimeoutMs");
                if (topProp != null)
                {
                    var topVal = topProp.GetValue(requestCmd, null);
                    int ms; if (TryParseAny(topVal, out ms)) return ms;
                }

                // ③ フィールドで持っている実装（稀）
                var topField = requestCmd?.GetType().GetField("OpTimeoutMs");
                if (topField != null)
                {
                    var topVal = topField.GetValue(requestCmd);
                    int ms; if (TryParseAny(topVal, out ms)) return ms;
                }
            }
            catch
            {
                // 失敗時は既定へフォールバック
            }

            return @default;
        }

        // ============================================================
        // Executor に「次回だけ適用するタイムアウト」を伝える（存在すれば反射で呼ぶ）
        // 既存機能を損なわないため、見つからなければ何もしない
        // ============================================================
        private static void TrySetNextTimeoutMs(object executor, int ms)
        {
            if (executor == null) return;
            try
            {
                var mi = executor.GetType().GetMethod(
                    "SetNextTimeoutMs",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                );
                if (mi != null)
                {
                    // ガード：最小10秒〜最大1時間
                    if (ms < 10_000) ms = 10_000;
                    if (ms > 3_600_000) ms = 3_600_000;
                    mi.Invoke(executor, new object[] { ms });
                }
            }
            catch
            {
                // 例外は握りつぶし：従来の既定タイムアウトで続行
            }
        }


        private static JToken TryExtractIdToken(string rawJson)
        {
            try
            {
                var tok = JToken.Parse(rawJson);
                if (tok.Type == JTokenType.Object)
                {
                    var id = tok["id"];
                    if (id != null) return id.DeepClone();
                }
                else if (tok.Type == JTokenType.String)
                {
                    var inner = tok.Value<string>();
                    if (!string.IsNullOrWhiteSpace(inner))
                    {
                        var tok2 = JToken.Parse(inner);
                        if (tok2.Type == JTokenType.Object)
                        {
                            var id2 = tok2["id"];
                            if (id2 != null) return id2.DeepClone();
                        }
                    }
                }
            }
            catch { /* ignore */ }

            try
            {
                var stripped = StripOuterSingleQuotes(rawJson);
                if (!string.Equals(stripped, rawJson, StringComparison.Ordinal))
                {
                    var tok3 = JToken.Parse(stripped);
                    if (tok3.Type == JTokenType.Object)
                    {
                        var id3 = tok3["id"];
                        if (id3 != null) return id3.DeepClone();
                    }
                }
            }
            catch { /* ignore */ }

            return null;
        }

        // JSONが単体/配列のどちらでも“コマンド個数”を返す簡易カウンタ
        private static int CountCommandsFromJson(string json)
        {
            try
            {
                var tok = JToken.Parse(json);
                if (tok.Type == JTokenType.Array) return ((JArray)tok).Count;
                if (tok.Type == JTokenType.Object) return 1;
            }
            catch { /* ignore */ }
            return 0;
        }

        private void Register(string method, IExternalEventHandler handler)
        {
            var ev = ExternalEvent.Create(handler);
            _extEvents[method] = (handler, ev);
        }

        private readonly Dictionary<string, (IExternalEventHandler handler, ExternalEvent ev)> _extEvents
            = new Dictionary<string, (IExternalEventHandler, ExternalEvent)>(StringComparer.Ordinal);

        // 受信したJSON-RPCを振り分ける想定の共通ルーター
        public bool TryRouteAndRaise(JObject incoming) // return: 受理したら true
        {
            var method = incoming?["method"]?.Value<string>();
            if (string.IsNullOrEmpty(method)) return false;

            if (!_extEvents.TryGetValue(method, out var pair))
                return false;

            // payloadを渡せる型なら流す（今回のListDuplicateElementsHandlerはSetPayloadあり）
            if (pair.handler is RevitMcpAddin.Commands.ListDuplicateElementsHandler h1)
                h1.SetPayload(incoming);
            // 他のハンドラでも同様に SetPayload(JObject) を持っていれば dynamic でもOK:
            // else if (pair.handler is dynamic dyn) dyn.SetPayload(incoming);

            pair.ev.Raise();
            return true;
        }

    }
}
