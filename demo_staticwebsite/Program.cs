using System.Collections.Generic;
using Pulumi;
using Aws = Pulumi.Aws;
// using SyncedFolder = Pulumi.SyncedFolder;

return await Pulumi.Deployment.RunAsync(() =>
{
    // Import the program's configuration settings.
    var config = new Config();
    var path = config.Get("path") ?? "./www";
    var indexDocument = config.Get("indexDocument") ?? "index.html";
    var errorDocument = config.Get("errorDocument") ?? "error.html";

    var provider = new Aws.Provider("apse2", new Aws.ProviderArgs { Profile = "admin-cms", Region = Aws.Region.APSoutheast2.ToString() });

    // Create an S3 bucket and configure it as a website.
    var bucket = new Aws.S3.Bucket("bucket", null, new CustomResourceOptions { Provider = provider });

    var bucketWebsite = new Aws.S3.BucketWebsiteConfiguration("bucket", new()
    {
        Bucket = bucket.Id,
        IndexDocument = new Aws.S3.Inputs.BucketWebsiteConfigurationIndexDocumentArgs
        {
            Suffix = indexDocument,
        },
        ErrorDocument = new Aws.S3.Inputs.BucketWebsiteConfigurationErrorDocumentArgs
        {
            Key = errorDocument,
        },
    }, new CustomResourceOptions { Provider = provider });

    // Configure ownership controls for the new S3 bucket
    var ownershipControls = new Aws.S3.BucketOwnershipControls("ownership-controls", new()
    {
        Bucket = bucket.Id,
        Rule = new Aws.S3.Inputs.BucketOwnershipControlsRuleArgs
        {
            ObjectOwnership = "BucketOwnerEnforced"
        },
    }, new CustomResourceOptions { Provider = provider });

    // Configure public access block for the new S3 bucket
    var publicAccessBlock = new Aws.S3.BucketPublicAccessBlock("public-access-block", new()
    {
        Bucket = bucket.Id,
        BlockPublicAcls = false,
    }, new CustomResourceOptions { Provider = provider });

    new Aws.S3.BucketObject("index.html", new Aws.S3.BucketObjectArgs
    {
        Bucket = bucket.BucketName,
        ContentType = "text/html",
        Source = new Pulumi.FileAsset($"{path}/{indexDocument}")
    }, new CustomResourceOptions { Provider = provider });
    new Aws.S3.BucketObject("error.html", new Aws.S3.BucketObjectArgs
    {
        Bucket = bucket.BucketName,
        ContentType = "text/html",
        Source = new Pulumi.FileAsset($"{path}/{errorDocument}")
    }, new CustomResourceOptions { Provider = provider });

    // Create a CloudFront CDN to distribute and cache the website.
    var cdn = new Aws.CloudFront.Distribution("cdn", new()
    {
        Enabled = true,
        Origins = new[]
        {
            new Aws.CloudFront.Inputs.DistributionOriginArgs
            {
                OriginId = bucket.Arn,
                DomainName = bucketWebsite.WebsiteEndpoint,
                CustomOriginConfig = new Aws.CloudFront.Inputs.DistributionOriginCustomOriginConfigArgs
                {
                    OriginProtocolPolicy = "http-only",
                    HttpPort = 80,
                    HttpsPort = 443,
                    OriginSslProtocols = new[]
                    {
                        "TLSv1.2",
                    },
                },
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
                RestrictionType = "none",
            },
        },
        ViewerCertificate = new Aws.CloudFront.Inputs.DistributionViewerCertificateArgs
        {
            CloudfrontDefaultCertificate = true,
        },
    }, new CustomResourceOptions { Provider = provider });

    var policy = Output.JsonSerialize(Output.Create(new
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
                Resource = Output.Format($"arn:aws:s3:::{bucket.BucketName}/*"),
                Condition = new
                {
                    StringEquals = new Dictionary<string, Output<string>>
                    {
                        ["AWS:SourceArn"] = Output.Format($"arn:aws:cloudfront::155742740775:distribution/{cdn.Id}")
                    }
                }
            }
        }
    }));

    new Aws.S3.BucketPolicy("public", new Aws.S3.BucketPolicyArgs
    {
        Bucket = bucket.Id,
        Policy = policy
    }, new CustomResourceOptions { Provider = provider });

    // Export the URLs and hostnames of the bucket and distribution.
    return new Dictionary<string, object?>
    {
        ["originURL"] = Output.Format($"http://{bucketWebsite.WebsiteEndpoint}"),
        ["originHostname"] = bucket.WebsiteEndpoint,
        ["cdnURL"] = Output.Format($"https://{cdn.DomainName}"),
        ["cdnHostname"] = cdn.DomainName,
        ["policy"] = policy
    };
});
