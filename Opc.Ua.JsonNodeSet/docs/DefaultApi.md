# Opc.Ua.JsonNodeSet.Api.DefaultApi

All URIs are relative to *https://webapi.opcfoundation.org/api/nodeset*

| Method | HTTP request | Description |
|--------|--------------|-------------|
| [**Get**](DefaultApi.md#get) | **GET** /get |  |

<a id="get"></a>
# **Get**
> UANodeSet Get ()



### Example
```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Opc.Ua.JsonNodeSet.Api;
using Opc.Ua.JsonNodeSet.Client;
using Opc.Ua.JsonNodeSet.Model;

namespace Example
{
    public class GetExample
    {
        public static void Main()
        {
            Configuration config = new Configuration();
            config.BasePath = "https://webapi.opcfoundation.org/api/nodeset";
            var apiInstance = new DefaultApi(config);

            try
            {
                UANodeSet result = apiInstance.Get();
                Debug.WriteLine(result);
            }
            catch (ApiException  e)
            {
                Debug.Print("Exception when calling DefaultApi.Get: " + e.Message);
                Debug.Print("Status Code: " + e.ErrorCode);
                Debug.Print(e.StackTrace);
            }
        }
    }
}
```

#### Using the GetWithHttpInfo variant
This returns an ApiResponse object which contains the response data, status code and headers.

```csharp
try
{
    ApiResponse<UANodeSet> response = apiInstance.GetWithHttpInfo();
    Debug.Write("Status Code: " + response.StatusCode);
    Debug.Write("Response Headers: " + response.Headers);
    Debug.Write("Response Body: " + response.Data);
}
catch (ApiException e)
{
    Debug.Print("Exception when calling DefaultApi.GetWithHttpInfo: " + e.Message);
    Debug.Print("Status Code: " + e.ErrorCode);
    Debug.Print(e.StackTrace);
}
```

### Parameters
This endpoint does not need any parameter.
### Return type

[**UANodeSet**](UANodeSet.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | Success |  -  |
| **404** | NodeSet not found |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

