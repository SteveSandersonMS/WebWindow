# Important

**I'm not directly maintaining or developing WebWindow currently or for the forseeable future.** The primary reason is that it's mostly fulfilled its purpose, which is to inspire and kickstart serious efforts to make cross-platform hybrid desktop+web apps with .NET Core a reality. Read more at https://github.com/SteveSandersonMS/WebWindow/issues/86.

People who want to build real cross-platform hybrid desktop+web apps with .NET Core should consider the following alternatives:

 * [Photino](https://www.tryphotino.io/), which is based on this WebWindow project and is the successor to it. Photino is maintained by the team at CODE Magazine and the project's open source community. It supports Windows, Mac, and Linux, along with UIs built using either Blazor (for .NET Core) or any JavaScript-based framework.
 * Official support for [Blazor hybrid desktop apps coming in .NET 6](https://devblogs.microsoft.com/dotnet/announcing-net-6-preview-1/#blazor-desktop-apps).

# WebWindow

For information, see [this blog post](https://blog.stevensanderson.com/2019/11/18/2019-11-18-webwindow-a-cross-platform-webview-for-dotnet-core/).

# Usage instructions

Unless you want to change the `WebWindow` library itself, you do not need to build this repo yourself. If you just want to use it in an app, grab the [prebuilt NuGet package](https://www.nuget.org/packages/WebWindow) or follow [these 'hello world' example steps](https://blog.stevensanderson.com/2019/11/18/2019-11-18-webwindow-a-cross-platform-webview-for-dotnet-core/).

# Samples

For samples, open the `WebWindow.Samples.sln` solution

These projects reference the prebuilt NuGet package so can be built without building the native code in this repo.

# How to build this repo

If you want to build the `WebWindow` library itself, you will need:

 * Windows, Mac, or Linux
 * Node.js (because `WebWindow.Blazor.JS` includes TypeScript code, so the build process involves calling Node to perform a Webpack build)
 * If you're on Windows:
   * Use Visual Studio with C++ support enabled. You *must* build in x64 configuration (*not* AnyCPU, which is the default).
   * If things don't seem to be updating, try right-clicking one of the `testassets` projects and choose *Rebuild* to force it to rebuild the native assets.
 * If you're on macOS:
   * Install Xcode so that you have the whole `gcc` toolchain available on the command line.
   * From the repo root, run `dotnet build src/WebWindow/WebWindow.csproj`
   * Then you can `cd testassets/HelloWorldApp` and `dotnet run`
 * If you're on Linux (tested with Ubuntu 18.04):
   * Install dependencies: `sudo apt-get update && sudo apt-get install libgtk-3-dev libwebkit2gtk-4.0-dev`
   * From the repo root, run `dotnet build src/WebWindow/WebWindow.csproj`
   * Then you can `cd testassets/HelloWorldApp` and `dotnet run`
 * If you're on Windows Subsystem for Linux (WSL), then as well as the above, you will need a local X server ([example setup](https://virtualizationreview.com/articles/2017/02/08/graphical-programs-on-windows-subsystem-on-linux.aspx)).

