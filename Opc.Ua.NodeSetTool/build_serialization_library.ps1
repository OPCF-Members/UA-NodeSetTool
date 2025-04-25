$Root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$NodeSets = "$Root\..\..\nodesets\v105"
$Java = "java.exe"
$ProjectName = "opcua-nodeset-json"
$Output = "..\Opc.Ua.JsonNodeSet"

if (!(Test-Path ".\openapi-generator-cli.jar" -PathType Leaf)) {
    Invoke-WebRequest -OutFile openapi-generator-cli.jar https://repo1.maven.org/maven2/org/openapitools/openapi-generator-cli/7.9.0/openapi-generator-cli-7.9.0.jar
}

& $Java -jar ".\openapi-generator-cli.jar" generate -g csharp `
    -i ".\json-nodeset-schema.json" `
    -o $Output `
    -p packageName=Opc.Ua.JsonNodeSet,packageVersion=1.504.1,optionalProjectFile=Opc.Ua.JsonNodeSet,targetFramework=netstandard2.0

& cd $Output
& dotnet tool install dotnet-outdated-tool --global
& dotnet outdated --upgrade

$ModelPath = "src\Opc.Ua.JsonNodeSet\Model\"

$OldText = "protected IEnumerable<ValidationResult> BaseValidate"
$NewText = "protected override IEnumerable<ValidationResult> BaseValidate"

(Get-Content $ModelPath"UAObjectType.cs") -replace [regex]::Escape($OldText), $NewText | Set-Content $ModelPath"UAObjectType.cs"
(Get-Content $ModelPath"UAVariableType.cs") -replace [regex]::Escape($OldText), $NewText | Set-Content $ModelPath"UAVariableType.cs"
(Get-Content $ModelPath"UADataType.cs") -replace [regex]::Escape($OldText), $NewText | Set-Content $ModelPath"UADataType.cs"
(Get-Content $ModelPath"UAReferenceType.cs") -replace [regex]::Escape($OldText), $NewText | Set-Content $ModelPath"UAReferenceType.cs"
(Get-Content $ModelPath"UAObject.cs") -replace [regex]::Escape($OldText), $NewText | Set-Content $ModelPath"UAObject.cs"
(Get-Content $ModelPath"UAVariable.cs") -replace [regex]::Escape($OldText), $NewText | Set-Content $ModelPath"UAVariable.cs"
(Get-Content $ModelPath"UAMethod.cs") -replace [regex]::Escape($OldText), $NewText | Set-Content $ModelPath"UAMethod.cs"
(Get-Content $ModelPath"UAView.cs") -replace [regex]::Escape($OldText), $NewText | Set-Content $ModelPath"UAView.cs"

$NewText = "protected virtual IEnumerable<ValidationResult> BaseValidate"
(Get-Content $ModelPath"UANode.cs") -replace [regex]::Escape($OldText), $NewText | Set-Content $ModelPath"UANode.cs"

$OldText = 'NodeClass? nodeClass = "UAObjectType"'
$NewText = 'NodeClass? nodeClass = Model.NodeClass.UAObjectType'
(Get-Content $ModelPath"UAObjectType.cs") -replace [regex]::Escape($OldText), $NewText | Set-Content $ModelPath"UAObjectType.cs"

$OldText = 'NodeClass? nodeClass = "UAVariableType"'
$NewText = 'NodeClass? nodeClass = Model.NodeClass.UAVariableType'
(Get-Content $ModelPath"UAVariableType.cs") -replace [regex]::Escape($OldText), $NewText | Set-Content $ModelPath"UAVariableType.cs"

$OldText = 'NodeClass? nodeClass = "UADataType"'
$NewText = 'NodeClass? nodeClass = Model.NodeClass.UADataType'
(Get-Content $ModelPath"UADataType.cs") -replace [regex]::Escape($OldText), $NewText | Set-Content $ModelPath"UADataType.cs"

$OldText = 'NodeClass? nodeClass = "UAReferenceType"'
$NewText = 'NodeClass? nodeClass = Model.NodeClass.UAReferenceType'
(Get-Content $ModelPath"UAReferenceType.cs") -replace [regex]::Escape($OldText), $NewText | Set-Content $ModelPath"UAReferenceType.cs"

$OldText = 'NodeClass? nodeClass = "UANode"'
$NewText = 'NodeClass? nodeClass = Model.NodeClass.UAObject'
(Get-Content $ModelPath"UANode.cs") -replace [regex]::Escape($OldText), $NewText | Set-Content $ModelPath"UANode.cs"
$OldText = 'NodeClass? nodeClass = "UAObject"'
(Get-Content $ModelPath"UAObject.cs") -replace [regex]::Escape($OldText), $NewText | Set-Content $ModelPath"UAObject.cs"

$OldText = 'NodeClass? nodeClass = "UAVariable"'
$NewText = 'NodeClass? nodeClass = Model.NodeClass.UAVariable'
(Get-Content $ModelPath"UAVariable.cs") -replace [regex]::Escape($OldText), $NewText | Set-Content $ModelPath"UAVariable.cs"

$OldText = 'NodeClass? nodeClass = "UAMethod"'
$NewText = 'NodeClass? nodeClass = Model.NodeClass.UAMethod'
(Get-Content $ModelPath"UAMethod.cs") -replace [regex]::Escape($OldText), $NewText | Set-Content $ModelPath"UAMethod.cs"

$OldText = 'NodeClass? nodeClass = "UAView"'
$NewText = 'NodeClass? nodeClass = Model.NodeClass.UAView'
(Get-Content $ModelPath"UAView.cs") -replace [regex]::Escape($OldText), $NewText | Set-Content $ModelPath"UAView.cs"

$ModelPath = "src\Opc.Ua.JsonNodeSet\Client\"
$OldText = "return client.Deserialize<T>(policyResult.Result);"
$NewText = "return client.Deserialize<T>(policyResult.Result, CancellationToken.None).Result;"

(Get-Content $ModelPath"ApiClient.cs") -replace [regex]::Escape($OldText), $NewText | Set-Content $ModelPath"ApiClient.cs"

$OldText = "MaxTimeout = configuration.Timeout,"
$NewText = "Timeout = new TimeSpan(configuration.Timeout * TimeSpan.TicksPerMillisecond),"

(Get-Content $ModelPath"ApiClient.cs") -replace [regex]::Escape($OldText), $NewText | Set-Content $ModelPath"ApiClient.cs"

$ModelPath = "src\Opc.Ua.JsonNodeSet\"
$OldText = '<Reference Include="System.Web" />'
$NewText = ""

(Get-Content $ModelPath"Opc.Ua.JsonNodeSet.csproj") -replace [regex]::Escape($OldText), $NewText | Set-Content $ModelPath"Opc.Ua.JsonNodeSet.csproj"

& cd $Root 