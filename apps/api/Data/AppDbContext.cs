using ChatGPTVoiceCompanion.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatGPTVoiceCompanion.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<SiteProfile> SiteProfiles => Set<SiteProfile>();
    public DbSet<ConversationSession> ConversationSessions => Set<ConversationSession>();
    public DbSet<CapturedMessage> CapturedMessages => Set<CapturedMessage>();
    public DbSet<SpeechEvent> SpeechEvents => Set<SpeechEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppSetting>()
            .HasIndex(x => x.Key)
            .IsUnique();

        modelBuilder.Entity<CapturedMessage>()
            .HasIndex(x => new { x.SessionId, x.TextHash, x.Role });
    }
}
