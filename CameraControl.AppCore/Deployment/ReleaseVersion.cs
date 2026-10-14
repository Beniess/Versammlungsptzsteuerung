using System.Globalization;

namespace CameraControl.AppCore.Deployment;

public readonly record struct ReleaseVersion : IComparable<ReleaseVersion>
{
    private readonly int major;
    private readonly int minor;
    private readonly int build;
    private readonly int revision;

    private ReleaseVersion(int major, int minor, int build, int revision) =>
        (this.major, this.minor, this.build, this.revision) = (major, minor, build, revision);

    public static ReleaseVersion Parse(string value)
    {
        string[] parts = value?.Split('.') ?? [];
        if (parts.Length is not (3 or 4))
            throw new FormatException("Die Releaseversion muss drei oder vier numerische Teile enthalten.");
        Span<int> numbers = stackalloc int[4];
        numbers.Clear();
        for (int index = 0; index < parts.Length; index++)
        {
            string part = parts[index];
            if (part.Length == 0 || (part.Length > 1 && part[0] == '0') ||
                !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out numbers[index]))
                throw new FormatException("Die Releaseversion ist ungültig.");
        }
        return new ReleaseVersion(numbers[0], numbers[1], numbers[2], numbers[3]);
    }

    public int CompareTo(ReleaseVersion other)
    {
        int result = major.CompareTo(other.major);
        if (result == 0) result = minor.CompareTo(other.minor);
        if (result == 0) result = build.CompareTo(other.build);
        if (result == 0) result = revision.CompareTo(other.revision);
        return result;
    }

    public override string ToString() => revision == 0
        ? FormattableString.Invariant($"{major}.{minor}.{build}")
        : FormattableString.Invariant($"{major}.{minor}.{build}.{revision}");
}
