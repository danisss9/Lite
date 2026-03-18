using Example;
using Lite;

var resourcesPath = Path.GetFullPath("resources");
StaticFileServer.Start(resourcesPath);

var window = new BrowserWindow("http://localhost:4444");
window.Run();

/* var window = new BrowserWindow("https://html5test.co/");
window.Run(); */

/* var window = new BrowserWindow("https://css3test.com/");
window.Run(); */

/* var window = new BrowserWindow("http://acid3.acidtests.org/");
window.Run(); */

/* var window = new BrowserWindow("https://browserbench.org/");
window.Run(); */
