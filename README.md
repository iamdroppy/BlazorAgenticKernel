# Agentic Blazor Kernel

A minimal **Blazor Server (SSR)** app that wires **Microsoft Semantic Kernel**
to **Hangfire** so an LLM can schedule its own future tool calls.

```
"Alert me in 5 minutes about the BBC News"
        │
        ▼
AgenticService  ─────►  Semantic Kernel (OpenAI auto tool calling)
                                   │
                                   ▼
                        Scheduler.ScheduleAlert(intent="read the BBC news",
                                                delayMinutes=5)
                                   │
                                   ▼
                        Hangfire (SQLite) enqueues ScheduledAgenticJob
                                   │
                                   ▼  (5 min later)
                        ScheduledAgenticJob.RunAsync
                                   │
                                   ▼
                        Kernel → News.ReadBbcNews()  (from BbcNewsPlugin.dll)
                                        or
                                 Browser.ReadUrl(url) (from BrowserPlugin.dll)
                                   │
                                   ▼
                        AlertService.Publish(...)
                                   │
                                   ▼
                        Blazor circuit → JS toast + browser notification
                                         + chat stream message
```

## Projects

| Project            | Type           | Role                                                                                                                 |
|--------------------|----------------|----------------------------------------------------------------------------------------------------------------------|
| `BbcNewsPlugin`    | class library  | Plugin DLL. Ships `News.ReadBbcNews` as a `[KernelFunction]`.                                                        |
| `BrowserPlugin`    | class library  | Plugin DLL. Ships `Browser.ReadUrl` — fetches any URL and returns its main text (HTML stripped via HtmlAgilityPack). |
| `AgenticBlazorApp` | ASP.NET Core   | Blazor SSR host, Semantic Kernel setup, Hangfire server + dashboard, chat UI.                                        |

## Prerequisites

- .NET 9 SDK
- An OpenAI API key

## Setup

```bash
cd AgenticBlazorKernel

# configure your OpenAI key (never commit it)
cd AgenticBlazorApp
dotnet user-secrets set "OpenAI:ApiKey" "sk-..."
# optional: override model
dotnet user-secrets set "OpenAI:ChatModel" "gpt-4o-mini"

cd ..
dotnet restore
dotnet build
```

## Run

```bash
dotnet run --project AgenticBlazorApp
```

Open <http://localhost:5222> (or the HTTPS port printed in the console).

The Hangfire dashboard is mounted at `/hangfire` in Development.
The chat UI lives at `/chat`.

## Try it

- `Read the BBC news now` — the kernel calls `News.ReadBbcNews` directly and you
  see the headlines inline.
- `Summarise https://example.com` — the kernel calls `Browser.ReadUrl(url)`,
  `BrowserPlugin` fetches & strips the page, and the model summarises the result.
- `Alert me in 1 minute about the BBC news` — the kernel calls
  `Scheduler.ScheduleAlert("read the BBC news", 1)`. After 1 min Hangfire wakes
  `ScheduledAgenticJob`, the kernel calls `News.ReadBbcNews`, and the UI fires
  a browser notification + toast + a new assistant message in the chat.
- `Alert me in 2 minutes about https://example.com` — same flow but the scheduled
  job uses `Browser.ReadUrl` to fetch the page before alerting.
- `Ping me in 30 seconds` — with no content intent the alert still fires, but
  the agent will just say "alert fired".

## Adding more plugins

Drop another class library that references `Microsoft.SemanticKernel.Abstractions`,
add `[KernelFunction]` methods, reference it from `AgenticBlazorApp`, and register
it in `Program.cs`:

```csharp
kernel.Plugins.AddFromObject(sp.GetRequiredService<MyPlugin>(), "MyPlugin");
```

The `AgenticService` + `ChatService` system prompts are where you teach the LLM
when to use each tool group.

## Production notes

- The Hangfire dashboard is unauthenticated — it is only mapped in Development.
  Add `DashboardOptions.Authorization` before exposing it elsewhere.
- `AlertService` is in-memory only; for multi-instance deployments swap it for
  SignalR groups, a message bus, or server-sent events.
- `SchedulerPlugin` trusts the LLM to set `delayMinutes` sensibly — you may
  want to clamp it server-side.
- `BrowserPlugin` fetches arbitrary URLs. For hardened deployments, add
  allowlists / SSRF guards before shipping this tool.
- The OpenAI API key is hard-coded in `Program.cs` for the demo. Move it to
  user-secrets / environment variables before committing anywhere public.
