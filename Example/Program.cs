using Example;
using Lite;

var resourcesPath = Path.GetFullPath("resources");
StaticFileServer.Start(resourcesPath);

var window = new BrowserWindow("http://localhost:4444");
window.Run();
