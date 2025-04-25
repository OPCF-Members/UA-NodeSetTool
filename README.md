## NodeSetTool
### Overview
This tool converts between different NodeSet formats.  

There are 3 formats defined:

1. XML (defined by a normative schema which can be found [here](https://github.com/OPCFoundation/UA-Nodeset/blob/latest/Schema/UANodeSet.xsd));
2. JSON (defined by a proposed OpenAPI schema which can be found [here](https://github.com/OPCF-Members/UA-ModelCompiler/blob/master/NodeSetTool/json-nodeset-schema.json));
3. GZIP (a compressed TAR archive containing the a multi-file version of the JSON format).

The JSON format uses the [OpenAPI specification](https://spec.openapis.org/oas/v3.0.3.html) because of the numerous tools available that can process JSON schemas in that form. This means there is an unnecessary HTTP GET service defined. The UANodeSet schema is defined by the body of the GET service response. The schema also uses OpenAPI 3.0.3 since there is less tool support for OpenAPI 3.1 (e.g. it is planned for .NET 10.0).

If useful, a version of the the schema that conforms to [JSON schema](https://json-schema.org/) could be published too.

The JSON form is designed to be easier for exception based parses by ordering the nodes: ReferenceTypes, DataTypes, VariableTypes, ObjectTypes, Variables, Methods, Objects and then Views. That said, perfect ordering is impossible since circular references between Nodes can exist and readers need to handle the possibility. In addition, no ordering is imposed on the list of individual types. 

The archive format contains a single NodeSet split int multiple files. The current tool hardcodes a max of 1000 nodes (including children) per file. Readers process the files in order (each file has a sequence number in it) and can easily construct a single NodeSet before processing if that is easier for them.

TAR/GZIP are used since they are platform independent and defined by RFCs.

The tool has a compare command which is needed for testing. It does a node-by-node check to make sure two NodeSets are identical. It is not sensitive to the order the Nodes appear in the file.

### Command Line
#### Commands
An tool to convert NodeSets between XML, JSON and GZIP forms.

Usage: NodeSetTool [command] [options]

Options:

|||
|--|--|
|-?\|-h\|--help|Show help information.|

Commands:

|||
|--|--|
|compare|Compares two NodeSets.|
|convert|Converts a NodeSet from one format to another.|

#### Convert
Converts a NodeSet from one format to another.

Usage: NodeSetTool convert [options]

Options:

|||
|--|--|
|-?\|-h\|--help|Show help information.|
|--in|The input file path.|
|--out|The output file path.|
|--type|The output file type (json \| xml \| tar.gz).|

#### Compare
Compares two NodeSets.

Usage: NodeSetTool compare [options]

Options:

|||
|--|--|
|-?\|-h\|--help|Show help information.|
|--in|The input file path.|
|--target|The comparison target file path.|
