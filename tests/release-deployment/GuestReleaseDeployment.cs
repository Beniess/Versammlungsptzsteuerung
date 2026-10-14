using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using CameraControl.WinUI.Tests;
using NUnit.Framework.Api;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

internal static class GuestReleaseDeployment
{
    internal static void Run(string executableA, string evidence, bool pipeRegression = false)
    {
        const string expectedA = @"C:\Payload\A\CameraControl.WinUI.exe";
        const string executableB = @"C:\Payload\B\CameraControl.WinUI.exe";
        if (!Environment.UserName.Equals("WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(executableA).Equals(expectedA, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(evidence).Equals(@"C:\Evidence\Release", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Der Release-Modus benötigt den eigenen Sandbox-Benutzer und die festen Gastpfade.");
        using (var document = JsonDocument.Parse(File.ReadAllText(@"C:\Evidence\release-environment.json")))
        {
            var environment = document.RootElement;
            if (environment.GetProperty("ComputerModel").GetString() != "Virtual Machine" ||
                !environment.GetProperty("PayloadReadOnly").GetBoolean())
                throw new InvalidOperationException("Der echte Gast- und Payload-Nachweis fehlt.");
        }
        if (FileVersionInfo.GetVersionInfo(executableA).FileVersion != "1.0.0.1" ||
            FileVersionInfo.GetVersionInfo(executableB).FileVersion != "1.0.0.2")
            throw new InvalidDataException("Die beiden freigegebenen QA-Dateiversionen stimmen nicht überein.");

        var names = new[] { "CAMERA_RELEASE_EXE_A", "CAMERA_RELEASE_EXE_B", "CAMERA_RELEASE_EVIDENCE_DIRECTORY" };
        var previous = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        var scopes = Path.Combine(Path.GetTempPath(), "CameraControl.Release.Tests");
        var before = ScopeDirectories(scopes);
        try
        {
            Directory.CreateDirectory(evidence);
            Environment.SetEnvironmentVariable(names[0], executableA);
            Environment.SetEnvironmentVariable(names[1], executableB);
            Environment.SetEnvironmentVariable(names[2], evidence);
            var runner = new NUnitTestAssemblyRunner(new DefaultTestAssemblyBuilder());
            runner.Load(typeof(ReleaseDeploymentTests).Assembly, new Dictionary<string, object>
            {
                ["NumberOfTestWorkers"] = 0
            });
            string[] focusedNames =
            [
                nameof(ReleaseDeploymentTests.NewDownloadedExeSwitchesToBAndTheRealLinkWorksWithoutDownloads),
                nameof(ReleaseDeploymentTests.TwoDownloadedBStartsLeaveOneCommittedWriterAndOneWindow)
            ];
            var selection = pipeRegression
                ? new XElement("or", focusedNames.Select(name => new XElement("test", typeof(ReleaseDeploymentTests).FullName + "." + name)))
                : new XElement("class", typeof(ReleaseDeploymentTests).FullName!);
            var filter = TestFilter.FromXml(TNode.FromXml(new XElement("filter", selection).ToString()));
            File.WriteAllText(Path.Combine(evidence, "release-discovery.xml"), runner.ExploreTests(filter).ToXml(true).OuterXml);
            int expectedCount = pipeRegression ? 2 : 7;
            if (runner.CountTestCases(filter) != expectedCount) throw new InvalidOperationException("Die vorhandene Release-Testklasse liefert nicht die genau ausgewählten Fälle.");
            var watch = Stopwatch.StartNew();
            var result = runner.Run(TestListener.NULL, filter);
            var xml = result.ToXml(true).OuterXml;
            File.WriteAllText(Path.Combine(evidence, "release-results.xml"), xml);
            var cases = XDocument.Parse(xml).Descendants("test-case").Select(node => new
            {
                Id = (string?)node.Attribute("id"), Name = (string?)node.Attribute("name"),
                Result = (string?)node.Attribute("result"), Duration = (string?)node.Attribute("duration"),
                Failure = (string?)node.Element("failure")?.Element("message")
            }).ToArray();
            string[] required =
            [
                nameof(ReleaseDeploymentTests.NewDownloadedExeSwitchesToBAndTheRealLinkWorksWithoutDownloads),
                nameof(ReleaseDeploymentTests.UnreachableLoopbackCameraDoesNotPreventTheStartupCommit),
                nameof(ReleaseDeploymentTests.TwoDownloadedBStartsLeaveOneCommittedWriterAndOneWindow),
                "FailedOrCancelledCandidatePreservesAAndUserData(\"fail-before-ready\")",
                "FailedOrCancelledCandidatePreservesAAndUserData(\"cancel-after-ready\")",
                nameof(ReleaseDeploymentTests.RefusedGracefulExitKeepsAAndDoesNotStartB),
                nameof(ReleaseDeploymentTests.LockedShellLinkPreservesAAndTheNextStartRecoversTheJournal)
            ];
            if (pipeRegression) required = focusedNames;
            var remaining = ScopeDirectories(scopes).Except(before, StringComparer.OrdinalIgnoreCase).ToArray();
            var allCasesPresent = cases.Select(item => item.Name).Order(StringComparer.Ordinal).SequenceEqual(required.Order(StringComparer.Ordinal));
            var passed = allCasesPresent && cases.Length == expectedCount && cases.All(item => item.Result == "Passed") &&
                result.PassCount == expectedCount && result.FailCount == 0 && result.SkipCount == 0 && result.InconclusiveCount == 0 && remaining.Length == 0;
            File.WriteAllText(Path.Combine(evidence, "release-summary.json"), JsonSerializer.Serialize(new
            {
                Passed = passed, Cases = cases, result.PassCount, result.FailCount, result.SkipCount, result.InconclusiveCount,
                Milliseconds = watch.ElapsedMilliseconds, BeforeScopes = before, RemainingOwnScopes = remaining,
                ExistingNUnitFixture = typeof(ReleaseDeploymentTests).FullName, FrameworkOwnsSetUpAndTearDown = true,
                Selection = pipeRegression ? "PipeRegression" : "All", ExpectedCount = expectedCount,
                SearchEvidence = false, SourceA = executableA, SourceB = executableB, HostInputs = false
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (!passed) throw new InvalidOperationException("Mindestens ein bestehender A/B-Fall oder seine Bereinigung ist im Gast fehlgeschlagen. Siehe release-results.xml und die Belege je Test-ID.");
        }
        finally { foreach (var name in names) Environment.SetEnvironmentVariable(name, previous[name]); }
    }

    private static string[] ScopeDirectories(string root) => Directory.Exists(root) ? Directory.GetDirectories(root) : [];
}
