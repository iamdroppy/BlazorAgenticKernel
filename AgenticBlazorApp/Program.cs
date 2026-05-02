using AgenticBlazorApp.Components;
using AgenticBlazorApp.Plugins;
using AgenticBlazorApp.Services;
using BbcNewsPlugin;
using BrowserPlugin;
using Hangfire;
using Hangfire.Storage.SQLite;
using MailPlugin;
using SerpApiPlugin;
using Microsoft.SemanticKernel;
using CurrencyPlugin;
using CepPlugin;
using FileSystemPlugin;

var builder = WebApplication.CreateBuilder(args);
// add configuration from appsettings.json and appsettings.local.json (if it exists)
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);
// ---------------------------------------------------------------------
// Blazor (Server-Side Rendering with interactive server components)
// ---------------------------------------------------------------------
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ---------------------------------------------------------------------
// Hangfire  (SQLite storage)
// ---------------------------------------------------------------------
var sqliteConn = builder.Configuration["Hangfire:SQLiteConnectionString"]
                 ?? "hangfire.db";

builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSQLiteStorage(sqliteConn));

builder.Services.AddHangfireServer();

// ---------------------------------------------------------------------
// HttpClient for the plugins (so they respect IHttpClientFactory + timeouts)
// ---------------------------------------------------------------------
builder.Services.AddHttpClient<NewsPlugin>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("AgenticBlazorKernel/1.0");
});

builder.Services.AddHttpClient<BrowserReader>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(20);
    c.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (compatible; AgenticBlazorKernel/1.0)");
});

// ---------------------------------------------------------------------
// Mail — bind Mail:Accounts from config and register the singleton registry.
// Supports N accounts, each with its own IMAP + SMTP settings.
// ---------------------------------------------------------------------
var mailAccounts = builder.Configuration
    .GetSection("Mail:Accounts")
    .Get<List<MailAccountOptions>>() ?? new List<MailAccountOptions>();

builder.Services.AddSingleton(new MailAccountRegistry(mailAccounts));

// ---------------------------------------------------------------------
// App services
// ---------------------------------------------------------------------
// AlertService is a singleton pub/sub bus so every Blazor circuit
// and every Hangfire job sees the same notification stream.
builder.Services.AddSingleton<AlertService>();

// Scoped — a fresh Kernel + plugin instances per request / per Hangfire job.

builder.Services.AddScoped<SchedulerPlugin>();
builder.Services.AddScoped<MailPlugin.MailPlugin>();
builder.Services.AddScoped<AgenticService>();
builder.Services.AddScoped<ScheduledAgenticJob>();
builder.Services.AddScoped<DuckDuckGoPlugin>();
builder.Services.AddScoped<LiveCurrency>();
builder.Services.AddScoped<CepPlugin.CepPlugin>();
builder.Services.AddScoped<ProcPlugin>();
builder.Services.AddScoped<FsPLugin>();
// ChatService is scoped to the Blazor circuit so the ChatHistory
// persists across turns for as long as the connection stays alive.
builder.Services.AddScoped<ChatService>();

// Telegram agent — long-polls Telegram and runs every incoming message
// through the same Kernel (with auto tool calling) that powers the
// Blazor chat.  Stays idle if Telegram:BotToken is not configured.
builder.Services.AddHostedService<TelegramAgentService>();

// ---------------------------------------------------------------------
// Semantic Kernel
// ---------------------------------------------------------------------
builder.Services.AddScoped<Kernel>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var apiKey = cfg["OpenAI:ApiKey"];
    if (string.IsNullOrEmpty(apiKey))
    {
        throw new InvalidOperationException(
            "OpenAI API key is not configured. Please set OpenAI:ApiKey in appsettings or user secrets.");
    }
    var model = cfg["OpenAI:ChatModel"] ?? "gpt-4o-mini";

    var kernelBuilder = Kernel.CreateBuilder();
    kernelBuilder.AddOpenAIChatCompletion(model, apiKey);

    var kernel = kernelBuilder.Build();

    // Load the external plugin DLL (BbcNewsPlugin assembly).
    kernel.Plugins.AddFromObject(sp.GetRequiredService<NewsPlugin>(), nameof(NewsPlugin));

    // Load the external plugin DLL (BrowserPlugin assembly).
    //kernel.Plugins.AddFromObject(sp.GetRequiredService<BrowserReader>(), nameof(BrowserReader));

    // Load the external plugin DLL (MailPlugin assembly).
    kernel.Plugins.AddFromObject(sp.GetRequiredService<MailPlugin.MailPlugin>(), nameof(MailPlugin.MailPlugin));

    // In-process Scheduler plugin that calls Hangfire.
    kernel.Plugins.AddFromObject(sp.GetRequiredService<SchedulerPlugin>(), nameof(SchedulerPlugin));

    // Loads Serpi plugins
    kernel.Plugins.AddFromObject(sp.GetRequiredService<DuckDuckGoPlugin>(), nameof(DuckDuckGoPlugin));

    // Currency Plugins
    kernel.Plugins.AddFromObject(sp.GetRequiredService<LiveCurrency>(), nameof(LiveCurrency));
    
    // CEP Plugins
    kernel.Plugins.AddFromObject(sp.GetRequiredService<CepPlugin.CepPlugin>(), nameof(CepPlugin.CepPlugin));

    // FileSystem Plugins
    kernel.Plugins.AddFromObject(sp.GetRequiredService<FsPLugin>(), nameof(FsPLugin));
    kernel.Plugins.AddFromObject(sp.GetRequiredService<ProcPlugin>(), nameof(ProcPlugin));

    return kernel;
});

// ---------------------------------------------------------------------
// Pipeline
// ---------------------------------------------------------------------
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

// Hangfire admin dashboard (only enabled in dev for safety).
if (app.Environment.IsDevelopment())
{
    app.UseHangfireDashboard("/hangfire");
}

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();
