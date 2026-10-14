using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace CameraControl_WinUI.Deployment;

public sealed record ShellLinkInfo(string TargetPath, string AppUserModelId, string Arguments);

internal static class ShellLinkInterop
{
    private static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");
    private static readonly PropertyKey AppIdKey = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    internal static Task<T> OnStaAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                completion.TrySetResult(action());
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(cancellationToken); }
            catch (COMException error) { completion.TrySetException(new IOException("Die Windows-Verknüpfung konnte nicht bearbeitet werden.", error)); }
            catch (Exception error) { completion.TrySetException(error); }
        }) { IsBackground = true, Name = "CameraControl shell link" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    internal static void Write(string path, ShellLinkInfo info)
    {
        object instance = Create();
        try
        {
            var link = (IShellLinkW)instance;
            link.SetPath(info.TargetPath);
            link.SetWorkingDirectory(Path.GetDirectoryName(info.TargetPath)!);
            link.SetArguments(info.Arguments);
            link.SetDescription("Versammlungs-Kamerasteuerung");
            link.SetIconLocation(info.TargetPath, 0);
            link.SetShowCmd(1);
            var properties = (IPropertyStore)instance;
            PropertyKey key = AppIdKey;
            PropVariant value = PropVariant.String(info.AppUserModelId);
            try { properties.SetValue(ref key, ref value); properties.Commit(); }
            finally { PropVariantClear(ref value); }
            ((IPersistFile)instance).Save(path, true);
        }
        finally { Marshal.FinalReleaseComObject(instance); }
    }

    internal static ShellLinkInfo Read(string path)
    {
        object instance = Create();
        try
        {
            ((IPersistFile)instance).Load(path, 0);
            var link = (IShellLinkW)instance;
            var target = new StringBuilder(32768);
            var arguments = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 4);
            link.GetArguments(arguments, arguments.Capacity);
            PropertyKey key = AppIdKey;
            ((IPropertyStore)instance).GetValue(ref key, out PropVariant value);
            string appId;
            try { appId = value.Text; }
            finally { PropVariantClear(ref value); }
            return new ShellLinkInfo(target.ToString(), appId, arguments.ToString());
        }
        finally { Marshal.FinalReleaseComObject(instance); }
    }

    internal static void Notify(string directory) => SHChangeNotify(0x00001000, 0x0005, directory, IntPtr.Zero);
    private static object Create() => Activator.CreateInstance(Type.GetTypeFromCLSID(ShellLinkClass, throwOnError: true)!)
        ?? throw new IOException("Windows hat kein Shell-Link-Objekt erzeugt.");

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] private ushort type;
        [FieldOffset(8)] private IntPtr value;
        internal static PropVariant String(string text) => new() { type = 31, value = Marshal.StringToCoTaskMemUni(text) };
        internal readonly string Text => type == 31 ? Marshal.PtrToStringUni(value) ?? string.Empty : string.Empty;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId, uint flags, string item1, IntPtr item2);

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, IntPtr findData, uint flags);
        void GetIDList(out IntPtr item);
        void SetIDList(IntPtr item);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string path);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
