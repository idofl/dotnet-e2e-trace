// Copyright 2021 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Google.Cloud.Logging.Console
{
    /// <summary>
    /// Console formatter for use with Google Cloud Logging.
    /// https://github.com/googleapis/google-cloud-dotnet/tree/master/apis/Google.Cloud.Logging
    /// </summary>
    public sealed class GoogleCloudConsoleFormatter : ConsoleFormatter, IDisposable
    {
        private readonly IDisposable _optionsReloadToken;
        private GoogleCloudConsoleFormatterOptions _options;

        /// <summary>
        /// Constructor accepting just an options, to simplify testing.
        /// </summary>
        /// <param name="options">The formatter options. Must not be null.</param>
        internal GoogleCloudConsoleFormatter(GoogleCloudConsoleFormatterOptions options)
            : base(nameof(GoogleCloudConsoleFormatter))
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _optionsReloadToken = null;
        }

        /// <summary>
        /// Constructs a new formatter which uses the specified monitor to retrieve
        /// options and watch for options changes.
        /// </summary>
        /// <param name="optionsMonitor">The monitor to observe for changes in options. Must not be null.</param>
        public GoogleCloudConsoleFormatter(IOptionsMonitor<GoogleCloudConsoleFormatterOptions> optionsMonitor)
            : this(optionsMonitor.CurrentValue)
        {
            _optionsReloadToken = optionsMonitor.OnChange(options => _options = options);
        }

        /// <inheritdoc />
        public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider scopeProvider, TextWriter textWriter)
        {
            string message = logEntry.Formatter(logEntry.State, logEntry.Exception);
            if (logEntry.Exception == null && message == null)
            {
                return;
            }
            using var output = new MemoryStream();
            {
                using var writer = new Utf8JsonWriter(output);
                {
                    writer.WriteStartObject();
                    writer.WriteString("message", message);
                    if (logEntry.Exception is object)
                    {
                        writer.WriteString("exception", logEntry.Exception.ToString());
                    }
                    writer.WriteString("severity", GetSeverity(logEntry.LogLevel));
                    writer.WriteString("activityId", System.Diagnostics.Activity.Current?.Id);
                    MaybeWriteTraceInformation(writer);

                    MaybeWriteKeyValuePairs(writer, logEntry.State, propertyName: "state");
                    MaybeWriteScopeInformation(writer, scopeProvider);
                    
                    writer.WriteEndObject();
                    writer.Flush();
                }
                textWriter.WriteLine(Encoding.UTF8.GetString(output.GetBuffer(), 0, (int) output.Position));
            }
        }

        /// <summary>
        /// Write Trace and Span IDs to the log if retrieval functions
        /// were provided.
        /// </summary>
        /// <param name="writer">JSON writer for output</param>
        private void MaybeWriteTraceInformation(Utf8JsonWriter writer)
        {
            if (_options.GetTraceID != null)
            {
                writer.WriteString("logging.googleapis.com/trace", _options.GetTraceID());
                if (_options.GetSpanID != null)
                {
                    writer.WriteString("logging.googleapis.com/spanId", _options.GetSpanID());
                }
            }
        }

        /// <summary>
        /// Write scoped data to the log
        /// </summary>
        /// <param name="writer">JSON writer for output</param>
        /// <param name="scopeProvider">Provider of scoped data</param>
        private void MaybeWriteScopeInformation(Utf8JsonWriter writer, IExternalScopeProvider scopeProvider)
        {
            if (!_options.IncludeScopes || scopeProvider is null)
            {
                return;
            }
            writer.WriteStartArray("scopes");
            scopeProvider.ForEachScope((scope, localWriter) =>
            {
                if (!MaybeWriteKeyValuePairs(localWriter, scope, propertyName: null))
                {
                    writer.WriteStringValue(ToInvariantString(scope));
                }
            }, writer);
            writer.WriteEndArray();
        }

        /// <summary>
        /// Write key/value pairs to the log
        /// </summary>
        /// <param name="writer">JSON writer for output</param>
        /// <param name="value">Key/value pairs to log</param>
        /// <param name="propertyName">Property to write the pairs under</param>
        /// <returns>
        /// True if any pairs were written to the log. False if no pairs were provided.
        /// </returns>
        private static bool MaybeWriteKeyValuePairs<TValue>(Utf8JsonWriter writer, TValue value, string propertyName)
        {
            if (value is IReadOnlyCollection<KeyValuePair<string, object>> stateDictionary)
            {
                if (propertyName is object)
                {
                    writer.WritePropertyName(propertyName);
                }
                writer.WriteStartObject();
                foreach (KeyValuePair<string, object> item in stateDictionary)
                {
                    writer.WriteString(item.Key, ToInvariantString(item.Value));
                }
                writer.WriteEndObject();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Convert .NET Logging log levels to Google Cloud Logging levels
        /// </summary>
        /// <param name="logLevel">Log level to convert</param>
        /// <returns>
        /// The corresponding Google Cloud Logging log level
        /// </returns>
        private static string GetSeverity(LogLevel logLevel) =>
            logLevel switch
            {
                LogLevel.Trace => "DEBUG",
                LogLevel.Debug => "DEBUG",
                LogLevel.Information => "INFO",
                LogLevel.Warning => "WARNING",
                LogLevel.Error => "ERROR",
                LogLevel.Critical => "CRITICAL",
                _ => throw new ArgumentOutOfRangeException(nameof(logLevel))
            };

        void IDisposable.Dispose() => _optionsReloadToken?.Dispose();

        private static string ToInvariantString(object obj) => 
            Convert.ToString(obj, CultureInfo.InvariantCulture);
    }
}