#nullable enable
using System;
using System.Reflection;

// Revit API compatibility helpers.
// - `ElementId.IntegerValue` is deprecated in Revit 2024+ and may be removed in future versions.
// - `ElementId.Value` exists in Revit 2024+ and is the preferred API.
//
// This file keeps the rest of the codebase clean by providing `IntValue()` / `LongValue()` extensions.
namespace Autodesk.Revit.DB
{
    internal static class ElementIdCompatExtensions
    {
        private static readonly PropertyInfo? ValueProp;

        static ElementIdCompatExtensions()
        {
            try
            {
                ValueProp = typeof(ElementId).GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            }
            catch
            {
                ValueProp = null;
            }
        }

        public static long LongValue(this ElementId id)
        {
            if (id == null) return 0;

            try
            {
                if (ValueProp != null)
                {
                    var v = ValueProp.GetValue(id, null);
                    if (v is long l) return l;
                    if (v is int i) return i;
                    if (v != null) return Convert.ToInt64(v);
                }
            }
            catch
            {
                // ignore; fallback below
            }

#pragma warning disable 0618 // ElementId.IntegerValue is deprecated in Revit 2024+
            return id.IntegerValue;
#pragma warning restore 0618
        }

        public static int IntValue(this ElementId id)
        {
            var v = LongValue(id);
            if (v > int.MaxValue) return unchecked((int)v);
            if (v < int.MinValue) return unchecked((int)v);
            return (int)v;
        }

        // WorksetId is not an ElementId; provide a small compat shim for call sites that log IDs.
        public static long LongValue(this WorksetId id)
        {
            if (id == null) return 0;
            try { return id.IntegerValue; } catch { return 0; }
        }

        public static int IntValue(this WorksetId id)
        {
            var v = LongValue(id);
            if (v > int.MaxValue) return unchecked((int)v);
            if (v < int.MinValue) return unchecked((int)v);
            return (int)v;
        }
    }

    internal static class ElementIdCompat
    {
        private static readonly ConstructorInfo? CtorLong;

        static ElementIdCompat()
        {
            try
            {
                CtorLong = typeof(ElementId).GetConstructor(new[] { typeof(long) });
            }
            catch
            {
                CtorLong = null;
            }
        }

        public static ElementId From(long id)
        {
            try
            {
                if (CtorLong != null)
                    return (ElementId)CtorLong.Invoke(new object[] { id });
            }
            catch
            {
                // ignore; fallback below
            }

#pragma warning disable 0618 // ElementId.ElementId(int) is deprecated in Revit 2024+
            return new ElementId((int)id);
#pragma warning restore 0618
        }

        public static ElementId From(BuiltInCategory bic)
        {
            return From((long)bic);
        }

        public static ElementId From(BuiltInParameter bip)
        {
            return From((long)bip);
        }
    }
}
