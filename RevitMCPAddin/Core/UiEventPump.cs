// ================================================================
// File   : Core/UiEventPump.cs  (Complete, safe-init + per-call timeout)
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// ================================================================
#nullable enable
using System;
using System.Threading.Tasks;
using Autodesk.Revit.UI;

namespace RevitMCPAddin.Core
{
    internal sealed class UiEventPump : IExternalEventHandler
    {
        private static readonly Lazy<UiEventPump> _lazy = new Lazy<UiEventPump>(() => new UiEventPump());
        public static UiEventPump Instance => _lazy.Value;

        private ExternalEvent _externalEvent;                     // Initialize() で生成
        private Func<UIApplication, object> _action;
        private TaskCompletionSource<object> _tcs;

        private static int _defaultTimeoutMs = 4000;              // 既定
        private static int _nextTimeoutMs = _defaultTimeoutMs; // “次回だけ”適用値

        public static void Initialize()
        {
            var inst = Instance;
            if (inst._externalEvent == null)
                inst._externalEvent = ExternalEvent.Create(inst);
        }

        public void SetNextTimeoutMs(int ms)
        {
            if (ms < 10_000) ms = 10_000;
            if (ms > 3_600_000) ms = 3_600_000;
            _nextTimeoutMs = ms;
        }

        private static int GetAndResetEffectiveTimeout(int requested)
        {
            int eff = (_nextTimeoutMs != _defaultTimeoutMs) ? _nextTimeoutMs : requested;
            _nextTimeoutMs = _defaultTimeoutMs;
            return eff;
        }

        private UiEventPump() { }

        public string GetName() => "RevitMCP UI Event Pump";

        public void Execute(UIApplication app)
        {
            var tcs = _tcs;
            var act = _action;
            _tcs = null;
            _action = null;

            if (tcs == null || act == null) return;

            try { tcs.TrySetResult(act(app)); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }

        public T Invoke<T>(Func<UIApplication, T> action, int timeoutMs = 4000)
        {
            if (_externalEvent == null)
            {
                try { _externalEvent = ExternalEvent.Create(this); }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "UiEventPump.Initialize() を OnStartup 等の標準API実行時に一度呼んでください（外部イベント中に ExternalEvent を生成しない）。", ex);
                }
            }

            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            _tcs = tcs;
            _action = app => (object)action(app);
            _externalEvent.Raise();

            int effTimeout = GetAndResetEffectiveTimeout(timeoutMs);
            if (!tcs.Task.Wait(effTimeout))
                throw new TimeoutException("UiEventPump.Invoke timed out.");

            if (tcs.Task.IsFaulted && tcs.Task.Exception != null)
                throw tcs.Task.Exception.InnerException ?? tcs.Task.Exception;

            return (T)tcs.Task.Result;
        }

        public bool InvokeSafe(Action<UIApplication> action, int timeoutMs = 4000)
        {
            try { Invoke(app => { action(app); return true; }, timeoutMs); return true; }
            catch { return false; }
        }

        public T InvokeSmart<T>(UIApplication uiapp, Func<UIApplication, T> action, int timeoutMs = 4000)
        {
            try { return action(uiapp); }
            catch { return Invoke(action, timeoutMs); }
        }

        public bool InvokeSmartSafe(UIApplication uiapp, Action<UIApplication> action, int timeoutMs = 4000)
        {
            try { action(uiapp); return true; }
            catch { return InvokeSafe(action, timeoutMs); }
        }
    }
}
