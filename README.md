libudpjson
==========

This library allows you to send and receive JSON-RPC commands over UDP.
It is based on the [JSON-RPC 2.0 spec](http://www.jsonrpc.org/specification).

## Development
This project is based on .NET Standard 1.4, meaning that it can be referenced from any .NET Core >= 1.0 and any .NET Framework >= 4.6.1 project.

VS 2015 does not have good support for managing .NET Standard 1.x projects. To build this project from source, you will need:

- Visual Studio 2017
- .NET Core 1.0 SDK (x64 and x86); this should come preinstalled with VS 2017