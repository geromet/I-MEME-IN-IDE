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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Media>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Path).IsRequired();
            entity.Property(m => m.Language).IsRequired();
            entity.Property(m => m.ContentHash).IsRequired();

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

            entity.HasIndex(p => new { p.WordId, p.Sequence });
        });

        modelBuilder.Entity<SearchHistoryEntry>(entity =>
        {
            entity.HasKey(h => h.Id);
            entity.Property(h => h.QueryText).IsRequired();
            entity.Property(h => h.Language).IsRequired();
            entity.Property(h => h.ScopeDescription).IsRequired();

            entity.HasIndex(h => h.SearchedAt);
        });
    }
}
