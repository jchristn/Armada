namespace Armada.Runtimes.Usage
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Porta.Pty;

    /// <summary>
    /// Isolated, bounded interactive quota reader. Sends exactly one slash command after a recognized idle
    /// prompt. Login, trust, consent and unknown screens end the attempt without an answer. No prompt is sent.
    /// </summary>
    public class InteractiveUsageReader
    {
        /// <summary>Last isolated helper PID, for cleanup verification.</summary>
        public int LastProcessId { get; private set; }

        /// <summary>Capture the runtime's built-in quota UI with a fixed terminal and locale.</summary>
        public async Task<UsageProbeResult> CaptureAsync(UsageProbeHost host, string accountKey, CancellationToken token = default)
        {
            if (!host.AllowPty) return UsageProbeResult.Unsupported("PtyDisabled");
            if (host.Runtime != AgentRuntimeEnum.ClaudeCode && host.Runtime != AgentRuntimeEnum.Gemini) return UsageProbeResult.Unsupported("PtyUnsupportedRuntime");
            bool claude = host.Runtime == AgentRuntimeEnum.ClaudeCode;
            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(host.TimeoutSeconds));
                IPtyConnection? terminal = null;
                try
                {
                    PtyOptions options = new PtyOptions
                    {
                        App = claude ? "claude" : "gemini",
                        CommandLine = claude ? new[] { "--settings", "{\"disableAllHooks\":true}", "--strict-mcp-config", "--mcp-config", "{}" } : Array.Empty<string>(),
                        Cwd = Path.GetTempPath(), Cols = 120, Rows = 40, Name = "xterm-256color",
                        Environment = new Dictionary<string, string> { ["TERM"] = "xterm-256color", ["LANG"] = "en_US.UTF-8", ["LC_ALL"] = "en_US.UTF-8", ["NO_COLOR"] = "1", ["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1" }
                    };
                    terminal = await PtyProvider.SpawnAsync(options, deadline.Token).ConfigureAwait(false);
                    LastProcessId = terminal.Pid;
                    using (deadline.Token.Register(() => Terminate(terminal)))
                    {
                        UsageTerminalScreen screen = new UsageTerminalScreen();
                        byte[] bytes = new byte[4096];
                        Decoder decoder = Encoding.UTF8.GetDecoder();
                        char[] chars = new char[4096];
                        int total = 0;
                        bool sent = false;
                        while (true)
                        {
                            int count = await terminal.ReaderStream.ReadAsync(bytes.AsMemory(), deadline.Token).ConfigureAwait(false);
                            if (count == 0) break;
                            total += count;
                            if (total > 256 * 1024) return UsageProbeResult.Failed("OversizeOutput");
                            int decoded = decoder.GetChars(bytes, 0, count, chars, 0);
                            screen.Append(chars, decoded);
                            string text = screen.Text;
                            if (Regex.IsMatch(text, "trust this|trust the files|sign in|log in|login with|choose.*theme|select.*theme|security notice|allow.*access|accept.*terms", RegexOptions.IgnoreCase))
                                return UsageProbeResult.Unsupported("PtyInteractionRequired", "sign in or complete CLI setup on the named host; the usage reader never answers setup screens");
                            if (!sent && IsIdle(text, claude))
                            {
                                byte[] command = Encoding.UTF8.GetBytes(claude ? "/usage\r" : "/stats\r");
                                await terminal.WriterStream.WriteAsync(command.AsMemory(), deadline.Token).ConfigureAwait(false);
                                await terminal.WriterStream.FlushAsync(deadline.Token).ConfigureAwait(false);
                                sent = true;
                            }
                            if (sent)
                            {
                                if (text.Contains("No API calls have been made in this session.", StringComparison.Ordinal)) return UsageProbeResult.Unsupported("PtyQuotaUnavailable");
                                UsageProbeResult parsed = ParseScreen(text, host.Runtime, DateTime.UtcNow);
                                if (parsed.Status == UsageSnapshotStatusEnum.Ok)
                                { parsed.AccountKey = accountKey; return parsed; }
                            }
                        }
                        return UsageProbeResult.Unsupported("PtyQuotaUnavailable");
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { return UsageProbeResult.Failed("PtyTimeout"); }
                catch (Exception ex) when (ex is IOException || ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is DllNotFoundException || ex is PlatformNotSupportedException)
                { return UsageProbeResult.Unsupported("PtyUnavailable"); }
                finally { if (terminal != null) { Terminate(terminal); terminal.Dispose(); } }
            }
        }

        /// <summary>Recognize only known idle inputs, never a login or confirmation prompt.</summary>
        public static bool IsIdle(string text, bool claude) => claude
            ? Regex.IsMatch(text, @"(?m)^\s*❯\s*(?:Try .*)?$")
            : text.Contains("Type your message", StringComparison.Ordinal) && text.Contains(">", StringComparison.Ordinal);

        /// <summary>Parse known percentage quota UI sections. Session token totals never become quota bars.</summary>
        public static UsageProbeResult ParseScreen(string text, AgentRuntimeEnum runtime, DateTime observed)
        {
            UsageProbeResult result = new UsageProbeResult { CapturedUtc = observed, Source = UsageCaptureSourceEnum.Pty, Provider = runtime == AgentRuntimeEnum.ClaudeCode ? "anthropic" : "google" };
            if (runtime == AgentRuntimeEnum.ClaudeCode)
            {
                MatchCollection matches = Regex.Matches(text, @"(?is)(Current session|Current week(?:\s*\([^\r\n]*\))?)[^%]{0,500}?([0-9]{1,3}(?:\.[0-9]+)?)%\s*used");
                foreach (Match match in matches)
                {
                    double percent = Double.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                    if (percent > 100 || result.Windows.Count >= 32) return UsageProbeResult.Failed("MalformedOutput");
                    result.Windows.Add(new UsageWindow { Name = match.Groups[1].Value, UsedPercent = percent, Unit = "percent" });
                }
            }
            else if (runtime == AgentRuntimeEnum.Gemini && text.Contains("Usage limit:", StringComparison.Ordinal) && text.Contains("Usage limits span all sessions", StringComparison.Ordinal))
            {
                Match quota = Regex.Match(text, @"(?i)([0-9]{1,3}(?:\.[0-9]+)?)%\s*used|Limit reached");
                if (quota.Success)
                {
                    double percent = quota.Groups[1].Success ? Double.Parse(quota.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 100;
                    if (percent > 100) return UsageProbeResult.Failed("MalformedOutput");
                    // The CLI rounds durations up to a minute; preserve that uncertainty and the source.
                    Match reset = Regex.Match(text, @"(?i)resets in\s+(?:(\d{1,3})h)?\s*(?:(\d{1,2})m)?");
                    DateTime? resetUtc = null;
                    if (reset.Success && (reset.Groups[1].Success || reset.Groups[2].Success))
                    {
                        int hours = reset.Groups[1].Success ? Int32.Parse(reset.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
                        int minutes = reset.Groups[2].Success ? Int32.Parse(reset.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
                        if (hours <= 168 && minutes < 60 && hours + minutes > 0) resetUtc = observed.AddHours(hours).AddMinutes(minutes);
                    }
                    result.Windows.Add(new UsageWindow { Name = "account_quota", UsedPercent = percent, Unit = "percent", ResetsUtc = resetUtc });
                }
            }
            if (result.Windows.Count == 0) return UsageProbeResult.Unsupported("PtyQuotaUnavailable");
            result.Status = UsageSnapshotStatusEnum.Ok;
            return result;
        }

        private static void Terminate(IPtyConnection terminal)
        {
            try { using (Process process = Process.GetProcessById(terminal.Pid)) { if (!process.HasExited) { process.Kill(true); process.WaitForExit(2000); } } }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            try { terminal.Kill(); } catch (InvalidOperationException) { }
        }
    }
}
