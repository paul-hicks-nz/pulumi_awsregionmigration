using Pulumi;
using Aws = Pulumi.Aws;
using Iam = Pulumi.Aws.Iam;
using DynamoDb = Pulumi.Aws.DynamoDB;
using Api = Pulumi.Aws.ApiGateway;
using System.Collections.Generic;

return await Pulumi.Deployment.RunAsync(() =>
{
    var provider = new Aws.Provider("ap-southeast-2", new Aws.ProviderArgs
    {
        Profile = "admin-cms",
        Region = "ap-southeast-2",
        DefaultTags = new Aws.Inputs.ProviderDefaultTagsArgs {
            Tags = new Dictionary<string, string>
            {
                ["App"] = "getset"
            }
        }
    });

    // Create a DynamoDB table
    var table = new DynamoDb.Table("Values", new DynamoDb.TableArgs
    {
        Attributes = {
            new DynamoDb.Inputs.TableAttributeArgs
            {
                Name = "id",
                Type = "S"
            }
        },
        HashKey = "id",
        BillingMode = "PAY_PER_REQUEST"
    }, new CustomResourceOptions
    {
        Provider = provider
    });

    // Create IAM role for API Gateway
    var role = new Iam.Role("ApiGatewayMayAccessDynamoDB", new Iam.RoleArgs
    {
        AssumeRolePolicy = @"{
            ""Version"": ""2012-10-17"",
            ""Statement"": [{
                ""Effect"": ""Allow"",
                ""Principal"": {
                    ""Service"": ""apigateway.amazonaws.com""
                },
                ""Action"": ""sts:AssumeRole""
            }]
        }"
    }, new CustomResourceOptions
    {
        Provider = provider
    });

    // Attach policy to the role
    var policy = new Iam.RolePolicy("apiGatewayPolicy", new Iam.RolePolicyArgs
    {
        Role = role.Id,
        Policy = Output.Format(@$"{{
            ""Version"": ""2012-10-17"",
            ""Statement"": [{{
                ""Effect"": ""Allow"",
                ""Action"": [
                    ""dynamodb:PutItem"",
                    ""dynamodb:GetItem""
                ],
                ""Resource"": ""{table.Arn}""
            }}]
        }}")
    }, new CustomResourceOptions
    {
        Provider = provider
    });

    // Create API Gateway Rest API
    var api = new Api.RestApi("demo", new Api.RestApiArgs
    {
        Name = "Demo",
        Description = "API for demonstrations"
    }, new CustomResourceOptions
    {
        Provider = provider
    });

    // Create a resource in API Gateway
    var resource = new Api.Resource("getset", new Api.ResourceArgs
    {
        RestApi = api.Id,
        ParentId = api.RootResourceId,
        PathPart = "getset"
    }, new CustomResourceOptions
    {
        Provider = provider
    });

    // Create a POST method
    var setMethod = new Api.Method("set", new Api.MethodArgs
    {
        Authorization = "NONE",
        HttpMethod = "POST",
        ResourceId = resource.Id,
        RestApi = api.Id
    }, new CustomResourceOptions
    {
        Provider = provider
    });

    // Create a GET method
    var getMethod = new Api.Method("get", new Api.MethodArgs
    {
        Authorization = "NONE",
        HttpMethod = "GET",
        ResourceId = resource.Id,
        RestApi = api.Id
    }, new CustomResourceOptions
    {
        Provider = provider
    });

    // Create integration for the POST method
    var setIntegration = new Api.Integration("set", new Api.IntegrationArgs
    {
        ResourceId = resource.Id,
        RestApi = api.Id,
        Credentials = role.Arn,
        HttpMethod = setMethod.HttpMethod,
        Type = "AWS",
        IntegrationHttpMethod = "POST",
        Uri = Output.Format($"arn:aws:apigateway:{Pulumi.Aws.GetRegion.Invoke(null, new InvokeOptions { Provider = provider }).Apply(result => result.Region)}:dynamodb:action/PutItem")
    }, new CustomResourceOptions
    {
        Provider = provider
    });

    // Create integration for the GET method
    var getIntegration = new Api.Integration("get", new Api.IntegrationArgs
    {
        ResourceId = resource.Id,
        RestApi = api.Id,
        Credentials = role.Arn,
        HttpMethod = getMethod.HttpMethod,
        Type = "AWS",
        IntegrationHttpMethod = "GET",
        Uri = Output.Format($"arn:aws:apigateway:{Pulumi.Aws.GetRegion.Invoke(null, new InvokeOptions { Provider = provider }).Apply(result => result.Region)}:dynamodb:action/GetItem")
    }, new CustomResourceOptions
    {
        Provider = provider
    });

    // Publish the RestApi to make it available on the internet.
    var deployment = new Api.Deployment("getset", new Api.DeploymentArgs
    {
        RestApi = api.Id,
        Description = "API for demonstrations"
    }, new CustomResourceOptions { Provider = provider });

    var stage = new Api.Stage("getset", new Api.StageArgs
    {
        Deployment = deployment.Id,
        RestApi = api.Id,
        StageName = "primary",
        Description = "API for demonstrations"
    }, new CustomResourceOptions { Provider = provider });

    return new Dictionary<string, object?>
    {
        ["apiUrl"] = stage.InvokeUrl
    };
});

