// ================================================================
// File: Core/Errors/ErrorContracts.cs
// Purpose: JSON-RPC error.data の共通枠（機械可読＋人間可読）
// Target : .NET Framework 4.8 / C# 8
// ================================================================
using System.Collections.Generic;

namespace RevitMCPAddin.Core.Errors
{
    /// <summary>機械可読なエラー詳細</summary>
    public sealed class ErrorDetails
    {
        public string code { get; set; }          // 例: "ERR_PARAM_MISSING"
        public string hint { get; set; }          // 例: "Use 'elementId' instead of 'roomId'."
        public string humanMessage { get; set; }  // 人間向け短文（日本語可）
        public object details { get; set; }       // 追加ペイロード
    }

    /// <summary>JSON-RPC error.data へ格納するエンベロープ</summary>
    public sealed class ErrorDataEnvelope
    {
        public bool ok { get; set; } = false;
        public ErrorDetails error { get; set; } = new ErrorDetails();
        public string method { get; set; }
        public object inputUnits { get; set; }
        public object internalUnits { get; set; }
        public object issues { get; set; }
        public bool retriable { get; set; } = false;
        public List<string> expectedParams { get; set; }
        public List<string> aliasesTried { get; set; }
    }
}
