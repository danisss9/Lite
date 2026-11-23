using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lite.Tests;

internal static class TestWebServer
{
    internal static WebApplicationBuilder CreateBuilder(string url = "http://127.0.0.1:0")
    {
#if NET8_0_OR_GREATER
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore();
        builder.Services.AddRoutingCore();
#else
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
#endif
        builder.WebHost.UseUrls(url);
        return builder;
    }
}
