using System.Collections.Generic;
using Pulumi;
using Aws = Pulumi.Aws;

// This program provisions an S3 bucket with access controls and a CloudFront distribution.

return await Deployment.RunAsync(() =>
{
    var config = new Config("aws");
    var region = config.Require("region");
    var name = "demo";

    // Provider
    var awsProvider = new Aws.Provider(name, new()
    {
        Region = region,
    });

    // Shared tags
    var migrateToTag = "ap-southeast-6";

    // S3 Bucket with public access block and ownership controls
    var s3Bucket = new demo_newregion.SecureBucket(name, new()
    {
        Tags =
        {
            { "MigrateTo", migrateToTag },
        },
    }, new ComponentResourceOptions
    {
        Provider = awsProvider,
    });

    // CloudFront Origin Access Control
    var cfOriginAccessControl = new Aws.CloudFront.OriginAccessControl(name, new()
    {
        OriginAccessControlOriginType = "s3",
        SigningBehavior = "always",
        SigningProtocol = "sigv4",
    }, new CustomResourceOptions
    {
        Provider = awsProvider,
    });

    // Derived/common outputs for use in CloudFront and policy
    var s3BucketArn = s3Bucket.Arn;
    var s3BucketName = s3Bucket.BucketName;
    var s3OriginId = s3BucketArn; // TargetOriginId/OriginId is an arbitrary string; preserve original ARN-like string
    var s3RegionalDomainName = s3Bucket.BucketRegionalDomainName;

    // CloudFront Distribution
    var cfDistribution = new Aws.CloudFront.Distribution(name, new()
    {
        CustomErrorResponses = new[]
        {
            new Aws.CloudFront.Inputs.DistributionCustomErrorResponseArgs
            {
                ErrorCode = 404,
                ResponseCode = 404,
                ResponsePagePath = "/error.html",
            },
        },
        DefaultCacheBehavior = new Aws.CloudFront.Inputs.DistributionDefaultCacheBehaviorArgs
        {
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
            ForwardedValues = new Aws.CloudFront.Inputs.DistributionDefaultCacheBehaviorForwardedValuesArgs
            {
                Cookies = new Aws.CloudFront.Inputs.DistributionDefaultCacheBehaviorForwardedValuesCookiesArgs
                {
                    Forward = "all",
                },
                QueryString = true,
            },
            MaxTtl = 600,
            MinTtl = 600,
            TargetOriginId = s3OriginId,
            ViewerProtocolPolicy = "redirect-to-https",
        },
        DefaultRootObject = "index.html",
        Enabled = true,
        Origins = new[]
        {
            new Aws.CloudFront.Inputs.DistributionOriginArgs
            {
                DomainName = s3RegionalDomainName,
                OriginAccessControlId = cfOriginAccessControl.Id,
                OriginId = s3OriginId,
            },
        },
        PriceClass = "PriceClass_100",
        Restrictions = new Aws.CloudFront.Inputs.DistributionRestrictionsArgs
        {
            GeoRestriction = new Aws.CloudFront.Inputs.DistributionRestrictionsGeoRestrictionArgs
            {
                Locations = new[]
                {
                    "NZ",
                },
                RestrictionType = "whitelist",
            },
        },
        Tags =
        {
            { "MigrateTo", migrateToTag },
        },
        ViewerCertificate = new Aws.CloudFront.Inputs.DistributionViewerCertificateArgs
        {
            CloudfrontDefaultCertificate = true,
            MinimumProtocolVersion = "TLSv1",
        },
    }, new CustomResourceOptions
    {
        Provider = awsProvider,
    });

    // S3 Bucket Policy
    var s3BucketPolicy = new Aws.S3.BucketPolicy(name, new()
    {
        Bucket = s3BucketName,
        Policy = Output.Tuple(cfDistribution.Arn, s3BucketArn).Apply(items =>
        {
            var distArn = items.Item1;
            var bucketArn = items.Item2;
            return $@"{{
  ""Statement"": [
    {{
      ""Action"": ""s3:GetObject"",
      ""Condition"": {{
        ""StringEquals"": {{
          ""AWS:SourceArn"": ""{distArn}""
        }}
      }},
      ""Effect"": ""Allow"",
      ""Principal"": {{
        ""Service"": ""cloudfront.amazonaws.com""
      }},
      ""Resource"": ""{bucketArn}/*"",
      ""Sid"": ""PublicReadGetObject""
    }}
  ],
  ""Version"": ""2012-10-17""
}}";
        }),
    }, new CustomResourceOptions
    {
        Provider = awsProvider,
    });

    // Stack exports
    return new Dictionary<string, object?>
    {
        ["websiteUrl"] = Output.Format($"https://{cfDistribution.DomainName}"),
        ["bucketName"] = s3BucketName,
    };
});
