using Lite.Example;
using Lite;

// Use the real LibVLC media backend for <audio>/<video> (falls back to the simulated timeline
// if the native libraries aren't available).
Lite.Media.Vlc.VlcMedia.Register();

var resourcesPath = Path.GetFullPath("resources");
StaticFileServer.Start(resourcesPath);

var window = new BrowserWindow("http://localhost:4444");
window.Run();

/* var window = new BrowserWindow("http://acid3.acidtests.org/");
window.Run(); */

/* var window = new BrowserWindow("https://html5test.co/");
window.Run(); */

/* var window = new BrowserWindow("https://css3test.com/");
window.Run(); */

/* var window = new BrowserWindow("https://browserbench.org/");
window.Run(); */

/* var window = new BrowserWindow("https://google.com/");
window.Run(); */
