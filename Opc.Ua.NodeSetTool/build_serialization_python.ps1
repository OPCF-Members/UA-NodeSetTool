$Root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$NodeSets = "$Root\..\..\nodesets\v105"
$Java = "java.exe"
$Output = "..\opcua-nodeset-python"

if (!(Test-Path ".\openapi-generator-cli.jar" -PathType Leaf)) {
    Invoke-WebRequest -OutFile openapi-generator-cli.jar https://repo1.maven.org/maven2/org/openapitools/openapi-generator-cli/7.9.0/openapi-generator-cli-7.9.0.jar
}
& $Java -jar ".\openapi-generator-cli.jar" generate -g python `
    -i ".\json-nodeset-schema.json" `
    -o $Output `
    -p packageName=opcua_nodeset,projectName=opcua-nodeset,packageVersion=1.504.1

cd $Output 

& pip uninstall -y opcua-nodeset
& python setup.py bdist_wheel
& pip install dist/opcua_nodeset-1.504.1-py3-none-any.whl

cd $Root  