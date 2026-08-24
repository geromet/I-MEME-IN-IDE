using MemeSearcher.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MemeSearcher.Infrastructure.Database;

public class MemeSearcherDbContext(DbContextOptions<MemeSearcherDbContext> options) : DbContext(options)
{
    public DbSet<Media> Media => Set<Media>();
    public DbSet<Transcript> Transcripts => Set<Transcript>();
    public DbSet<Segment> Segments => Set<Segment>();
    public DbSet<Word> Words => Set<Word>();
    public DbSet<Phone> Phones => Set<Phone>();
    public DbSet<SearchHistoryEntry> SearchHistory => Set<SearchHistoryEntry>();
    public DbSet<PhoneNGramPosting> PhoneNGramPostings => Set<PhoneNGramPosting>();
    public DbSet<Catalog> Catalogs => Set<Catalog>();
    public DbSet<CatalogMedia> CatalogMedia => Set<CatalogMedia>();
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<TemplateVariant> TemplateVariants => Set<TemplateVariant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Media>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Path).IsRequired();
            entity.Property(m => m.Language).IsRequired();
            entity.Property(m => m.ContentHash).IsRequired();

            // Milestone 13: an explicit fluent default (not just the C# property initializer) so
            // the migration backfills existing rows to "selected" too - the property initializer
            // only applies to newly-constructed-in-memory Media instances, not rows already in the
            // database when this column was added.
            entity.Property(m => m.IsSelectedForSearch).HasDefaultValue(true);

            // Recognizing "I already indexed this exact file" (addendum §3) relies on this.
            entity.HasIndex(m => m.ContentHash).IsUnique();
            entity.HasIndex(m => m.Path);

            entity.HasMany<Transcript>()
                .WithOne()
                .HasForeignKey(t => t.MediaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Transcript>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Source).IsRequired();
            entity.Property(t => t.Language).IsRequired();

            entity.HasIndex(t => t.MediaId);

            entity.HasMany(t => t.Segments)
                .WithOne()
                .HasForeignKey(s => s.TranscriptId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Segment>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Text).IsRequired();

            entity.HasIndex(s => new { s.TranscriptId, s.Sequence });

            entity.HasMany(s => s.Words)
                .WithOne()
                .HasForeignKey(w => w.SegmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Word>(entity =>
        {
            entity.HasKey(w => w.Id);
            entity.Property(w => w.Text).IsRequired();

            // Stored as the enum name, not its ordinal: a migration that reorders the enum must not
            // silently relabel every phone row in the corpus (#18).
            entity.Property(w => w.PhonemeAlphabet).HasConversion<string>().IsRequired();

            entity.HasIndex(w => new { w.SegmentId, w.Sequence });

            entity.HasMany(w => w.Phones)
                .WithOne()
                .HasForeignKey(p => p.WordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Phone>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Symbol).IsRequired();
            entity.Property(p => p.Alphabet).HasConversion<string>().IsRequired();

            entity.HasIndex(p => new { p.WordId, p.Sequence });
        });

        modelBuilder.Entity<SearchHistoryEntry>(entity =>
        {
            entity.HasKey(h => h.Id);
            entity.Property(h => h.ScopeDescription).IsRequired();

            entity.HasIndex(h => h.SearchedAt);

            // A deleted template's past runs are still a real fact about what was searched - the
            // history row survives with TemplateId cleared, not cascade-deleted (#21).
            entity.HasOne<Template>()
                .WithMany()
                .HasForeignKey(h => h.TemplateId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PhoneNGramPosting>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.NGram).IsRequired();

            // Lookup shape is always "this media's postings for one of these n-grams" (#9) - the
            // composite index covers that directly, and also enforces that a reindex can never
            // leave two rows claiming the same position for the same n-gram in the same media.
            entity.HasIndex(p => new { p.MediaId, p.NGram, p.StreamPosition }).IsUnique();

            entity.HasOne<Media>()
                .WithMany()
                .HasForeignKey(p => p.MediaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Catalog>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).IsRequired();
        });

        modelBuilder.Entity<CatalogMedia>(entity =>
        {
            entity.HasKey(cm => new { cm.CatalogId, cm.MediaId });

            // Deleting a catalog removes only these join rows (#20: "deleting a catalog must never
            // delete sources").
            entity.HasOne<Catalog>()
                .WithMany()
                .HasForeignKey(cm => cm.CatalogId)
                .OnDelete(DeleteBehavior.Cascade);

            // Deleting a source removes it from every catalog without orphaning rows (#20 exit
            // criterion) - the same cascade mechanism PhoneNGramPosting already relies on above.
            entity.HasOne<Media>()
                .WithMany()
                .HasForeignKey(cm => cm.MediaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Template>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Name).IsRequired();
            entity.Property(t => t.Mode).HasConversion<string>().IsRequired();

            // A template must survive its target catalog being deleted (#20/#21: a catalog
            // deletion never cascades into anything that merely references it) - it just falls
            // back to searching all indexed media.
            entity.HasOne<Catalog>()
                .WithMany()
                .HasForeignKey(t => t.TargetCatalogId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TemplateVariant>(entity =>
        {
            entity.HasKey(v => v.Id);
            entity.Property(v => v.Label).IsRequired();
            entity.Property(v => v.PhonesRaw).IsRequired();
            entity.Property(v => v.Alphabet).HasConversion<string>().IsRequired();

            entity.HasIndex(v => v.TemplateId);

            entity.HasOne<Template>()
                .WithMany()
                .HasForeignKey(v => v.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
