// ============================================================================
// File: Core/TypeUtils.cs
// Revit 2023 / .NET Framework 4.8
// 概要：ElementType 複製ユーティリティ（重名時は連番で安全に再試行）
//  - GetDuplicatedTypeId(...) : Duplicate(...)の戻りが ElementType/ElementId どちらでも ElementId に正規化
//  - DuplicateTypeWithUniqueName(...) : 名前衝突を連番で回避しつつ複製
// ============================================================================

using System;
using System.Reflection;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Core
{
    public static class TypeUtils
    {
        /// <summary>
        /// ElementType.Duplicate(name) の戻り値が ElementType / ElementId どちらでも ElementId に正規化する。
        /// </summary>
        public static ElementId GetDuplicatedTypeId(ElementType srcType, string name)
        {
            if (srcType == null) throw new ArgumentNullException(nameof(srcType));

            // 通常: 2023 は ElementId 戻り。バージョン差を吸収するため反射で解決。
            object ret = srcType.Duplicate(name);

            if (ret is ElementId eid) return eid;
            if (ret is ElementType et) return et.Id;

            // プロパティ "Id" を持つ場合（保険）
            var idProp = ret?.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
            if (idProp?.GetValue(ret) is ElementId viaProp) return viaProp;

            throw new InvalidOperationException("Duplicate(name) の戻り値から ElementId を取得できませんでした。");
        }

        /// <summary>
        /// ElementType を複製し、重名の場合は "baseName 2", "baseName 3", ... と最大 maxTries 回まで再試行。
        /// 成功時 ElementId を返す。
        /// </summary>
        public static ElementId DuplicateTypeWithUniqueName(ElementType type, string baseName, Document doc, int maxTries = 20)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(baseName)) baseName = $"{type.Name} (clone)";
            if (maxTries < 1) maxTries = 1;

            string tryName = baseName.Trim();

            for (int i = 0; i < maxTries; i++)
            {
                try
                {
                    var newTypeId = GetDuplicatedTypeId(type, tryName);
                    if (newTypeId != null && newTypeId != ElementId.InvalidElementId)
                        return newTypeId;

                    // 念のため
                    tryName = $"{baseName} {i + 2}";
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    // 同名など → 連番で再試行
                    tryName = $"{baseName} {i + 2}";
                }
            }

            throw new InvalidOperationException($"タイプ複製に失敗しました: '{baseName}'（{maxTries} 回試行）");
        }
    }
}
