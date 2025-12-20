#nullable enable
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Misc
{
    public class GetLastSelectionCommand : IRevitCommandHandler
    {
        public string CommandName => "get_last_selection";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            int maxAgeMs = 0;
            try
            {
                var p = cmd?.Params as Newtonsoft.Json.Linq.JObject;
                maxAgeMs = System.Math.Max(0, p?.Value<int?>("maxAgeMs") ?? 0);
            }
            catch { }

            var snap = SelectionStash.GetSnapshot();
            var ageMs = (int)System.Math.Max(0, (System.DateTime.UtcNow - snap.ObservedUtc).TotalMilliseconds);
            bool fresh = (maxAgeMs <= 0) || (ageMs <= maxAgeMs);

            var snapNonEmpty = SelectionStash.GetLastNonEmptySnapshot();
            var ageNonEmptyMs = (int)System.Math.Max(0, (System.DateTime.UtcNow - snapNonEmpty.ObservedUtc).TotalMilliseconds);
            bool freshNonEmpty = (maxAgeMs <= 0) || (ageNonEmptyMs <= maxAgeMs);

            return new
            {
                ok = snap.Ids != null && snap.Ids.Length > 0 && fresh,
                elementIds = snap.Ids ?? System.Array.Empty<int>(),
                count = snap.Ids?.Length ?? 0,
                observedAtUtc = snap.ObservedUtc,
                ageMs,
                revision = snap.Revision,
                docPath = snap.DocPath,
                docTitle = snap.DocTitle,
                activeViewId = snap.ActiveViewId,
                hash = snap.Hash,
                lastNonEmpty = new
                {
                    ok = snapNonEmpty.Ids != null && snapNonEmpty.Ids.Length > 0 && freshNonEmpty,
                    elementIds = snapNonEmpty.Ids ?? System.Array.Empty<int>(),
                    count = snapNonEmpty.Ids?.Length ?? 0,
                    observedAtUtc = snapNonEmpty.ObservedUtc,
                    ageMs = ageNonEmptyMs,
                    revision = snapNonEmpty.Revision,
                    docPath = snapNonEmpty.DocPath,
                    docTitle = snapNonEmpty.DocTitle,
                    activeViewId = snapNonEmpty.ActiveViewId,
                    hash = snapNonEmpty.Hash
                }
            };
        }
    }
}
