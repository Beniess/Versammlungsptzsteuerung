using System.Security.Cryptography;
using NUnit.Framework;
#if HAS_LICENSE_CATALOG
using CameraControl_WinUI;
#endif

namespace CameraControl.WinUI.Tests;

[TestFixture, Category("LicenseCatalog")]
public sealed class LicenseCatalogTests
{
#if HAS_LICENSE_CATALOG
    [Test]
    public void EmbeddedCatalogPreservesEveryIndexedOriginalAndItsHash()
    {
        var catalog = LicenseCatalog.Load();
        Assert.That(catalog.Components.Count, Is.EqualTo(26));
        var files = catalog.Components.SelectMany(component => component.Files).ToArray();
        Assert.That(files.Length, Is.EqualTo(48));
        Assert.That(files.Select(file => file.Path).Distinct().Count(), Is.EqualTo(files.Length));
        Assert.That(catalog.Components.Where(component => component.Name.StartsWith("VLC-Contrib-Quellen:", StringComparison.Ordinal))
            .All(component => !string.IsNullOrWhiteSpace(component.Scope)), Is.True);
        foreach (var file in files)
        {
            var bytes = catalog.ReadOriginal(file.Path);
            Assert.That(Convert.ToHexString(SHA256.HashData(bytes)), Is.EqualTo(file.Sha256).IgnoreCase, file.Path);
            if (file.ReadableText) Assert.That(catalog.ReadText(file.Path), Is.Not.Empty, file.Path);
            if (file.SourceArchive is not null) Assert.That(file.SourceEntry, Is.Not.Empty, file.Path);
        }
        Assert.That(files.Single(file => file.Path == "VLC-HRTF/Original-sofa-license-metadata.txt").Extraction, Is.Not.Empty);
        Assert.That(catalog.ReadText("Sources-and-replacement.md"), Does.Contain("offen"));
        Assert.That(catalog.ReadText("THIRD-PARTY-NOTICES.txt"), Is.Not.Empty);
    }
#else
    [Test] public void EmbeddedLicenseCatalogIsRequired() => Assert.Fail("Der eingebettete Lizenzkatalog und seine Originalressourcen fehlen.");
#endif
}
