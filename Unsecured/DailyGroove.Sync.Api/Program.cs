using DailyGroove.Sync.Api;
using DailyGroove.Sync.Api.Data;
using Microsoft.EntityFrameworkCore;

// ⚠️ DELIBERATELY INSECURE BASELINE — never deploy with real data. See README.md.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<SyncDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// INSECURE: any origin, method and header.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

// INSECURE: the full exception, stack trace included, goes back to the caller.
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "text/plain";
    await context.Response.WriteAsync(error?.ToString() ?? "Unknown error");
}));

app.UseCors();

// Baseline convenience: create/upgrade the schema on startup.
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<SyncDbContext>().Database.Migrate();

app.MapSyncEndpoints();

app.Run();

// Lets WebApplicationFactory<Program> see the entry point from the test project.
public partial class Program;
