using System.Collections.Generic;

namespace RevitMCPAddin.Models
{
    /// <summary>
    /// Request for creating new walls flush (face-aligned) to existing walls.
    /// Intended to be populated from JSON.
    /// </summary>
    public sealed class CreateFlushWallsRequest
    {
        /// <summary>Source wall element ids (int).</summary>
        public List<int> SourceWallIds { get; set; } = new List<int>();

        /// <summary>
        /// New wall type name (WallType.Name) or an ElementId integer string.
        /// Example: "(内壁)W5" or "123456".
        /// </summary>
        public string NewWallTypeNameOrId { get; set; } = string.Empty;

        /// <summary>
        /// Which side of the source wall to create on:
        /// - "ByGlobalDirection": choose side whose finish-face normal is closest to GlobalDirection
        /// - "ByExterior": use the wall's exterior side
        /// - "ByInterior": use the wall's interior side
        /// </summary>
        public string SideMode { get; set; } = "ByGlobalDirection";

        /// <summary>
        /// Only used when SideMode == "ByGlobalDirection".
        /// Example: [0,-1,0] means global -Y side.
        /// </summary>
        public double[] GlobalDirection { get; set; } = new double[] { 0, -1, 0 };

        /// <summary>
        /// Plane reference on the source wall (contact side):
        /// - "FinishFace"
        /// - "CoreFace"
        /// - "WallCenterline"
        /// - "CoreCenterline"
        /// </summary>
        public string SourcePlane { get; set; } = "FinishFace";

        /// <summary>
        /// Plane reference on the new wall (side facing the source). If empty, uses SourcePlane.
        /// </summary>
        public string NewPlane { get; set; } = string.Empty;

        /// <summary>
        /// New wall exterior direction mode:
        /// - "AwayFromSource": new exterior points away from the source (toward placement direction)
        /// - "MatchSourceExterior": match the source wall exterior direction
        /// - "OppositeSourceExterior": opposite to the source wall exterior direction
        /// </summary>
        public string NewExteriorMode { get; set; } = "MatchSourceExterior";

        /// <summary>Apply miter joints for a chain (Line-Line only).</summary>
        public bool MiterJoints { get; set; } = true;

        /// <summary>Copy base/top constraints (levels/offsets/height) from the source wall.</summary>
        public bool CopyVerticalConstraints { get; set; } = true;
    }
}

