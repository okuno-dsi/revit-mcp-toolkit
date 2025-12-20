using System;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace RhinoMcpPlugin.Core.UserData
{
    [System.Runtime.InteropServices.Guid("A1B4BDE5-1F0D-4C18-9B23-19F7EBFEE8F1")]
    public class RevitRefUserData : Rhino.DocObjects.Custom.UserData
    {
        public string RevitUniqueId { get; set; } = "";
        public Transform BaselineWorldXform { get; set; } = Transform.Identity;
        public string Units { get; set; } = "feet";
        public double ScaleToRhino { get; set; } = 304.8; // ft -> mm
        public string SnapshotStamp { get; set; } = "";
        public string GeomHash { get; set; } = "";

        public override bool ShouldWrite => true;
        protected override bool Write(Rhino.FileIO.BinaryArchiveWriter archive)
        {
            archive.WriteString(RevitUniqueId ?? "");
            archive.WriteTransform(BaselineWorldXform);
            archive.WriteString(Units ?? "feet");
            archive.WriteDouble(ScaleToRhino);
            archive.WriteString(SnapshotStamp ?? "");
            archive.WriteString(GeomHash ?? "");
            return true;
        }
        protected override bool Read(Rhino.FileIO.BinaryArchiveReader archive)
        {
            RevitUniqueId = archive.ReadString();
            BaselineWorldXform = archive.ReadTransform();
            Units = archive.ReadString();
            ScaleToRhino = archive.ReadDouble();
            SnapshotStamp = archive.ReadString();
            GeomHash = archive.ReadString();
            return true;
        }

        public static RevitRefUserData From(RhinoObject obj)
        {
            var g = obj?.Geometry?.UserData?.Find(typeof(RevitRefUserData)) as RevitRefUserData;
            if (g != null) return g;
            return obj?.Attributes?.UserData?.Find(typeof(RevitRefUserData)) as RevitRefUserData;
        }
    }
}
