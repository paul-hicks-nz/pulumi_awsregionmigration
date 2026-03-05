using System.Collections.Generic;
using Pulumi;
using Pulumi.Aws.Inputs;
using Aws = Pulumi.Aws;

return await Deployment.RunAsync(() =>
{
    // Import the program's configuration settings.
    var config = new Config();
    var path = config.Get("path") ?? "./www";
    var indexDocument = config.Get("indexDocument") ?? "index.html";
    var errorDocument = config.Get("errorDocument") ?? "error.html";

    var provider = new Aws.Provider("apse2", new Aws.ProviderArgs
    {
        Profile = "admin-cms",
        Region = Aws.Region.APSoutheast2.ToString(),
        DefaultTags = new ProviderDefaultTagsArgs
        {
            Tags = {
                ["MigrateTo"] = "ap-southeast-6"
            }
        }
    });

    // Create an S3 bucket and configure it as a website.
    var bucket = new Aws.S3.Bucket("demo", null, new CustomResourceOptions { Provider = provider });

    // Configure ownership controls for the new S3 bucket
    var ownershipControls = new Aws.S3.BucketOwnershipControls("demo", new()
    {
        Bucket = bucket.Id,
        Rule = new Aws.S3.Inputs.BucketOwnershipControlsRuleArgs
        {
            ObjectOwnership = "BucketOwnerEnforced"
        },
    }, new CustomResourceOptions { Provider = provider });

    // Configure public access block for the new S3 bucket
    var publicAccessBlock = new Aws.S3.BucketPublicAccessBlock("demo", new()
    {
        Bucket = bucket.Id,
        BlockPublicAcls = false,
    }, new CustomResourceOptions { Provider = provider });

    var @index = new Aws.S3.BucketObject("index.html", new Aws.S3.BucketObjectArgs
    {
        Bucket = bucket.BucketName,
        ContentType = "text/html",
        Source = new FileAsset($"{path}/{indexDocument}")
    }, new CustomResourceOptions { Provider = provider });
    var @error = new Aws.S3.BucketObject("error.html", new Aws.S3.BucketObjectArgs
    {
        Bucket = bucket.BucketName,
        ContentType = "text/html",
        Source = new FileAsset($"{path}/{errorDocument}")
    }, new CustomResourceOptions { Provider = provider });

    var oac = new Aws.CloudFront.OriginAccessControl("demo", new()
    {
        OriginAccessControlOriginType = "s3",
        SigningBehavior = "always",
        SigningProtocol = "sigv4"
    }, new CustomResourceOptions { Provider = provider });

    // Create a CloudFront CDN to distribute and cache the website.
    var cdn = new Aws.CloudFront.Distribution("demo", new()
    {
        Enabled = true,
        DefaultRootObject = "index.html",
        Origins = new[]
        {
            new Aws.CloudFront.Inputs.DistributionOriginArgs
            {
                OriginId = bucket.Arn,
                DomainName = bucket.BucketRegionalDomainName,
                OriginAccessControlId = oac.Id
            },
        },
        DefaultCacheBehavior = new Aws.CloudFront.Inputs.DistributionDefaultCacheBehaviorArgs
        {
            TargetOriginId = bucket.Arn,
            ViewerProtocolPolicy = "redirect-to-https",
            AllowedMethods = new[]
            {
                "GET",
                "HEAD",
                "OPTIONS",
            },
            CachedMethods = new[]
            {
                "GET",
                "HEAD",
                "OPTIONS",
            },
            DefaultTtl = 600,
            MaxTtl = 600,
            MinTtl = 600,
            ForwardedValues = new Aws.CloudFront.Inputs.DistributionDefaultCacheBehaviorForwardedValuesArgs
            {
                QueryString = true,
                Cookies = new Aws.CloudFront.Inputs.DistributionDefaultCacheBehaviorForwardedValuesCookiesArgs
                {
                    Forward = "all",
                },
            },
        },
        PriceClass = "PriceClass_100",
        CustomErrorResponses = new[]
        {
            new Aws.CloudFront.Inputs.DistributionCustomErrorResponseArgs
            {
                ErrorCode = 404,
                ResponseCode = 404,
                ResponsePagePath = $"/{errorDocument}",
            },
        },
        Restrictions = new Aws.CloudFront.Inputs.DistributionRestrictionsArgs
        {
            GeoRestriction = new Aws.CloudFront.Inputs.DistributionRestrictionsGeoRestrictionArgs
            {
                RestrictionType = "whitelist",
                Locations = new[] { "NZ" }
            },
        },
        ViewerCertificate = new Aws.CloudFront.Inputs.DistributionViewerCertificateArgs
        {
            CloudfrontDefaultCertificate = true,
        },
    }, new CustomResourceOptions { Provider = provider });

    var @policy = new Aws.S3.BucketPolicy("public", new Aws.S3.BucketPolicyArgs
    {
        Bucket = bucket.Id,
        Policy = Output.JsonSerialize(Output.Create(new
        {
            Version = Aws.Iam.PolicyDocumentVersion.PolicyDocumentVersion_2012_10_17.ToString(),
            Statement = new[] {
            new {
                Sid = "PublicReadGetObject",
                Effect = Aws.Iam.PolicyStatementEffect.ALLOW.ToString(),
                Principal = new
                {
                    Service = "cloudfront.amazonaws.com"
                },
                Action = "s3:GetObject",
                Resource = Output.Format($"{bucket.Arn}/*"),
                Condition = new
                {
                    StringEquals = new Dictionary<string, Output<string>>
                    {
                        ["AWS:SourceArn"] = cdn.Arn
                    }
                }
            }
        }
        }))
    }, new CustomResourceOptions { Provider = provider });

    // Export the URLs and hostnames of the bucket and distribution.
    return new Dictionary<string, object?>
    {
        ["cdnURL"] = Output.Format($"https://{cdn.DomainName}")
    };
});
