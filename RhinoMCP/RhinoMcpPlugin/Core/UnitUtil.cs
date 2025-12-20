namespace RhinoMcpPlugin.Core
{
    public static class UnitUtil
    {
        public static double FeetToMm(double v) => v * 304.8;
        public static double MmToFeet(double v) => v / 304.8;
    }
}
