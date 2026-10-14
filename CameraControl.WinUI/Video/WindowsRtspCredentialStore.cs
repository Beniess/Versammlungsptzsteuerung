using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CameraControl_WinUI.Video;

public sealed class WindowsRtspCredentialStore : IRtspCredentialStore
{
    private const uint GenericCredential = 1;
    private const int NotFound = 1168;
    private readonly string expectedTarget;

    public WindowsRtspCredentialStore(bool isQa, string expectedTarget)
    {
        ArgumentNullException.ThrowIfNull(expectedTarget);
        bool valid = isQa
            ? expectedTarget.StartsWith("Versammlungsptzsteuerung/QA/", StringComparison.Ordinal) && expectedTarget.EndsWith("/RTSP", StringComparison.Ordinal) &&
                expectedTarget.Length == "Versammlungsptzsteuerung/QA/".Length + 64 + 5 &&
                expectedTarget["Versammlungsptzsteuerung/QA/".Length..^5] is { Length: 64 } hash && hash.All(char.IsAsciiHexDigit)
            : expectedTarget == "Versammlungsptzsteuerung/RTSP";
        if (!valid) throw new ArgumentException("Der Zugangsdatenbereich gehört nicht zu diesem Programmstart.", nameof(expectedTarget));
        this.expectedTarget = expectedTarget;
    }

    public Task SaveAsync(string target, string userName, string password, CancellationToken cancellationToken = default)
    {
        ValidateTarget(target, cancellationToken);
        ArgumentNullException.ThrowIfNull(password);
        if (string.IsNullOrWhiteSpace(userName) || userName.Contains('\0') || password.Contains('\0'))
            throw new ArgumentException("Die Zugangsdaten sind unvollständig oder enthalten ein ungültiges Zeichen.");
        byte[] bytes = Encoding.Unicode.GetBytes(password);
        if (bytes.Length > 2560) { CryptographicOperations.ZeroMemory(bytes); throw new ArgumentException("Das Kennwort ist für den Windows-Speicher zu lang."); }
        nint blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            if (bytes.Length > 0) Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential { Type = GenericCredential, TargetName = target, UserName = userName,
                CredentialBlobSize = (uint)bytes.Length, CredentialBlob = blob, Persist = 2 };
            if (!CredWrite(ref credential, 0)) throw Failure("speichern");
        }
        finally
        {
            for (int index = 0; index < bytes.Length; index++) Marshal.WriteByte(blob, index, 0);
            Marshal.FreeCoTaskMem(blob);
            CryptographicOperations.ZeroMemory(bytes);
        }
        return Task.CompletedTask;
    }

    public Task<RtspCredential?> ReadAsync(string target, CancellationToken cancellationToken = default)
    {
        ValidateTarget(target, cancellationToken);
        if (!CredRead(target, GenericCredential, 0, out nint pointer))
        {
            if (Marshal.GetLastWin32Error() == NotFound) return Task.FromResult<RtspCredential?>(null);
            throw Failure("lesen");
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlobSize > 2560 || credential.CredentialBlobSize % 2 != 0)
                throw new InvalidOperationException("Die gespeicherten Zugangsdaten sind nicht lesbar.");
            byte[] bytes = new byte[credential.CredentialBlobSize];
            try
            {
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return Task.FromResult<RtspCredential?>(new(credential.UserName ?? "", Encoding.Unicode.GetString(bytes)));
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally { CredFree(pointer); }
    }

    public Task DeleteAsync(string target, CancellationToken cancellationToken = default)
    {
        ValidateTarget(target, cancellationToken);
        if (!CredDelete(target, GenericCredential, 0) && Marshal.GetLastWin32Error() != NotFound) throw Failure("entfernen");
        return Task.CompletedTask;
    }

    private void ValidateTarget(string target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(target, expectedTarget, StringComparison.Ordinal))
            throw new InvalidOperationException("Die Zugangsdaten gehören nicht zum eigenen Programmbereich.");
    }

    private static InvalidOperationException Failure(string operation) =>
        new($"Windows konnte die Zugangsdaten nicht {operation}. Fehler {Marshal.GetLastWin32Error()}.");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out nint credential);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(nint pointer);
}
