using System.Collections.Generic;
using Pulumi;
using Pulumi.Aws.Inputs;
using Aws = Pulumi.Aws;

/*
Refactored Pulumi program that provisions an S3 bucket with ownership/policy/PAB and a CloudFront distribution using an Origin Access Control.
Improvements: replaces hardcoded cross-resource identifiers with typed references, clarifies names/variables, adds brief comments and spacing, and deduplicates literals.
*/

return await Deployment.RunAsync(() =>
{
    // Shared literals
    const string name = "demo";
    const string tagMigrateToKey = "MigrateTo";
    const string tagMigrateToValue = "ap-southeast-6";
    const string defaultRootObject = "index.html";
    const int cacheTtl = 600;
    var methods = new[] { "GET", "HEAD", "OPTIONS" };

    var provider = new Aws.Provider(name, new Aws.ProviderArgs
    {
        Profile = "admin-cms",
        Region = Aws.Region.APSoutheast4.ToString(),
        DefaultTags = new ProviderDefaultTagsArgs
        {
            Tags = {
                [tagMigrateToKey] = tagMigrateToValue
            }
        }
    });

    // S3 bucket
    var s3Bucket = new Aws.S3.Bucket(name, null, new CustomResourceOptions { Provider = provider });

    // S3 bucket ownership controls
    var s3BucketOwnership = new Aws.S3.BucketOwnershipControls(name, new()
    {
        // Use bucket name output rather than hardcoded string
        Bucket = s3Bucket.BucketName,
        Rule = new Aws.S3.Inputs.BucketOwnershipControlsRuleArgs
        {
            ObjectOwnership = "BucketOwnerEnforced",
        },
    }, new CustomResourceOptions { Provider = provider });

    // Origin Access Control for CloudFront -> S3
    var oac = new Aws.CloudFront.OriginAccessControl(name, new()
    {
        OriginAccessControlOriginType = "s3",
        SigningBehavior = "always",
        SigningProtocol = "sigv4",
    }, new CustomResourceOptions { Provider = provider });

    // Build commonly reused S3 identifiers
    var s3BucketArn = s3Bucket.Arn;
    var s3BucketName = s3Bucket.BucketName;
    var s3BucketRegionalDomainName = s3Bucket.BucketRegionalDomainName;

    var @index = new Aws.S3.BucketObject("index.html", new Aws.S3.BucketObjectArgs
    {
        Bucket = s3BucketName,
        ContentType = "text/html",
        Source = new FileAsset("./www/index.html")
    }, new CustomResourceOptions { Provider = provider });
    var @error = new Aws.S3.BucketObject("error.html", new Aws.S3.BucketObjectArgs
    {
        Bucket = s3BucketName,
        ContentType = "text/html",
        Source = new FileAsset("./www/error.html")
    }, new CustomResourceOptions { Provider = provider });

    // CloudFront distribution
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
            AllowedMethods = methods,
            CachedMethods = methods,
            DefaultTtl = cacheTtl,
            ForwardedValues = new Aws.CloudFront.Inputs.DistributionDefaultCacheBehaviorForwardedValuesArgs
            {
                Cookies = new Aws.CloudFront.Inputs.DistributionDefaultCacheBehaviorForwardedValuesCookiesArgs
                {
                    Forward = "all",
                },
                QueryString = true,
            },
            MaxTtl = cacheTtl,
            MinTtl = cacheTtl,

            // TargetOriginId must match the defined OriginId below; use the bucket ARN consistently
            TargetOriginId = s3BucketArn,
            ViewerProtocolPolicy = "redirect-to-https",
        },
        DefaultRootObject = defaultRootObject,
        Enabled = true,
        HttpVersion = "http2",
        Origins = new[]
        {
            new Aws.CloudFront.Inputs.DistributionOriginArgs
            {
                // Use the bucket's regional domain name for S3 origin
                DomainName = s3BucketRegionalDomainName,

                // Reference the created OAC by its ID output
                OriginAccessControlId = oac.Id,

                // Keep OriginId consistent with DefaultCacheBehavior.TargetOriginId
                OriginId = s3BucketArn,
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
        ViewerCertificate = new Aws.CloudFront.Inputs.DistributionViewerCertificateArgs
        {
            CloudfrontDefaultCertificate = true,
            MinimumProtocolVersion = "TLSv1",
        },
    }, new CustomResourceOptions { Provider = provider });

    // S3 bucket policy allowing CloudFront to GetObject from this bucket (via distribution ARN)
    var s3BucketPolicy = new Aws.S3.BucketPolicy(name, new()
    {
        Bucket = s3BucketName,
        Policy = Output.JsonSerialize(Output.Create(new
        {
            Version = Aws.Iam.PolicyDocumentVersion.PolicyDocumentVersion_2012_10_17.ToString(),
            Statement = new[] {
                new {
                    Sid = "PublicReadGetObject",
                    Effect= Aws.Iam.PolicyStatementEffect.ALLOW.ToString(),
                    Principal = new
                    {
                        Service = "cloudfront.amazonaws.com"
                    },
                    Action = "s3:GetObject",
                    Resource = Output.Format($"{s3BucketArn}/*"),
                    Condition = new {
                        StringEquals = new Dictionary<string, Output<string>> {
                            // Expecting the CloudFront distribution ARN
                            ["AWS:SourceArn"] = cfDistribution.Arn
                        }
                    },
                }
            },
        }))
    }, new CustomResourceOptions { Provider = provider });

    // S3 bucket Public Access Block
    var s3BucketPublicAccessBlock = new Aws.S3.BucketPublicAccessBlock(name, new()
    {
        Bucket = s3BucketName,
    }, new CustomResourceOptions { Provider = provider });

    return new Dictionary<string, object?>
    {
        ["cdnURL"] = Output.Format($"https://{cfDistribution.DomainName}")
    };

});