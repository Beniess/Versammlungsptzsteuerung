using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using NUnit.Framework;

namespace CameraControl.WinUI.Tests;

[TestFixture, Category("Layout"), NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class NativeLayoutTests
{
    [TestCase("Light", "empty", false)]
    [TestCase("Light", "empty", true)]
    [TestCase("Dark", "empty", false)]
    [TestCase("Dark", "empty", true)]
    [TestCase("Light", "long", false)]
    [TestCase("Light", "long", true)]
    [TestCase("Dark", "long", false)]
    [TestCase("Dark", "long", true)]
    public void FourNativeTilesKeepNamesInsideTheirBounds(string theme, string state, bool minimum)
    {
        using var qa = new NativeQaSession($"{theme}-{state}-{(minimum ? "minimum" : "target")}", theme, state);
        var width = (int)Math.Ceiling(360 * qa.Scale);
        var height = (int)Math.Ceiling(280 * qa.Scale);
        qa.Resize(minimum ? width : Math.Max(600, width), minimum ? height : Math.Max(430, height));
        qa.Capture("positions");
        AssertFourTileBounds(qa, state == "long");
    }

    public void CompactTargetAndMinimum(string theme)
    {
        using var qa = new NativeQaSession(theme + "-compact-matrix", theme, "long");
        var width = (int)Math.Ceiling(360 * qa.Scale);
        var height = (int)Math.Ceiling(280 * qa.Scale);
        qa.Resize(Math.Max(600, width), Math.Max(430, height));
        qa.Capture("positions-target");
        AssertFourTileBounds(qa, longNames: true);
        qa.Resize(width, height);
        qa.Capture("positions-minimum");
        AssertFourTileBounds(qa, longNames: true);
    }

    private static void AssertFourTileBounds(NativeQaSession qa, bool longNames)
    {
        var ids = new[] { "PresetLeser", "PresetTisch", "PresetRedner", "PresetHome" };
        var bounds = ids.Select(id => qa.Find(id).BoundingRectangle).ToArray();
        Assert.That(bounds[0].Y, Is.EqualTo(bounds[1].Y).Within(1));
        Assert.That(bounds[2].Y, Is.EqualTo(bounds[3].Y).Within(1));
        Assert.That(bounds[0].X, Is.EqualTo(bounds[2].X).Within(1));
        Assert.That(bounds[1].X, Is.EqualTo(bounds[3].X).Within(1));
        Assert.That(bounds[1].Left, Is.GreaterThan(bounds[0].Right));
        Assert.That(bounds[2].Top, Is.GreaterThan(bounds[0].Bottom));
        foreach (var id in ids)
        {
            var button = qa.Find(id);
            var name = qa.Find(id + "Name");
            var status = qa.Find(id + "State");
            Assert.That(button.IsOffscreen, Is.False);
            Assert.That(button.BoundingRectangle.Width / qa.Scale, Is.GreaterThan(120));
            Assert.That(button.BoundingRectangle.Height / qa.Scale, Is.GreaterThan(48));
            Assert.That(name.BoundingRectangle.Height, Is.GreaterThan(0));
            Assert.That(name.BoundingRectangle.Height / qa.Scale, Is.GreaterThanOrEqualTo(18), "Der Name benötigt mindestens eine vollständige native Textzeile.");
            Assert.That(name.BoundingRectangle.Top, Is.GreaterThanOrEqualTo(button.BoundingRectangle.Top));
            Assert.That(name.BoundingRectangle.Bottom, Is.LessThanOrEqualTo(button.BoundingRectangle.Bottom + 1));
            Assert.That(name.BoundingRectangle.Right, Is.LessThanOrEqualTo(button.BoundingRectangle.Right + 1));
            Assert.That(status.BoundingRectangle.Bottom, Is.LessThanOrEqualTo(button.BoundingRectangle.Bottom + 1), "Der sichtbare Zustand darf am Mindestmaß nicht abgeschnitten werden.");
            Assert.That(status.BoundingRectangle.Height / qa.Scale, Is.GreaterThanOrEqualTo(18), "Die Statuszeile benötigt ihre vollständige native Zeilenhöhe.");
            if (longNames && id != "PresetHome") Assert.That(button.Name, Does.Contain("Die ausführliche Bezeichnung"));
        }
    }

    [TestCase("Light")]
    [TestCase("Dark")]
    public void NavigationAndFocusRemainNative(string theme)
    {
        using var qa = new NativeQaSession(theme + "-navigation", theme);
        qa.Find("NavigateControl").Patterns.SelectionItem.Pattern.Select();
        NativeQaSession.WaitUntil(() => qa.Window.FindFirstDescendant(c => c.ByAutomationId("ControlPage")) is not null, "Die Steuerungsseite fehlt.");
        Assert.That(qa.Find("PanTiltPad").IsEnabled, Is.False, "Ein leerer QA-Start darf keine Kamera bedienen.");
        Assert.That(qa.Find("OpenPreview").IsOffscreen, Is.False);
        qa.Capture("control-empty");
        qa.Find("NavigateSettings").Patterns.SelectionItem.Pattern.Select();
        NativeQaSession.WaitUntil(() => qa.Window.FindFirstDescendant(c => c.ByAutomationId("ConnectionHost")) is not null, "Die Einstellungsseite fehlt.");
        qa.Find("ConnectionHost").Focus();
        var focused = qa.Automation.FocusedElement();
        Assert.That(focused, Is.Not.Null);
        Assert.That(qa.Automation.Compare(focused!, qa.Find("ConnectionHost")), Is.True);
        qa.Capture("settings-focus");
        qa.Find("NavigatePositions").Patterns.SelectionItem.Pattern.Select();
        NativeQaSession.WaitUntil(() => qa.Window.FindFirstDescendant(c => c.ByAutomationId("PresetHome")) is not null, "Die Positionsseite fehlt nach dem Seitenwechsel.");
        qa.Capture("positions-return");
    }

    [Test]
    public void InvalidSettingsExposeAnExplicitRepairAction()
    {
        using var qa = new NativeQaSession("invalid-settings", "Light", "invalid");
        qa.Find("NavigateSettings").Patterns.SelectionItem.Pattern.Select();
        NativeQaSession.WaitUntil(() => qa.Window.FindFirstDescendant(c => c.ByAutomationId("ResetSettings")) is { IsEnabled: true }, "Neue Einrichtung ist nicht erreichbar.");
        qa.Capture("settings-error");
        Assert.That(qa.Find("ConnectionSave").IsEnabled, Is.False);
    }

    [TestCase("Light")]
    [TestCase("Dark")]
    [Category("KeyboardFocus")]
    public void KeyboardTabMovesFocusToTheNextNativeField(string theme)
    {
        using var qa = new NativeQaSession(theme + "-keyboard-focus", theme);
        qa.Find("NavigateSettings").Patterns.SelectionItem.Pattern.Select();
        NativeQaSession.WaitUntil(() => qa.Window.FindFirstDescendant(c => c.ByAutomationId("ConnectionHost")) is { IsEnabled: true }, "Das Hostfeld ist nicht bereit.");
        qa.Find("ConnectionHost").Focus();
        var before = qa.Automation.FocusedElement();
        Keyboard.Type(VirtualKeyShort.TAB);
        var after = qa.Automation.FocusedElement();
        Assert.That(after, Is.Not.Null);
        Assert.That(qa.Window.FindAllDescendants().Any(element => qa.Automation.Compare(element, after!)), Is.True,
            "Der Fokus muss innerhalb der eigenen App bleiben.");
        Assert.That(qa.Automation.Compare(after!, before!), Is.False,
            "Tab muss den Fokus tatsächlich zum nächsten Feld bewegen.");
        qa.Capture("keyboard-focus");
    }
}
