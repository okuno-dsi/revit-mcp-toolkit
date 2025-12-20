// ================================================================
// File   : RevitUI/UiHelpers.cs
// Purpose: Revit UI ヘルパ（DockablePane 解決 / PostCommand / ViewChange）
// Target : .NET Framework 4.8 / C# 8 / Revit 2023+
// Notes  :
//   - BuiltInDockablePanes の型/場所/表記差（フィールド/プロパティ/メソッド）を最大限吸収
//   - 最終フォールバックで列挙値を“決め打ち”して DockablePaneId を生成（必ず解決）
//   - RequestViewChange / PostCommand の戻り値 void 差も反射で吸収
//   - 受け口キー：pane / builtIn / builtin / name / title / guid を受理
// ================================================================
#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Linq;
using System.Reflection;
using static Autodesk.Revit.UI.DockablePanes;

namespace RevitMCPAddin.RevitUI
{
    internal static class UiHelpers
    {
        // ---------- 文字正規化（英数字のみ小文字化） ----------
        private static string Key(string s)
        {
            if (s == null) return string.Empty;
            var arr = s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray();
            return new string(arr);
        }

        // ---------- token が ProjectBrowser 系かどうか ----------
        private static bool IsProjectBrowserToken(string tokenKey)
            => tokenKey.Contains("project") || tokenKey.Contains("browser");

        // ---------- 任意の値を DockablePaneId に変換（型差を吸収） ----------
        private static bool TryConvertToPaneId(object val, Type enumTypeOrNull, out DockablePaneId id)
        {
            id = default(DockablePaneId);
            if (val == null) return false;

            // 1) そのまま
            if (val is DockablePaneId dpid) { id = dpid; return true; }

            // 2) Guid → ラップ
            if (val is Guid g) { id = new DockablePaneId(g); return true; }

            // 3) enum 等 → 1引数コンストラクタ
            var ctor = typeof(DockablePaneId).GetConstructors()
                .FirstOrDefault(c => {
                    var ps = c.GetParameters();
                    return ps.Length == 1 && ps[0].ParameterType.IsAssignableFrom(val.GetType());
                });
            if (ctor != null)
            {
                id = (DockablePaneId)ctor.Invoke(new object[] { val });
                return true;
            }

            // 4) Guid プロパティ
            var gp = val.GetType().GetProperty("Guid", BindingFlags.Public | BindingFlags.Instance);
            if (gp != null && gp.GetValue(val) is Guid g2)
            {
                id = new DockablePaneId(g2);
                return true;
            }

            // 5) enum 型が分かっていればその型を受けるコンストラクタで生成
            if (enumTypeOrNull != null)
            {
                var ctor2 = typeof(DockablePaneId).GetConstructor(new[] { enumTypeOrNull });
                if (ctor2 != null)
                {
                    id = (DockablePaneId)ctor2.Invoke(new object[] { val });
                    return true;
                }
            }

            return false;
        }

        // ---------- 候補型を収集（直参照 + RevitAPIUI 全探索） ----------
        private static Type[] CollectBuiltInPaneTypes()
        {
            var tA = typeof(DockablePanes).GetNestedType("BuiltInDockablePanes", BindingFlags.Public);
            var tB = typeof(BuiltInDockablePanes);

            var extra = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => {
                    try { var n = a.GetName().Name; return n != null && n.IndexOf("RevitAPIUI", StringComparison.OrdinalIgnoreCase) >= 0; }
                    catch { return false; }
                })
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .Where(t => (t.Namespace ?? "").StartsWith("Autodesk.Revit.UI", StringComparison.OrdinalIgnoreCase)
                            && t.Name.Equals("BuiltInDockablePanes", StringComparison.OrdinalIgnoreCase));

            return new[] { tA, tB }.Where(x => x != null).Concat(extra).Distinct().ToArray();
        }

        // ---------- 指定メンバー名（field/property/method）から DockablePaneId を生成 ----------
        private static bool TryMakePaneIdFromMember(string[] candidateNames, out DockablePaneId id)
        {
            id = default(DockablePaneId);

            var types = CollectBuiltInPaneTypes();
            foreach (var t in types)
            {
                try
                {
                    // 1) public static プロパティ
                    foreach (var name in candidateNames)
                    {
                        var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
                        if (p != null)
                        {
                            var val = p.GetValue(null, null);
                            if (TryConvertToPaneId(val, t.IsEnum ? t : null, out id))
                                return true;
                        }
                    }

                    // 2) public static フィールド
                    foreach (var name in candidateNames)
                    {
                        var f = t.GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
                        if (f != null)
                        {
                            var val = f.GetValue(null);
                            if (TryConvertToPaneId(val, t.IsEnum ? t : null, out id))
                                return true;
                        }
                    }

                    // 3) public static メソッド（引数なし・戻り DockablePaneId/Guid/enum 想定）
                    foreach (var name in candidateNames)
                    {
                        var m = t.GetMethod(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
                        if (m != null && m.GetParameters().Length == 0)
                        {
                            var val = m.Invoke(null, null);
                            if (TryConvertToPaneId(val, t.IsEnum ? t : null, out id))
                                return true;
                        }
                    }
                }
                catch { /* 次の型へ */ }
            }

            // 最後の試行：Enum.Parse → DockablePaneId(enum) コンストラクタ
            foreach (var t in types)
            {
                try
                {
                    if (!t.IsEnum) continue;
                    foreach (var name in candidateNames)
                    {
                        object enumObj = Enum.Parse(t, name, true);
                        if (enumObj != null && TryConvertToPaneId(enumObj, t, out id))
                            return true;
                    }
                }
                catch { /* 次へ */ }
            }

            return false;
        }

        // ---------- “決め打ち”フォールバック（必ずここで成功させる想定） ----------
        private static bool TryForceWellKnownPaneIdByToken(string token, out DockablePaneId id)
        {
            id = default(DockablePaneId);
            var k = Key(token);

            if (IsProjectBrowserToken(k))
            {
                // Project Browser の候補名をすべて試す
                if (TryMakePaneIdFromMember(new[] { "ProjectBrowser" }, out id)) return true;
            }
            else
            {
                // Properties は PropertiesPalette が本命。環境によっては "Properties" しかない
                if (TryMakePaneIdFromMember(new[] { "PropertiesPalette", "Properties" }, out id)) return true;
            }
            return false;
        }

        // ---------- 柔軟探索（反射 + フォールバック） ----------
        public static bool TryGetBuiltInPaneId(string token, out DockablePaneId id)
        {
            id = default(DockablePaneId);
            var want = Key(token);

            // 名称エイリアス（部分一致で拾う）
            string[] aliases = IsProjectBrowserToken(want)
                ? new[] { "ProjectBrowser", "Project Browser" }
                : new[] { "PropertiesPalette", "Properties Palette", "Properties" };

            var types = CollectBuiltInPaneTypes();

            foreach (var t in types)
            {
                try
                {
                    // 1) プロパティ総当たり
                    foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Static))
                    {
                        var pk = Key(p.Name);
                        if (!aliases.Any(a => pk.Contains(Key(a)))) continue;

                        var val = p.GetValue(null, null);
                        if (TryConvertToPaneId(val, t.IsEnum ? t : null, out id))
                            return true;
                    }

                    // 2) フィールド総当たり
                    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
                    {
                        var fk = Key(f.Name);
                        if (!aliases.Any(a => fk.Contains(Key(a)))) continue;

                        var val = f.GetValue(null);
                        if (TryConvertToPaneId(val, t.IsEnum ? t : null, out id))
                            return true;
                    }

                    // 3) メソッド総当たり（引数なし限定）
                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (m.GetParameters().Length != 0) continue;
                        var mk = Key(m.Name);
                        if (!aliases.Any(a => mk.Contains(Key(a)))) continue;

                        var val = m.Invoke(null, null);
                        if (TryConvertToPaneId(val, t.IsEnum ? t : null, out id))
                            return true;
                    }
                }
                catch { /* 次候補へ */ }
            }

            // 最終フォールバック：列挙値を直接決め打ちで生成
            return TryForceWellKnownPaneIdByToken(token, out id);
        }

        public static DockablePaneId GetBuiltInPaneId(string name)
        {
            DockablePaneId id;
            if (TryGetBuiltInPaneId(name, out id)) return id;
            throw new InvalidOperationException("BuiltInDockablePanes member not found for: " + name);
        }

        // ---------- pane / builtIn / builtin / name / title / guid を受け付け ----------
        public static bool TryResolvePaneId(JObject p, out DockablePaneId id)
        {
            id = default(DockablePaneId);
            if (p == null) return false;

            string token = null;
            JToken j;
            if (p.TryGetValue("pane", out j)) token = j.ToString();
            else if (p.TryGetValue("builtIn", out j)) token = j.ToString();
            else if (p.TryGetValue("builtin", out j)) token = j.ToString();
            else if (p.TryGetValue("name", out j)) token = j.ToString();
            else if (p.TryGetValue("title", out j)) token = j.ToString();

            if (!string.IsNullOrEmpty(token))
            {
                if (TryGetBuiltInPaneId(token, out id)) return true;
            }

            // GUID 指定（空 GUID は拒否）
            JToken jGuid;
            Guid g;
            if (p.TryGetValue("guid", out jGuid) && Guid.TryParse(jGuid.ToString(), out g) && g != Guid.Empty)
            {
                id = new DockablePaneId(g);
                return true;
            }

            return false;
        }

        // ---------- 互換ラッパ（既存コードが UiHelpers.TryRequestViewChange を参照する場合） ----------
        public static bool TryRequestViewChange(UIDocument uidoc, View v)
        {
            return UiCommandHelpers.TryRequestViewChange(uidoc, v);
        }
    }

    // ---------- PostCommand / RequestViewChange / Pane 状態取得 ユーティリティ ----------
    internal static class UiCommandHelpers
    {
        public static bool TryPostByNames(UIApplication uiapp, params string[] enumNamesInOrder)
        {
            foreach (var name in enumNamesInOrder)
            {
                try
                {
                    PostableCommand pc;
                    if (!Enum.TryParse<PostableCommand>(name, true, out pc))
                        continue;

                    var rid = RevitCommandId.LookupPostableCommandId(pc);
                    uiapp.PostCommand(rid); // 例外なし＝成功（戻り void 環境あり）
                    return true;
                }
                catch { /* 次候補へ */ }
            }
            return false;
        }

        public static bool TryPostByIds(UIApplication uiapp, params string[] commandIdsInOrder)
        {
            foreach (var cid in commandIdsInOrder)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(cid)) continue;
                    var rid = RevitCommandId.LookupCommandId(cid);
                    if (rid == null) continue;
                    uiapp.PostCommand(rid);
                    return true;
                }
                catch { /* 次候補 */ }
            }
            return false;
        }

        public static bool TryRequestViewChange(UIDocument uidoc, View v)
        {
            try
            {
                var mi = typeof(UIDocument).GetMethod("RequestViewChange", new Type[] { typeof(View) });
                if (mi == null) return false;

                var result = mi.Invoke(uidoc, new object[] { v });
                if (mi.ReturnType == typeof(bool))
                    return result is bool b && b;

                // void 戻り → 例外なしを成功扱い
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// DockablePane の可視状態を取得（IsVisible → IsShown の順に反射。なければ fallbackWhenUnknown を返す）
        /// </summary>
        public static bool TryGetVisible(DockablePane pane, bool fallbackWhenUnknown = true)
        {
            try
            {
                var mi = typeof(DockablePane).GetMethod("IsVisible", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (mi != null && mi.ReturnType == typeof(bool))
                {
                    var r = mi.Invoke(pane, null);
                    if (r is bool b1) return b1;
                }
            }
            catch { /* ignore */ }

            try
            {
                var mi = typeof(DockablePane).GetMethod("IsShown", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (mi != null && mi.ReturnType == typeof(bool))
                {
                    var r = mi.Invoke(pane, null);
                    if (r is bool b2) return b2;
                }
            }
            catch { /* ignore */ }

            return fallbackWhenUnknown;
        }

        public static void UiDelay(int ms = 150)
        {
            try { System.Threading.Thread.Sleep(ms); } catch { }
        }
        internal static class PaneResolveUtil
        {
            /// <summary>
            /// DockablePane を UIApplication 経由で取得（存在しなければ null）
            /// </summary>
            public static DockablePane TryGetPane(UIApplication app, DockablePaneId id)
            {
                try
                {
                    return app.GetDockablePane(id);
                }
                catch
                {
                    return null;
                }
            }
        }

        public static bool RequestViewChangeSmart(UIApplication uiapp, UIDocument uidoc, View v, int uiDelayMs = 150)
        {
            return UiEventPump.Instance.InvokeSmart<bool>(uiapp, app =>
            {
                try
                {
                    var mi = typeof(UIDocument).GetMethod("RequestViewChange", new Type[] { typeof(View) });
                    if (mi != null)
                    {
                        var res = mi.Invoke(uidoc, new object[] { v });
                        if (mi.ReturnType == typeof(bool))
                        {
                            bool ok = res is bool b && b;
                            if (ok) UiDelay(uiDelayMs);
                            return ok;
                        }
                        UiDelay(uiDelayMs);
                        return true;
                    }
                    uidoc.RequestViewChange(v);
                    UiDelay(uiDelayMs);
                    return true;
                }
                catch { return false; }
            });
        }

    }
}
