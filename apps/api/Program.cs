using ChatGPTVoiceCompanion.Data;
using ChatGPTVoiceCompanion.Options;
using ChatGPTVoiceCompanion.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpClient();
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalExtension", policy =>
    {
        policy
            .WithOrigins(
                "https://chat.openai.com",
                "https://chatgpt.com",
                "http://localhost:4200",
                "http://127.0.0.1:4200")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.Configure<ApiOptions>(
    builder.Configuration.GetSection(ApiOptions.SectionName));
builder.Services.Configure<SpeechOptions>(
    builder.Configuration.GetSection(SpeechOptions.SectionName));
builder.Services.Configure<StabilityOptions>(
    builder.Configuration.GetSection(StabilityOptions.SectionName));

builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("AppDb");
    options.UseSqlite(connectionString);
});

builder.Services.AddSingleton<ISpeechQueue, SpeechQueue>();
builder.Services.AddSingleton<IMessageDeduper, MessageDeduper>();
builder.Services.AddSingleton<IStabilityBuffer, StabilityBuffer>();
builder.Services.AddHostedService(sp => (SpeechQueue)sp.GetRequiredService<ISpeechQueue>());
builder.Services.AddHostedService(sp => (StabilityBuffer)sp.GetRequiredService<IStabilityBuffer>());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseCors("LocalExtension");
app.MapControllers();

app.Run();
