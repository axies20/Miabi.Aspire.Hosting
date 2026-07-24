using Miabi.Aspire.Hosting.Blazor.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents();

var app = builder.Build();

app.MapStaticAssets();
app.UseAntiforgery();
app.MapRazorComponents<App>();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
