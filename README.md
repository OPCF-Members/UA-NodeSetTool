# UA-NodeSetTool

> **This repo is PROTOTYPE code and is not a published specification so the schema WILL change.**

A .NET command-line tool and library for converting OPC UA NodeSets between XML, JSON, and TAR.GZ archive formats.

## Packages

| Package | Description |
|---------|-------------|
| **Opc.Ua.NodeSetTool** | Command-line tool (distributed as a .NET global tool) for converting and comparing OPC UA NodeSet files. |
| **Opc.Ua.JsonNodeSet** | Class library that provides serialization and deserialization of OPC UA NodeSets in XML, JSON, and TAR.GZ formats. Can be referenced from your own projects. |

## Supported Formats

| Format | Extension | Description |
|--------|-----------|-------------|
| XML | `.xml` | Standard OPC UA NodeSet format ([UANodeSet.xsd](https://github.com/OPCFoundation/UA-Nodeset/blob/latest/Schema/UANodeSet.xsd)) |
| JSON | `.json` | Compact JSON representation using a [JSON schema](https://github.com/OPCF-Members/UA-ModelCompiler/blob/master/NodeSetTool/json-nodeset-schema.json) |
| TAR.GZ | `.tar.gz` | Compressed archive that splits large NodeSets across multiple JSON files |

### JSON Format

The JSON format is defined by a [JSON schema](https://json-schema.org/). The schema is available [here](https://github.com/OPCF-Members/UA-ModelCompiler/blob/master/NodeSetTool/json-nodeset-schema.json).

The JSON form is designed to be easier for exception based parsers by ordering the nodes: ReferenceTypes, DataTypes, VariableTypes, ObjectTypes, Variables, Methods, Objects and then Views. That said, perfect ordering is impossible since circular references between Nodes can exist and readers need to handle the possibility. No ordering is imposed on the list of individual types.

### Archive Format (TAR.GZ)

The archive format contains a single NodeSet split into multiple files. The default maximum is 1000 nodes (including children) per file. Readers process the files in order (each file has a sequence number in it) and can construct a single NodeSet before processing if that is easier.

TAR/GZIP are used since they are platform independent and defined by RFCs.

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later

## Building

```bash
dotnet build
```

Or use the build script to produce NuGet packages:

```powershell
.\build.ps1 -Configuration Release
```

Packages are output to `./build/nupkg/`.

## Installation

Install as a global .NET tool from a local build:

```bash
dotnet tool install --global --add-source ./build/nupkg Opc.Ua.NodeSetTool
```

Or from NuGet (when published):

```bash
dotnet tool install --global Opc.Ua.NodeSetTool
```

## Usage

### Convert

Converts a NodeSet from one format to another.

```
ua-nodeset-tool convert [options]
```

| Option | Description |
|--------|-------------|
| `--in` | Input file path. Format is auto-detected from extension or content. |
| `--out` | Output file path. Defaults to the input filename with the new extension. |
| `--type` | Output format: `json`, `xml`, or `tar.gz`. |
| `--max` | Maximum nodes per file in TAR.GZ archives (default: 1000, minimum: 100). |

**Examples:**

```bash
# XML to JSON
ua-nodeset-tool convert --in MyModel.NodeSet2.xml --type json

# JSON to XML
ua-nodeset-tool convert --in MyModel.NodeSet2.json --type xml

# XML to TAR.GZ archive with 500 nodes per file
ua-nodeset-tool convert --in MyModel.NodeSet2.xml --type tar.gz --max 500
```

### Compare

Compares two NodeSets to verify they are semantically identical. The comparison is node-by-node and is not sensitive to the order Nodes appear in the files.

```
ua-nodeset-tool compare [options]
```

| Option | Description |
|--------|-------------|
| `--in` | The input file path. |
| `--target` | The comparison target file path. |

**Example:**

```bash
ua-nodeset-tool compare --in original.xml --target converted.xml
```

## Using the Library

Reference the `Opc.Ua.JsonNodeSet` package in your project to convert NodeSets programmatically:

```csharp
using Opc.Ua.JsonNodeSet;

// Load from any supported format (auto-detected)
var nodeSet = NodeSetSerializer.Load("MyModel.NodeSet2.xml");

// Save to JSON
NodeSetSerializer.SaveJson(nodeSet, "MyModel.NodeSet2.json");

// Save to XML
NodeSetSerializer.SaveXml(nodeSet, "MyModel.NodeSet2.xml");

// Save to TAR.GZ archive
NodeSetSerializer.SaveArchive(nodeSet, "MyModel.NodeSet2.tar.gz", maxNodesPerFile: 1000);
```

## Running Tests

```bash
dotnet test
```

## Examples

The `examples/` directory contains sample NodeSet files in all three formats:

- `DemoModel.NodeSet2.xml` / `.json` / `.tar.gz`
- `Opc.Ua.NodeSet2.Services.xml` / `.json` / `.tar.gz`

## License

MIT
