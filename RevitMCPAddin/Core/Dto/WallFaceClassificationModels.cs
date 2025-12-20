#nullable enable
using System.Collections.Generic;

namespace RevitMCPAddin.Core.Dto
{
    public class WallFaceClassificationRequest
    {
        public List<int> ElementIds { get; set; } = new List<int>();

        public double OffsetMm { get; set; } = 1000.0;

        public bool RoomCheck { get; set; } = true;

        public double MinAreaM2 { get; set; } = 1.0;

        public bool IncludeGeometryInfo { get; set; } = false;

        public bool IncludeStableReference { get; set; } = true;
    }

    public class WallFaceNormalDto
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    public class WallFaceInfoDto
    {
        public int FaceIndex { get; set; }

        /// <summary>
        /// "exterior", "interior", "top", "bottom", "end", "other"
        /// </summary>
        public string Role { get; set; } = "other";

        public bool IsVertical { get; set; }

        public double? AreaM2 { get; set; }

        public WallFaceNormalDto? Normal { get; set; }

        public string? StableReference { get; set; }
    }

    public class WallFaceClassificationForElement
    {
        public int ElementId { get; set; }

        public List<WallFaceInfoDto> Faces { get; set; } = new List<WallFaceInfoDto>();
    }

    public class WallFaceClassificationError
    {
        public int ElementId { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class WallFaceClassificationResult
    {
        public bool ok { get; set; }
        public List<WallFaceClassificationForElement> walls { get; set; } = new List<WallFaceClassificationForElement>();
        public List<WallFaceClassificationError> errors { get; set; } = new List<WallFaceClassificationError>();
    }
}

