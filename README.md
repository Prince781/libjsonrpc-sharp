libudpjson
==========

This library allows you to send and receive JSON-RPC commands over UDP.
It is based on the [JSON-RPC 2.0 spec](http://www.jsonrpc.org/specification).

## Development
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
