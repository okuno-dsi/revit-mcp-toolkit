// ================================================================
// File: Core/IRevitCommandHandler.cs
// ================================================================
using Autodesk.Revit.UI;

namespace RevitMCPAddin.Core
{
    /// <summary>各コマンドの実装クラスが実装するインターフェース</summary>
    public interface IRevitCommandHandler
    {
        /// <summary>コマンド名</summary>
        string CommandName { get; }

        /// <summary>
        /// コマンドを実行し、任意のオブジェクトを返す
        /// </summary>
        object Execute(UIApplication uiapp, RequestCommand cmd);
    }
}