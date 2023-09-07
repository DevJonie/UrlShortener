using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

builder.Services.AddScoped<UrlShorteningService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    app.ApplyMigrations();
}

app.MapPost("api/shorten", async (
    ShortenUrlRequest request,
    ApplicationDbContext dbContext,
    UrlShorteningService urlShorteningService,
    HttpContext httpContext) =>
{
    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _))
    {
        return Results.BadRequest("The specified URL is invalid.");
    }

    var code = await urlShorteningService.GenerateUniqueCodeAsync();

    var shortenedUrl =  ShortenedUrl.Create(
        longUrl: request.Url,
        code: code,
        shortUrl: $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/{code}");

    await dbContext.ShortenedUrls.AddAsync(shortenedUrl);

    await dbContext.SaveChangesAsync();
    
    return Results.Ok(shortenedUrl.ShortUrl);
});


app.MapGet("api/{code}", async (string code, ApplicationDbContext dbContext) =>
{
    var shortenedUrl = await dbContext.ShortenedUrls.FirstOrDefaultAsync(s => s.Code == code);

    if (shortenedUrl is null)
    {
        return Results.NotFound();
    }

    return Results.Redirect(shortenedUrl.LongUrl);
});

await app.RunAsync();


public class UrlShorteningService
{
    public const int NumberOfCharsInShortLink = 7;
    private const string AllowedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    private readonly Random _random = new();
    private readonly ApplicationDbContext _dbContext;

    public UrlShorteningService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<string> GenerateUniqueCodeAsync()
    {
        var codeCahrs = new char[NumberOfCharsInShortLink];

        while (true)
        {
            for (int i = 0; i < NumberOfCharsInShortLink; i++)
            {
                int randomIndex = _random.Next(AllowedChars.Length - 1);

                codeCahrs[i] = AllowedChars[randomIndex];
            }

            var code = new string(codeCahrs);

            if (!await _dbContext.ShortenedUrls.AnyAsync(s => s.Code == code))
            {
                return code;
            } 
        }
    }
}

public class ApplicationDbContext: DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<ShortenedUrl> ShortenedUrls { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShortenedUrl>(builder => 
        {
            builder.Property(s => s.Code).HasMaxLength(UrlShorteningService.NumberOfCharsInShortLink);
            builder.HasIndex(s => s.Code).IsUnique();
        });
    }
}

public class ShortenUrlRequest
{
    public string Url { get; set; } = string.Empty;
}

public class ShortenedUrl
{
    private ShortenedUrl() { }

    public Guid Id { get; private set; }
    public string LongUrl { get; private set; } = string.Empty;
    public string ShortUrl { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public DateTime CreatedDate { get; private set; }

    public static ShortenedUrl Create(string longUrl, string code, string shortUrl)
    {
        return new ShortenedUrl
        {
            Id = Guid.NewGuid(),
            CreatedDate = DateTime.UtcNow,
            LongUrl = longUrl,
            Code = code,
            ShortUrl = shortUrl
        };
    }
}


public static class MigrationExtensions 
{
    public async static void ApplyMigrations(this WebApplication app) 
    {
        using var scope = app.Services.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.Database.Migrate();
        

    }
}