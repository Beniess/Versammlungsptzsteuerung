using System.Diagnostics;
using System.Text.Json;
using CameraControl.WinUI.Tests;

internal static class GuestPhotosAndPreview
{
    internal static void Run(string evidence)
    {
        if (!Environment.UserName.Equals("WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase) ||
            !Required("CAMERA_UI_EXE").Equals(@"C:\Payload\App\CameraControl.WinUI.exe", StringComparison.OrdinalIgnoreCase) ||
            !Required("CAMERA_UI_REPO_ROOT").Equals(@"C:\Payload\Repo", StringComparison.OrdinalIgnoreCase) ||
            !Required("CAMERA_UI_REPLACEMENT_DIRECTORY").Equals(@"C:\Payload\Replacement", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Der Foto-Modus benötigt den eigenen Sandbox-Gast und den festen schreibgeschützten Payload.");

        string root = Path.GetFullPath(evidence);
        Directory.CreateDirectory(root);
        string? previousEvidence = Environment.GetEnvironmentVariable("CAMERA_UI_EVIDENCE_DIRECTORY");
        var results = new List<CaseResult>();
        var tests = new NativePreviewTests();
        var cases = new (string Name, Action Execute)[]
        {
            ("01-real-photos-and-restart", tests.ThreeRealTestPhotosAppearOnPositionsAndSurviveRestart),
            ("02-ten-photo-cycles-errors-main-close", tests.TenNativePhotoCyclesPreservePairsAndMainCloseReleasesPlayer),
            ("03-modified-library-video-photo", tests.ModifiedLibraryFromOwnCacheShowsMarkerAndCapturesRealPng)
        };
        string? selection = Environment.GetEnvironmentVariable("CAMERA_UI_MEDIA_CASE");
        if (selection is not null and not "FirstPhotos" and not "Lifecycle") throw new InvalidOperationException("Unbekannter enger Foto-Nachlauf.");
        if (selection == "FirstPhotos") cases = [cases[0]];
        if (selection == "Lifecycle") cases = [cases[1]];
        try
        {
            foreach (var test in cases)
            {
                string folder = Path.Combine(root, test.Name);
                Directory.CreateDirectory(folder);
                Environment.SetEnvironmentVariable("CAMERA_UI_EVIDENCE_DIRECTORY", folder);
                var timer = Stopwatch.StartNew();
                string? failure = null;
                try { test.Execute(); }
                catch (Exception error) { failure = error.ToString(); }
                results.Add(new CaseResult(test.Name, failure is null, timer.ElapsedMilliseconds, failure));
                Console.WriteLine((failure is null ? "PASS " : "FAIL ") + test.Name);
                File.WriteAllText(Path.Combine(root, "photos-preview-results.json"), JsonSerializer.Serialize(new
                {
                    Cases = results, CompletedCases = results.Count, RequiredCases = cases.Length,
                    RequiredPhotoCycles = 10, FullMatrix = selection is null, RemainingRequiredMatrix = selection is not null,
                    SkipCount = 0, HostInputs = false, CameraConnections = false
                }, new JsonSerializerOptions { WriteIndented = true }));
                CopyCompletedCase(folder, Path.Combine(@"C:\Evidence", test.Name));
            }
        }
        finally { Environment.SetEnvironmentVariable("CAMERA_UI_EVIDENCE_DIRECTORY", previousEvidence); }
        if (results.Count != cases.Length || results.Any(value => !value.Passed))
            throw new InvalidOperationException("Mindestens ein echter Foto-, Vorschau- oder Austauschfall ist fehlgeschlagen. Siehe photos-preview-results.json.");
    }

    private static string Required(string name) => Path.GetFullPath(Environment.GetEnvironmentVariable(name) ??
        throw new InvalidOperationException("Der feste Gastpfad fehlt: " + name));

    private static void CopyCompletedCase(string source, string destination)
    {
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.GetFullPath(Path.Combine(destination, Path.GetRelativePath(source, file)));
            if (!target.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Der Belegpfad verlässt den eigenen gemappten Gastordner.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    private sealed record CaseResult(string Name, bool Passed, long Milliseconds, string? Error);
}
