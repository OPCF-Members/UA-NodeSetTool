# OPC UA JSON-LD / RDF Prototype

## Overview

The NodeSetEditor can export OPC UA information models as **JSON-LD** files (`.jsonld`).
These files are valid RDF and can be loaded into any SPARQL-capable triple store
(Apache Jena Fuseki, GraphDB, Stardog, etc.) for querying across one or many models.

Each exported file is a self-contained **named graph** identified by the model URI.
This lets you load multiple models into the same store and query them independently
or together.

---

## JSON-LD Document Structure

```
{
  "@context": { ... },          ← namespace prefixes + property mappings
  "@id": "<modelUri>",          ← named graph identifier
  "@type": "opcua:UANodeSet",   ← graph-level metadata type
  "modelUri": "...",
  "version": "...",
  "requiredModels": [ ... ],
  "@graph": [                   ← all nodes live here
    { normative nodes with isNormative: true },
    { external stubs  with isExternal:  true }
  ]
}
```

Because `@id` and `@graph` appear together at the root, the entire `@graph` array
becomes a **named graph** in RDF terms. Graph-level metadata (modelUri, version,
requiredModels) are triples about the graph itself.

---

## OPC UA–Specific RDF Vocabulary

All predicates live under the `http://opcfoundation.org/UA/` namespace (prefix `opcua`).

### Node Types (rdf:type)

| JSON-LD `@type`          | OPC UA Node Class  |
|--------------------------|--------------------|
| `opcua:UADataType`       | DataType           |
| `opcua:UAObjectType`     | ObjectType         |
| `opcua:UAVariableType`   | VariableType       |
| `opcua:UAReferenceType`  | ReferenceType      |
| `opcua:UAObject`         | Object             |
| `opcua:UAVariable`       | Variable           |
| `opcua:UAMethod`         | Method             |
| `opcua:UAView`           | View               |

### Node Properties

| Predicate                  | RDF Property               | XSD Type    | Description                          |
|----------------------------|----------------------------|-------------|--------------------------------------|
| `browseName`               | `opcua:BrowseName`         | string      | Qualified browse name                |
| `symbolicName`             | `opcua:SymbolicName`       | string      | Code-generation name                 |
| `description`              | `rdfs:comment`             | string      | Localized description                |
| `releaseStatus`            | `opcua:ReleaseStatus`      | string      | `Released`, `Draft`, or `Deprecated` |
| `dataType`                 | `opcua:HasDataType`        | @id         | DataType node reference              |
| `valueRank`                | `opcua:ValueRank`          | xsd:integer | Scalar / array indicator             |
| `arrayDimensions`          | `opcua:ArrayDimensions`    | string      | Comma-separated dimension lengths    |
| `typeDefinition`           | `opcua:HasTypeDefinition`  | @id         | TypeDefinition node reference        |
| `modellingRule`            | `opcua:HasModellingRule`   | @id         | Mandatory / Optional / Placeholder   |
| `parent`                   | `opcua:HasParent`          | @id         | Parent node reference                |
| `accessRestrictions`       | `opcua:AccessRestrictions` | xsd:integer | Bitmask                              |

### Reference Properties (Hierarchical)

| Forward Predicate            | Inverse Predicate              | Reference Type ID |
|------------------------------|--------------------------------|-------------------|
| `opcua:HasSubtype`           | `opcua:SubtypeOf`              | i=45              |
| `opcua:HasProperty`          | `opcua:PropertyOf`             | i=46              |
| `opcua:HasComponent`         | `opcua:ComponentOf`            | i=47              |
| `opcua:Organizes`            | `opcua:OrganizedBy`            | i=35              |
| `opcua:HasEncoding`          | `opcua:EncodingOf`             | i=38              |
| `opcua:HasDescription`       | *(not mapped)*                 | i=39              |
| `opcua:HasOrderedComponent`  | `opcua:OrderedComponentOf`     | i=49 / i=14156    |

Reference types not in this table are serialized as generic `opcua:References` entries
with `opcua:ReferenceType` and `opcua:Target` sub-properties.

### DataType Definition Properties

| Predicate              | RDF Property            | Description                     |
|------------------------|-------------------------|---------------------------------|
| `definition`           | `opcua:HasDefinition`   | Structured/enum field container |
| `fields`               | `opcua:HasField`        | Array of field objects          |
| `fieldName`            | `opcua:FieldName`       | Field name string               |
| `fieldValue`           | `opcua:FieldValue`      | Enumeration integer value       |
| `fieldDataType`        | `opcua:FieldDataType`   | Field data type reference       |
| `isUnion`              | `opcua:IsUnion`         | True if union type              |
| `isOptional`           | `opcua:IsOptional`      | True if optional field          |
| `allowSubTypes`        | `opcua:AllowSubTypes`   | True if subtypes allowed        |

### Classification Properties

| Predicate       | RDF Property          | Description                                     |
|-----------------|-----------------------|-------------------------------------------------|
| `isNormative`   | `opcua:IsNormative`   | `true` — node is fully defined in this model     |
| `isExternal`    | `opcua:IsExternal`    | `true` — stub for a node defined in another model |

### Node ID Format (CURIEs)

Node IDs use compact URIs (CURIEs) based on `@context` prefixes:

- **Core namespace:** `opcua:i=22` → `http://opcfoundation.org/UA/i=22` (Structure)
- **Model namespace:** `dm:i=68` → `urn:opcfoundation.org:2024-01:DemoModel/i=68`

The prefix for each model namespace is auto-derived from the URI's last path segment.

---

## Import Behaviour

When importing a `.jsonld` file via `LoadJsonLd` + `LoadInto(addressSpace)`:

1. Nodes marked `isExternal: true` (or legacy `isExternalReference: true`) are **not**
   added to the address space.
2. Instead, their node IDs are collected and **validated** — every external node must
   already exist in the target AddressSpace. If any are missing, an
   `InvalidOperationException` is thrown listing the missing IDs.
3. Nodes marked `isNormative: true` (or without either flag) are imported normally.

This ensures referential integrity: you must load dependency models (e.g. the core
namespace) before importing a model that references them.

---

## Setting Up Apache Jena Fuseki

### Prerequisites

- Java 11+ installed
- Download [Apache Jena Fuseki](https://jena.apache.org/download/) (e.g. `apache-jena-fuseki-5.x.x.zip`)

### Quick Start

```bash
# Unzip and start with an in-memory dataset
cd apache-jena-fuseki-5.x.x
./fuseki-server --mem /ua
```

Open `http://localhost:3030` in a browser.

### Loading Data

1. Go to **Manage** → dataset `/ua` → **Upload data**
2. Upload one or more `.jsonld` files exported from the NodeSetEditor
3. Each file becomes a named graph identified by its model URI

Alternatively, use the command line:

```bash
# Upload a single file
curl -X POST 'http://localhost:3030/ua/data?graph=urn:example:MyModel' \
     -H 'Content-Type: application/ld+json' \
     --data-binary @MyModel.jsonld

# Or use Jena's CLI tools directly (no server needed)
sparql --data MyModel.jsonld --query query.rq
```

### Querying via the Web UI

Go to **Query** on the Fuseki web interface and paste any SPARQL query. Results are
displayed as a table. Named graph queries require `GRAPH ?g { ... }` syntax.

---

## Sample SPARQL Queries

All queries below use `GRAPH ?g` to work across any model loaded into the store.
Replace `?g` with a specific URI to target one model (e.g.
`GRAPH <urn:opcfoundation.org:2024-01:DemoModel>`).

### 1. List All ObjectTypes

```sparql
PREFIX opcua: <http://opcfoundation.org/UA/>

SELECT ?graph ?node ?name
WHERE {
  GRAPH ?graph {
    ?node a opcua:UAObjectType ;
          opcua:BrowseName ?name .
  }
}
```

### 2. List All ObjectTypes (Normative Only, Excluding External Stubs)

```sparql
PREFIX opcua: <http://opcfoundation.org/UA/>

SELECT ?graph ?node ?name
WHERE {
  GRAPH ?graph {
    ?node a opcua:UAObjectType ;
          opcua:BrowseName ?name ;
          opcua:IsNormative true .
  }
}
```

### 3. Type Hierarchy — Find All Subtypes of a Given Type

```sparql
PREFIX opcua: <http://opcfoundation.org/UA/>

SELECT ?graph ?subtype ?name
WHERE {
  GRAPH ?graph {
    ?subtype opcua:SubtypeOf+ opcua:i=22 ;
             opcua:BrowseName ?name .
  }
}
```

This uses SPARQL property paths (`+` = one or more hops) to walk the full
`SubtypeOf` chain from `Structure` (i=22) downward.

### 4. DataType Fields — Show Structure Definitions

```sparql
PREFIX opcua: <http://opcfoundation.org/UA/>

SELECT ?graph ?datatype ?dtName ?fieldName ?fieldType
WHERE {
  GRAPH ?graph {
    ?datatype a opcua:UADataType ;
              opcua:BrowseName ?dtName ;
              opcua:IsNormative true ;
              opcua:HasDefinition ?def .
    ?def opcua:HasField ?field .
    ?field opcua:FieldName ?fieldName .
    OPTIONAL { ?field opcua:FieldDataType ?fieldType . }
  }
}
ORDER BY ?dtName ?fieldName
```

### 5. Find All Variables of a Given DataType

```sparql
PREFIX opcua: <http://opcfoundation.org/UA/>

SELECT ?graph ?var ?name ?dt
WHERE {
  GRAPH ?graph {
    ?var a opcua:UAVariable ;
         opcua:BrowseName ?name ;
         opcua:HasDataType ?dt .
    FILTER(?dt = opcua:i=12)
  }
}
```

Replace `opcua:i=12` with any DataType node ID (i=12 = String).

### 6. Component Tree — ObjectType with Its Components and Properties

```sparql
PREFIX opcua: <http://opcfoundation.org/UA/>

SELECT ?graph ?type ?typeName ?rel ?child ?childName
WHERE {
  GRAPH ?graph {
    ?type a opcua:UAObjectType ;
          opcua:BrowseName ?typeName ;
          opcua:IsNormative true .
    {
      ?type opcua:HasComponent ?child .
      BIND("HasComponent" AS ?rel)
    } UNION {
      ?type opcua:HasProperty ?child .
      BIND("HasProperty" AS ?rel)
    }
    ?child opcua:BrowseName ?childName .
  }
}
ORDER BY ?typeName ?rel ?childName
```

### 7. Cross-Model Dependencies — Which Models Does This Model Require?

```sparql
PREFIX opcua: <http://opcfoundation.org/UA/>

SELECT ?model ?reqModel
WHERE {
  ?model a opcua:UANodeSet ;
         opcua:RequiredModels ?req .
  ?req opcua:ModelUri ?reqModel .
}
```

Note: model metadata lives in the **default graph** (graph-level triples about
the named graph), so no `GRAPH` wrapper is needed here.

### 8. External Dependencies — List All External Stubs

```sparql
PREFIX opcua: <http://opcfoundation.org/UA/>

SELECT ?graph ?node ?type ?name
WHERE {
  GRAPH ?graph {
    ?node opcua:IsExternal true ;
          a ?type ;
          opcua:BrowseName ?name .
  }
}
ORDER BY ?graph ?type ?name
```

### 9. Find Nodes with Access Restrictions

```sparql
PREFIX opcua: <http://opcfoundation.org/UA/>

SELECT ?graph ?node ?name ?restrictions
WHERE {
  GRAPH ?graph {
    ?node opcua:AccessRestrictions ?restrictions ;
          opcua:BrowseName ?name .
    FILTER(?restrictions > 0)
  }
}
```

### 10. Union and Structure Types

```sparql
PREFIX opcua: <http://opcfoundation.org/UA/>

SELECT ?graph ?datatype ?name ?isUnion
WHERE {
  GRAPH ?graph {
    ?datatype a opcua:UADataType ;
              opcua:BrowseName ?name ;
              opcua:IsNormative true ;
              opcua:HasDefinition ?def .
    OPTIONAL { ?def opcua:IsUnion ?isUnion . }
  }
}
ORDER BY ?name
```

---

## Loading Multiple Models

When multiple `.jsonld` files are loaded into Fuseki, each occupies its own named
graph. You can query across all of them simultaneously:

```sparql
PREFIX opcua: <http://opcfoundation.org/UA/>

# Count nodes per model
SELECT ?graph (COUNT(?node) AS ?nodeCount)
WHERE {
  GRAPH ?graph {
    ?node opcua:IsNormative true .
  }
}
GROUP BY ?graph
```

Or target a specific model:

```sparql
PREFIX opcua: <http://opcfoundation.org/UA/>

SELECT ?node ?name
FROM NAMED <urn:opcfoundation.org:2024-01:DemoModel>
WHERE {
  GRAPH <urn:opcfoundation.org:2024-01:DemoModel> {
    ?node a opcua:UAObjectType ;
          opcua:BrowseName ?name .
  }
}
```

---

## Files

| File | Description |
|------|-------------|
| `Opc.Ua.JsonNodeSet/NodeSetSerializer.cs` | `BuildJsonLd()`, `LoadJsonLd()`, `SaveJsonLd()`, `NodeToJsonLd()` |
| `Opc.Ua.JsonNodeSet/AddressSpace.cs` | `GetNodeSetWithStubs()` — generates external stub list |
| `examples/DemoModel.NodeSet2.jsonld` | Hand-crafted reference example |
| `Opc.Ua.NodeSetTool.Tests/AddressSpaceLibTests.cs` | Round-trip and validation tests |
