using System.Collections.Generic;
using System.Linq;
using Pulumi;
using Aws = Pulumi.Aws;

return await Deployment.RunAsync(() =>
{
    var provider = new Aws.Provider("demo", new()
    {
        Region = "ap-southeast-4",
    });

    var table = new Aws.DynamoDB.Table("Values", new()
    {
        Attributes = new[]
        {
            new Aws.DynamoDB.Inputs.TableAttributeArgs
            {
                Name = "id",
                Type = "N",
            },
        },
        BillingMode = "PAY_PER_REQUEST",
        HashKey = "id"
    }, new CustomResourceOptions
    {
        Provider = provider
    });

    var api = new Aws.ApiGateway.RestApi("DemoApp", new()
    {
        Description = "Add and read values from a DynamoDB table"
    }, new CustomResourceOptions
    {
        Provider = provider
    });

    var app = new Aws.ApiGateway.Resource("DemoApp", new()
    {
        RestApi = api.Id,
        ParentId = api.RootResourceId,
        PathPart = "{id}",
    }, new CustomResourceOptions
    {
        Provider = provider
    });

    var deployment = new Aws.ApiGateway.Deployment("DemoApp", new()
    {
        RestApi = api.Id
    }, new CustomResourceOptions
    {
        Provider = provider
    });

    var stage = new Aws.ApiGateway.Stage("DemoApp", new()
    {
        Deployment = deployment.Id,
        RestApi = api.Id,
        StageName = "Demo"
    }, new CustomResourceOptions
    {
        Provider = provider,
        Parent = deployment
    });

#pragma warning disable CA1806 // Do not ignore method results
  new Aws.ApiGateway.UsagePlan("savings", new()
    {
        ApiStages = new[]
        {
            new Aws.ApiGateway.Inputs.UsagePlanApiStageArgs
            {
                ApiId = api.Id,
                Stage = stage.StageName,
            },
        },
        ThrottleSettings = new Aws.ApiGateway.Inputs.UsagePlanThrottleSettingsArgs
        {
            BurstLimit = 1,
            RateLimit = 1,
        },
    }, new CustomResourceOptions
    {
        Provider = provider,
        Parent = stage
    });
#pragma warning restore CA1806 // Do not ignore method results

});

