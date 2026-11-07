using Blueshift.Agents;
using Blueshift.Agents.Abstract;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Concrete;
using AIESummit.Agents;
using AIESummit.Api;
using AIESummit.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
var projectId = config["Firebase:ProjectId"] ?? throw new InvalidOperationException("Firebase project ID is required.");
var emulator = builder.Environment.IsDevelopment() && config.GetValue<bool>("Firebase:Emulator");
var firebaseKey = config["Firebase:ApiKey"] ?? (emulator ? "demo-key" : throw new InvalidOperationException("Firebase web API key is required."));
var issuer = $"https://securetoken.google.com/{projectId}";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.IncludeErrorDetails = false;
    options.Audience = projectId;
    options.TokenValidationParameters.ValidIssuer = issuer;
    options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
    if (!emulator)
        options.Authority = issuer;
    else
    {
        options.TokenValidationParameters.RequireSignedTokens = false;
        options.TokenValidationParameters.SignatureValidator = (token, _) =>
        {
            var jwt = new JsonWebToken(token);
            if (jwt.Alg != "none" || jwt.IsSigned)
                throw new Microsoft.IdentityModel.Tokens.SecurityTokenInvalidSignatureException();
            return jwt;
        };
    }
});
builder.Services.AddAuthorizationBuilder().AddDefaultPolicy("Firebase", policy => policy.RequireAuthenticatedUser().RequireClaim("sub"));
builder.Services.AddDbContext<AIESummitDb>(options => options.UseNpgsql(config.GetConnectionString("AIESummitDb")));
builder.Services.AddScoped<EFBaseDbContext>(services => services.GetRequiredService<AIESummitDb>());
builder.Services.AddScoped(typeof(IBasePersistenceRepository<>), typeof(EFBasePersistenceRepository<>));
builder.Services.AddSingleton<IBroadcaster>(NullBroadcaster.Instance);
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddAgentPackServices(typeof(OcrAgent).Assembly);
builder.Services.AddBlueshiftAgents(options =>
{
    options.Enabled = config.GetValue<bool>("AzureQueue:Enabled");
    options.ConnectionString = config["AzureQueue:ConnectionString"];
    options.Environment = "dev";
    options.VenturePrefix = "aiesummit";
    options.RegisterPublishers = true;
    options.RegisterReceivers = false;
});
builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = UploadEndpoints.MaxBytes + 65536);

var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AIESummitDb>();
    await Bootstrap.InitializeAsync(db, config);
}
_ = RootBaseAgent.ActivateAllAgents(app.Services, [typeof(OcrAgent).Assembly]).ToArray();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/firebase-config", () => new { projectId, apiKey = firebaseKey, emulator, emulatorPort = config["Firebase:EmulatorPort"] ?? "19099" });
app.MapUploadEndpoints();
app.MapExtractionEndpoints();
await app.RunAsync();

