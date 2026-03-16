using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Example;

internal static class StaticFileServer
{
    public static void Start(string resourcesPath)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            WebRootPath = resourcesPath,
        });

        builder.WebHost.UseKestrelCore();
        builder.WebHost.UseUrls("http://localhost:4444");
        builder.Services.AddRoutingCore();

        var app = builder.Build();

        var provider = new PhysicalFileProvider(resourcesPath);
        var options = new StaticFileOptions { FileProvider = provider };

        app.UseStaticFiles(options);

        app.MapGet("/", async ctx =>
        {
            ctx.Response.Redirect("/index.html");
            await ctx.Response.CompleteAsync();
        });

        app.RunAsync();
    }
}
