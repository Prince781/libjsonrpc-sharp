﻿libjsonrpc-sharp
================

Extremely light-weight, general-purpose, intuitive implementation of JSON-RPC.

This library allows you to send and receive JSON-RPC. Using [Stream](https://docs.microsoft.com/en-us/dotnet/api/system.io.stream?view=netframework-4.7.2)s makes the implementation agnostic to the type of channel used to send data. 
It is based on the [JSON-RPC 2.0 spec](http://www.jsonrpc.org/specification).

## Authors

The original version of this software was developed internally at [BrightLogic](https://www.brightlogic.com) by Princeton Ferro, who is now the current maintainer. It has been released under the GNU Lesser General Public License version 2.1. Since then it has been entirely rewritten with an API similar to [jsonrpc-glib](https://gitlab.gnome.org/GNOME/jsonrpc-glib).

## Development/Building

### Linux

- Make sure you have `dotnet` and `dotnet-cli` installed. Often the two may be bundled together.
- Make sure you have `mono` installed.

```bash
$ git clone https://github.com/Prince781/libjsonrpc-sharp.git
$ cd libjsonrpc-sharp
$ export FrameworkPathOverride=/usr/lib/mono/$API_VERSION_DIR/
```

Where `$API_VERSION_DIR` could be `4.7-api`. Check what's contained in `/usr/lib/mono/`. [See this issue for more information.](https://github.com/dotnet/cli/issues/5977)

Finally, do:

```bash
$ dotnet build
```

### Windows

This project is based on the .NET Standard 2.0. This means that it can be used in .NET Core >= 2.0 and .NET Framework >= 4.6.1 projects.

Visual Studio 2015 does not have good support for .NET Standard 2.0/.NET Core 2.0 applications. Use Visual Studio 2017.

You must install these:
- [.NET Core 2.0 SDK (x86 and x64)](https://github.com/dotnet/cli/tree/release/2.0.0#installers-and-binaries)
- Visual Studio 2017, version 15.3 or later to support .NET Core 2.0/.NET Standard 2.0 projects. As of writing, version 15.3 is a
preview build 2. You can get it here: https://blogs.msdn.microsoft.com/dotnet/2017/06/12/net-core-2-and-visual-studio-2017-preview-2/. 

Make sure to launch VS2017 preview (2), which is a separate application from the installed VS2017.

Eventually once 15.3 comes out of preview status, you should not use this link, and check ahead of time that your VS2017 can create .NET Core 2.0 projects.

## Dependencies
- Newtonsoft.Json 10.0.3

## Documentation
Run `doxygen Doxyfile` and look in `html/` for documentation.
