namespace Armada.Core.Services.FleetActionOutputParsers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Xml;
    using System.Xml.Linq;

    /// <summary>Known output formats. Unknown or incomplete responses never imply findings.</summary>
    public static class FleetActionParserRegistry
    {
        /// <summary>
        /// The <c>auto</c> parser id: the runner uses the parser that matches the vessel's resolved audit command (for
        /// example npm-audit-json for an npm repository, osv-scanner-json for the universal fallback).
        /// </summary>
        public const string AutoParserId = "auto";

        /// <summary>
        /// Whether an id names a registered parser or <see cref="AutoParserId"/>.
        /// </summary>
        /// <param name="id">Parser id.</param>
        /// <returns>True when known.</returns>
        public static bool IsKnown(string? id) => id == AutoParserId || Get(id) != null;

        /// <summary>Resolve a parser ID, or null for an unknown ID.</summary>
        public static IFleetActionOutputParser? Get(string? id) => id switch
        {
            "dotnet-vulnerable" => new DotnetVulnerableParser(),
            "npm-audit-json" => new NpmAuditJsonParser(),
            "pnpm-audit" => new PnpmAuditParser(),
            "yarn-audit" => new YarnAuditParser(),
            "pip-audit-json" => new PipAuditJsonParser(),
            "bundle-audit" => new BundleAuditParser(),
            "govulncheck-json" => new GovulncheckJsonParser(),
            "cargo-audit-json" => new CargoAuditJsonParser(),
            "composer-audit-json" => new ComposerAuditJsonParser(),
            "osv-scanner-json" => new OsvScannerJsonParser(),
            "gitleaks-json" => new GitleaksJsonParser(),
            "dotnet-test-trx" => new DotnetTestTrxParser(),
            "junit-xml" => new JunitXmlParser(),
            "go-test-json" => new GoTestJsonParser(),
            _ => null
        };

    }
}
