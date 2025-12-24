#nullable enable
using System;

namespace RevitMCPAddin.Core.Failures
{
    /// <summary>
    /// Thread-local context used to let transaction-level code (TxnUtil / IFailuresPreprocessor)
    /// know the current failureHandling mode for the active MCP command execution.
    /// </summary>
    internal static class FailureHandlingContext
    {
        [ThreadStatic] private static FailureHandlingMode _mode;
        [ThreadStatic] private static bool _enabled;
        [ThreadStatic] private static CommandIssues? _issues;

        public static bool Enabled { get { return _enabled && _mode != FailureHandlingMode.Off; } }
        public static FailureHandlingMode Mode { get { return _mode; } }

        public static CommandIssues Issues
        {
            get
            {
                if (_issues == null) _issues = new CommandIssues();
                return _issues;
            }
        }

        public static IDisposable Push(FailureHandlingMode mode, CommandIssues issues)
        {
            return new Scope(mode, issues);
        }

        private sealed class Scope : IDisposable
        {
            private readonly FailureHandlingMode _prevMode;
            private readonly bool _prevEnabled;
            private readonly CommandIssues? _prevIssues;
            private bool _disposed;

            public Scope(FailureHandlingMode mode, CommandIssues issues)
            {
                _prevMode = _mode;
                _prevEnabled = _enabled;
                _prevIssues = _issues;

                _mode = mode;
                _enabled = mode != FailureHandlingMode.Off;
                _issues = issues;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                _mode = _prevMode;
                _enabled = _prevEnabled;
                _issues = _prevIssues;
            }
        }
    }
}

