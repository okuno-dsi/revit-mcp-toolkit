#nullable enable
using System;
using System.IO;
using Autodesk.Revit.DB;

namespace SmartOpen
{
    public sealed class RvtInfo
    {
        public string Path { get; set; } = string.Empty;
        public string Name => System.IO.Path.GetFileName(Path);
        public long SizeBytes { get; set; }
        public DateTime LastWrite { get; set; }
        public string RevitBuild { get; set; } = string.Empty;
        public string Build => RevitBuild;
        public bool IsWorkshared { get; set; }
        public bool IsCentral { get; set; }
        public string CentralPath { get; set; } = string.Empty;
        public int LinkCount { get; set; }
        public string? Warning { get; set; }

        public string SizeMB => $"{Math.Round(SizeBytes / (1024.0 * 1024.0), 1)} MB";
        public string Modified => LastWrite.ToString("yyyy-MM-dd HH:mm");
    }

    public static class RvtInfoUtil
    {
        public static RvtInfo? TryGet(string filePath)
        {
            try
            {
                var fi = new FileInfo(filePath);
                if (!fi.Exists) return null;

                var basic = BasicFileInfo.Extract(filePath);
                var info = new RvtInfo
                {
                    Path = filePath,
                    SizeBytes = fi.Length,
                    LastWrite = fi.LastWriteTime,
                    RevitBuild = basic.Format,
                    IsWorkshared = basic.IsWorkshared,
                    IsCentral = basic.IsCentral,
                    CentralPath = basic.CentralPath
                };

                try
                {
                    var mp = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);
                    var td = TransmissionData.ReadTransmissionData(mp);
                    if (td != null)
                    {
                        int cnt = 0;
                        foreach (var id in td.GetAllExternalFileReferenceIds())
                        {
                            var r = td.GetLastSavedReferenceData(id);
                            if (r != null) cnt++;
                        }
                        info.LinkCount = cnt;
                    }
                }
                catch
                {
                    info.Warning = "Unable to read TransmissionData (older file or inaccessible).";
                }

                return info;
            }
            catch
            {
                return null;
            }
        }
    }
}
