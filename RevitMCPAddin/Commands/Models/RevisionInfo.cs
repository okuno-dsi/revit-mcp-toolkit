using System.Collections.Generic;

namespace RevitMCPAddin.Models
{
    public class RevisionCloudBBox
    {
        public (double x, double y, double z) min { get; set; }
        public (double x, double y, double z) max { get; set; }
    }

    /// <summary>list_revisions の 1 要素と互換のDTO</summary>
    public class RevisionInfo
    {
        public int revisionId { get; set; }
        public string uniqueId { get; set; }
        public int sequenceNumber { get; set; }
        public string revisionNumber { get; set; }
        public string description { get; set; }
        public string revisionDate { get; set; }
        public bool issued { get; set; }
        public string issuedBy { get; set; }
        public string issuedTo { get; set; }
        public string visibility { get; set; }
        public int numberingSequenceId { get; set; }
        public int cloudCount { get; set; }
        // includeClouds=true の時のみ
        public List<object> clouds { get; set; }
    }

    /// <summary>list_sheet_revisions のシート1件</summary>
    public class SheetRevisionItem
    {
        public int sheetId { get; set; }
        public string sheetNumber { get; set; }
        public string sheetName { get; set; }
        public List<int> revisionIds { get; set; }
        // includeRevisionDetails=true の時のみ
        public List<RevisionBrief> revisions { get; set; }
    }

    /// <summary>シート側の簡易リビジョン詳細（list_sheet_revisions用）</summary>
    public class RevisionBrief
    {
        public int revisionId { get; set; }
        public string revisionNumber { get; set; }
        public int sequenceNumber { get; set; }
        public string description { get; set; }
        public string revisionDate { get; set; }
        public bool issued { get; set; }
    }
}
