using Aquifer.Data.EventHandlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aquifer.Data.Entities;

[EntityTypeConfiguration(typeof(BibleEntityConfiguration))]
public class BibleEntity : IHasUpdatedTimestamp
{
    public int Id { get; set; }
    public int LanguageId { get; set; }
    public string Name { get; set; } = null!;
    public string Abbreviation { get; set; } = null!;
    public string LicenseInfo { get; set; } = null!;
    public bool Enabled { get; set; }
    public bool LanguageDefault { get; set; }
    public bool RestrictedLicense { get; set; }
    public bool GreekAlignment { get; set; }
    public int ContentIteration { get; set; }

    [SqlDefaultValue("getutcdate()")]
    public DateTime Created { get; set; } = DateTime.UtcNow;

    public LanguageEntity Language { get; set; } = null!;
    public ICollection<BibleBookContentEntity> BibleBookContents { get; set; } = [];
    public ICollection<BibleVersionWordEntity> BibleVersionWords { get; set; } = [];

    [SqlDefaultValue("getutcdate()")]
    public DateTime Updated { get; set; } = DateTime.UtcNow;
}

public class BibleEntityConfiguration : IEntityTypeConfiguration<BibleEntity>
{
    public void Configure(EntityTypeBuilder<BibleEntity> builder)
    {
        builder.Property(b => b.LanguageDefault).HasDefaultValue(false);

        builder
            .HasIndex(x => x.LanguageId)
            .HasFilter($"{nameof(BibleEntity.LanguageDefault)} = 1")
            .HasDatabaseName("IX_Bibles_LanguageId_LanguageDefault")
            .IsUnique();
    }
}