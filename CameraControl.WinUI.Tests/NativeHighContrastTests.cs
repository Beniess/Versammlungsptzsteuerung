using System.Runtime.InteropServices;
using NUnit.Framework;

namespace CameraControl.WinUI.Tests;

[TestFixture, Category("HighContrast"), NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class NativeHighContrastTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void RealHighContrastPreservesTheFourTileLayout(bool minimum)
    {
        var contrast = new HighContrast { Size = (uint)Marshal.SizeOf<HighContrast>() };
        Assert.That(SystemParametersInfo(0x0042, contrast.Size, ref contrast, 0), Is.True);
        Assert.That((contrast.Flags & 1) != 0, Is.True, "Die echte Windows-Kontrastdarstellung muss aktiv sein.");
        new NativeLayoutTests().FourNativeTilesKeepNamesInsideTheirBounds("HighContrast", "long", minimum);
    }

    [StructLayout(LayoutKind.Sequential)] private struct HighContrast { public uint Size, Flags; public nint DefaultScheme; }
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, ref HighContrast data, uint flags);
}
