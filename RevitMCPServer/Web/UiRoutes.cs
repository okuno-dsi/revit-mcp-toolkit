using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace RevitMcpServer.Web
{
    public static class UiRoutes
    {
        public static void MapUi(this WebApplication app, string wwwrootPath)
        {
            // Default files & static files
            app.UseDefaultFiles(new DefaultFilesOptions { DefaultFileNames = { "index.html", "dashboard.html", "commands.html" } });
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(wwwrootPath),
                ServeUnknownFileTypes = true
            });

            // Root -> serve HTML when available
            app.MapGet("/", async ctx =>
            {
                foreach (var f in new[] { "index.html", "dashboard.html", "commands.html" })
                {
                    var path = Path.Combine(wwwrootPath, f);
                    if (File.Exists(path)) { await ctx.Response.SendFileAsync(path); return; }
                }
                ctx.Response.Redirect("/openapi.json");
            });

            // Dynamic UI preview (optional)
            app.MapGet("/ui", async ctx =>
            {
                var p = Path.Combine(wwwrootPath, "index.html");
                if (File.Exists(p)) { await ctx.Response.SendFileAsync(p); return; }
                await ctx.Response.WriteAsync("<html><body><h1>No UI</h1></body></html>");
            });
        }
    }
}

