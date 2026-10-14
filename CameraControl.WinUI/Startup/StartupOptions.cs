using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CameraControl.AppCore.Deployment;
using CameraControl.AppCore.Storage;
using CameraControl_WinUI.Deployment;

namespace CameraControl_WinUI.Startup;

public enum StartupKind { Development, UiQa, Deployment }
public enum DeploymentQaFault { None, FailBeforeReady, CancelAfterReady, RefuseExit }

public sealed record StartupOptions(StartupKind Kind, bool IsQa, bool IsCandidate, string DataDirectory,
    DeploymentScope? Scope, DeploymentQaFault Fault)
{
    public static StartupOptions Read(string[] arguments, bool deploymentRelease, string processPath,
        string applicationDirectory, Func<string, string?> environment)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (arguments.Length % 2 != 0) throw new IOException("Die Startargumente sind unvollständig.");
        for (int index = 0; index < arguments.Length; index += 2)
        {
            string key = arguments[index];
            if (key is not ("--deployment-candidate" or "--deployment-version" or "--deployment-parent" or "--deployment-qa-root") ||
                !values.TryAdd(key, arguments[index + 1])) throw new IOException("Die Startargumente sind nicht eindeutig.");
        }
        bool candidate = values.ContainsKey("--deployment-candidate");
        if (candidate) ValidateCandidateFields(values);
        else if (values.ContainsKey("--deployment-version") || values.ContainsKey("--deployment-parent"))
            throw new IOException("Die Kandidatenidentität fehlt.");
        if (!deploymentRelease && values.Count != 0) throw new IOException("Dieser Build besitzt keine lokale Programmeinrichtung.");

        string? qaMode = environment("CAMERA_CONTROL_QA_MODE");
        string? qaData = environment("CAMERA_CONTROL_QA_DATA_DIRECTORY");
        string? faultText = environment("CAMERA_CONTROL_QA_DEPLOYMENT_FAULT");
        if (qaMode is not (null or "1") || (qaMode == "1") != (qaData is not null))
            throw new IOException("Der QA-Modus benötigt einen eindeutigen eigenen Datenpfad.");
        if (qaData is not null) qaData = ValidateQaDataDirectory(qaData);
        if (values.TryGetValue("--deployment-qa-root", out string? qaRoot))
        {
            DeploymentScope scope = DeploymentScope.ForQa(qaRoot);
            string data = ValidateQaDataDirectory(Path.Combine(scope.QaRoot!, "Data"));
            if (qaData is not null && !DeploymentScope.SamePath(qaData, data))
                throw new IOException("Die QA-Datenpfade widersprechen sich.");
            return new(StartupKind.Deployment, true, candidate, data, scope, ParseFault(faultText));
        }
        if (faultText is not null) throw new IOException("Testfehler sind nur in einer ausdrücklichen lokalen QA-Einrichtung erlaubt.");
        if (qaData is not null)
        {
            if (candidate) throw new IOException("Der QA-Kandidat benötigt seinen eigenen Programmbereich.");
            return new(StartupKind.UiQa, true, false, qaData, null, DeploymentQaFault.None);
        }
        if (deploymentRelease)
        {
            string host = DeploymentScope.Absolute(processPath);
            if (!string.Equals(Path.GetExtension(host), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Der Start erfordert die veröffentlichte Host-EXE.");
            return new(StartupKind.Deployment, false, candidate, WindowsDataPath.GetDirectory(),
                DeploymentScope.ForCurrentUser(), DeploymentQaFault.None);
        }
        string developmentKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DeploymentScope.Absolute(applicationDirectory).ToUpperInvariant())))[..24];
        return new(StartupKind.Development, true, false,
            ValidateQaDataDirectory(Path.Combine(Path.GetTempPath(), "Versammlungsptzsteuerung-Development", developmentKey, "Data")), null, DeploymentQaFault.None);
    }

    private static void ValidateCandidateFields(Dictionary<string, string> values)
    {
        if (!Guid.TryParseExact(values["--deployment-candidate"], "N", out Guid token) || token == Guid.Empty ||
            !values.TryGetValue("--deployment-version", out string? version) ||
            !values.TryGetValue("--deployment-parent", out string? parent) ||
            !int.TryParse(parent, NumberStyles.None, CultureInfo.InvariantCulture, out int pid) || pid <= 0)
            throw new IOException("Die Kandidatenidentität ist ungültig.");
        try { ReleaseVersion.Parse(version); }
        catch (FormatException error) { throw new IOException("Die Kandidatenversion ist ungültig.", error); }
    }

    private static DeploymentQaFault ParseFault(string? value) => value switch
    {
        null => DeploymentQaFault.None,
        "fail-before-ready" => DeploymentQaFault.FailBeforeReady,
        "cancel-after-ready" => DeploymentQaFault.CancelAfterReady,
        "refuse-exit" => DeploymentQaFault.RefuseExit,
        _ => throw new IOException("Der QA-Testfehler ist nicht bekannt.")
    };

    private static string ValidateQaDataDirectory(string value)
    {
        string path = DeploymentScope.Absolute(value);
        string production = DeploymentScope.Absolute(WindowsDataPath.GetDirectory());
        if (DeploymentScope.SamePath(path, production) ||
            path.StartsWith(production + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            production.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("QA darf keinen Produktdatenpfad verwenden.");
        DeploymentScope.CheckPath(path);
        return path;
    }
}
