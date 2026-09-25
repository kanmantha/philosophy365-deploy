using AIPhilosophy.Core.Interfaces;
using AIPhilosophy.Core.Domain;
using AIPhilosophy.Infrastructure.Data;
using AIPhilosophy.Infrastructure.Providers;
using AIPhilosophy.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(AppContext.BaseDirectory, "logs", "api-.log"), rollingInterval: RollingInterval.Day)
    .CreateLogger();
builder.Host.UseSerilog();

var mockOutRoot = builder.Configuration["Mock:OutputPath"];
if (string.IsNullOrWhiteSpace(mockOutRoot))
    mockOutRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "videos");
Directory.CreateDirectory(mockOutRoot);
builder.Configuration["Mock:OutputPath"] = mockOutRoot;

builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "365 Days of Philosophy API",
        Version = "v1",
        Description = "Free SaaS - batch-generate daily AI philosophy videos and auto-post to social media."
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Enter 'Bearer {token}'",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer"
    });
});

builder.Services.AddAppDbContext(builder.Configuration);
builder.Services.AddHttpContextAccessor();

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
    {
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequiredLength = 8;
        o.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddFixedWindowLimiter("api", opt =>
    {
        opt.PermitLimit = 120;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 10;
    });
});

builder.Services.AddCors(o =>
{
    o.AddPolicy("web", p => p
        .SetIsOriginAllowed(_ => true)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddSingleton<ICronService, CronService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<DataSeeder>();
builder.Services.AddScoped<VideoGenerationService>();
builder.Services.AddScoped<SocialPosterService>();

builder.Services.AddSingleton<IVideoProvider, MockVideoProvider>();
builder.Services.AddSingleton<IVideoProvider, ReplicateVideoProvider>();
builder.Services.AddSingleton<ISocialProvider, MockSocialProvider>();
builder.Services.AddSingleton<ISocialProvider, YouTubeSocialProvider>();
builder.Services.AddSingleton<ISocialProvider, TikTokSocialProvider>();
builder.Services.AddSingleton<ISocialProvider, InstagramSocialProvider>();
builder.Services.AddSingleton<ISocialProvider, XSocialProvider>();
builder.Services.AddSingleton<ISocialProvider, LinkedInSocialProvider>();
builder.Services.AddSingleton<ISocialProvider, FacebookSocialProvider>();
builder.Services.AddSingleton<ProviderRegistry>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseRateLimiter();
app.UseStaticFiles();
app.UseCors("web");
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await db.EnsureSchemaAsync(!builder.Configuration.UsesPostgres(), log);
        var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
        await seeder.SeedAsync();
        log.LogInformation("Database ready and seeded.");
    }
    catch (Exception ex)
    {
        log.LogCritical(ex, "Database initialization failed.");
    }
}

app.Run();

public partial class Program { }