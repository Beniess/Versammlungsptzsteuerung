using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using NUnit.Framework;

namespace CameraControl.WinUI.Tests;

[TestFixture, Category("LicenseDialog"), NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class NativeLicenseTests
{
    [Test]
    public void LicenseDialogRemainsReadableAndKeyboardScrollableAtMinimumSize()
    {
        using var qa = new NativeQaSession("license-dialog-minimum", "Light");
        qa.Find("NavigateSettings").Patterns.SelectionItem.Pattern.Select();
        NativeQaSession.WaitUntil(() => qa.Window.FindFirstDescendant(c => c.ByAutomationId("OpenLicenses")) is not null, "Die Lizenzaktion fehlt.");
        qa.Resize((int)Math.Ceiling(360 * qa.Scale), (int)Math.Ceiling(280 * qa.Scale));
        qa.Find("OpenLicenses").Patterns.Invoke.Pattern.Invoke();
        NativeQaSession.WaitUntil(() => qa.Window.FindFirstDescendant(c => c.ByAutomationId("LicensesDialog")) is { } candidate &&
            !candidate.IsOffscreen && candidate.BoundingRectangle.Width > 0 && candidate.BoundingRectangle.Height > 0, "Der native Lizenzdialog fehlt.");
        var dialog = qa.Find("LicensesDialog");
        var frame = qa.Window.BoundingRectangle;
        Assert.That(dialog.BoundingRectangle.Left, Is.GreaterThanOrEqualTo(frame.Left));
        Assert.That(dialog.BoundingRectangle.Right, Is.LessThanOrEqualTo(frame.Right + 1));
        Assert.That(dialog.BoundingRectangle.Top, Is.GreaterThanOrEqualTo(frame.Top));
        Assert.That(dialog.BoundingRectangle.Bottom, Is.LessThanOrEqualTo(frame.Bottom + 1));
        Assert.That(qa.Find("LicenseVersion").Name, Does.StartWith("Version "));
        qa.Find("LicenseEntry").AsComboBox().Select(1);
        NativeQaSession.WaitUntil(() => qa.Find("LicenseText").Name.Contains("Quellen", StringComparison.Ordinal), "Das eingebettete Quelldokument fehlt.");
        qa.Capture("sources-before-scroll");
        var scroll = qa.Find("LicenseDocumentScroll");
        Assert.That(scroll.BoundingRectangle.Height / qa.Scale, Is.GreaterThan(24));
        scroll.Focus();
        var before = scroll.Patterns.Scroll.Pattern.VerticalScrollPercent.Value;
        Keyboard.Type(VirtualKeyShort.NEXT);
        NativeQaSession.WaitUntil(() => scroll.Patterns.Scroll.Pattern.VerticalScrollPercent.Value > before, "Bild ab muss den Lizenztext tatsächlich scrollen.");
        qa.Capture("sources-keyboard-scroll");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        NativeQaSession.WaitUntil(() => qa.Window.FindFirstDescendant(c => c.ByAutomationId("LicensesDialog")) is not { } remaining || remaining.IsOffscreen,
            "Escape muss den nativen Dialog schließen.");
    }
}
