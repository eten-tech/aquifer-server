namespace Aquifer.Data.Schemas;

public sealed class BibleLicenseInfoSchema
{
    public required string Title { get; init; }
    public required LicenseCopyright Copyright { get; init; }
    public required IReadOnlyList<LicenseDetails> Licenses { get; init; }

    public sealed class LicenseCopyright
    {
        public string? Dates { get; set; }
        public required LicenseCopyrightHolder Holder { get; init; }
    }

    public sealed class LicenseCopyrightHolder
    {
        public required string Name { get; init; }

        // missing for some Bibles
        public string? Url { get; set; }
    }

    public sealed class LicenseDetails
    {
        public required LicenseData Eng { get; init; }
    }

    public sealed class LicenseData
    {
        public required string Name { get; init; }
        public required string Url { get; init; }
    }
}