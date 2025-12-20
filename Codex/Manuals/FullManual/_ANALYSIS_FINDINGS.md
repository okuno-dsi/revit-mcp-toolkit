# Analysis Findings

以下は自動解析で気付いた点です。必要に応じてレビュー・修正してください。
- INFO: Type DeleteTypeIfUnusedCommand declares multiple aliases: delete_type_if_unused, purge_unused_types, force_delete_type
- INFO: Type EditToposurfacePointsHandler declares multiple aliases: append_toposurface_points, replace_toposurface_points
- INFO: Type SiteCoordinatesHandler declares multiple aliases: set_project_base_point, set_survey_point, set_shared_coordinates_from_points, get_site_overview
- INFO: Type PropertyLineEditDeleteHandler declares multiple aliases: update_property_line, delete_property_line
- INFO: Type SiteComponentHandler declares multiple aliases: place_site_component, list_site_components, delete_site_component
- INFO: Type SiteSubRegionHandler declares multiple aliases: create_site_subregion_from_boundary, delete_site_subregion
- INFO: Type ParkingSpotHandler declares multiple aliases: place_parking_spot, list_parking_spots, delete_parking_spot
- INFO: Type TopographyInfoHandler declares multiple aliases: get_topography_info, list_topographies, get_site_subregions, set_topography_material, set_subregion_material
- INFO: Type ConstraintCommands declares multiple aliases: lock_constraint, unlock_constraint, set_alignment_constraint, update_dimension_value_if_temp_dim
- 改善提案: Manifest\ManifestExporter.BuildFromAssembly では CommandName のエイリアス区切り ("|") を分割していません。CommandRouter と同様に分割し、それぞれを個別に登録/公開すると、マニフェストの整合性が高まります。
- 改善提案: Manifest に ParamsSchema/ResultSchema の自動生成を追加すると、クライアント側のバリデーションとドキュメント品質が向上します（本ツールが抽出した `p.Value<T>("...")` などから推定可能）。
- 注意: RevitUI 直下にあるコマンドはカテゴリが `RevitUI` となるよう調整しました（ファイル名がカテゴリに含まれないよう修正済み）。
- Note: Purpose lines have been heuristically generated from command names (e.g., "get_...", "create_..."). Review edge cases.
- Note: Parameter types/defaults are inferred from p.Value<T>(...) usage and ?? defaults; required flags are inferred from "Missing '..." checks. Please review for commands with complex validation.
