using System.Collections.Generic;
using System.Linq;
using Pulumi;
using Pulumi.Serialization;
using Aws = Pulumi.Aws;

/*
Refactored Pulumi program creating an S3 bucket with policy/controls and a CloudFront distribution/OAC.
Improvements: replaced hardcoded intra-stack identifiers with typed references, clarified names, added comments, and deduplicated literals.
*/

return await Deployment.RunAsync(() =>
{
    // Shared literals
    const string migrateToRegionTag = "ap-southeast-6";
    const string bucketName = "demo-9cc426a";
    const string defaultRoot = "index.html";
    const int defaultTtl = 600;

    // AWS providers
    var aws_sentify_demo_global = new Aws.Provider("aws_sentify_demo_global", new()
    {
        Region = "global",
    });

    var aws_sentify_demo_ap_southeast_2 = new Aws.Provider("aws_sentify_demo_ap-southeast-2", new()
    {
        Region = "ap-southeast-2",
    });

    // S3 bucket for website content
    var bucket = new Aws.S3.Bucket("demo-9cc426a", new()
    {
        BucketName = bucketName,
        RequestPayer = "BucketOwner",
        ServerSideEncryptionConfiguration = new Aws.S3.Inputs.BucketServerSideEncryptionConfigurationArgs
        {
            Rule = new Aws.S3.Inputs.BucketServerSideEncryptionConfigurationRuleArgs
            {
                ApplyServerSideEncryptionByDefault = new Aws.S3.Inputs.BucketServerSideEncryptionConfigurationRuleApplyServerSideEncryptionByDefaultArgs
                {
                    SseAlgorithm = "AES256",
                },
            },
        },
        Tags =
        {
            { "MigrateTo", migrateToRegionTag },
        },
    }, new CustomResourceOptions
    {
        Provider = aws_sentify_demo_ap_southeast_2,
        ImportId = "demo-9cc426a",
    });

    // S3 public access block
    var bucketPublicAccessBlock = new Aws.S3.BucketPublicAccessBlock("demo-9cc426a_1", new()
    {
        Bucket = bucket.Id, // was "demo-9cc426a"
    }, new CustomResourceOptions
    {
        Provider = aws_sentify_demo_ap_southeast_2,
        ImportId = "demo-9cc426a",
    });

    // CloudFront Origin Access Control
    var originAccessControl = new Aws.CloudFront.OriginAccessControl("E2OS23KWF95585", new()
    {
        Name = "demo-e10cc0e",
        OriginAccessControlOriginType = "s3",
        SigningBehavior = "always",
        SigningProtocol = "sigv4",
    }, new CustomResourceOptions
    {
        Provider = aws_sentify_demo_global,
        ImportId = "E2OS23KWF95585",
    });

    // CloudFront distribution serving the S3 bucket
    var distribution = new Aws.CloudFront.Distribution("E1RNY9HPGY8YPZ", new()
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
            DefaultTtl = defaultTtl,
            ForwardedValues = new Aws.CloudFront.Inputs.DistributionDefaultCacheBehaviorForwardedValuesArgs
            {
                Cookies = new Aws.CloudFront.Inputs.DistributionDefaultCacheBehaviorForwardedValuesCookiesArgs
                {
                    Forward = "all",
                },
                QueryString = true,
            },
            MaxTtl = defaultTtl,
            MinTtl = defaultTtl,
            // Use S3 bucket ARN for TargetOriginId to match the original literal form
            TargetOriginId = bucket.Arn,
            ViewerProtocolPolicy = "redirect-to-https",
        },
        DefaultRootObject = defaultRoot,
        Enabled = true,
        HttpVersion = "http2",
        Origins = new[]
        {
            new Aws.CloudFront.Inputs.DistributionOriginArgs
            {
                // Use the bucket's regional domain name
                DomainName = bucket.RegionalDomainName,
                OriginAccessControlId = originAccessControl.Id, // was "E2OS23KWF95585"
                // Keep origin id consistent with DefaultCacheBehavior.TargetOriginId
                OriginId = bucket.Arn, // was "arn:aws:s3:::demo-9cc426a"
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
            { "MigrateTo", migrateToRegionTag },
        },
        ViewerCertificate = new Aws.CloudFront.Inputs.DistributionViewerCertificateArgs
        {
            CloudfrontDefaultCertificate = true,
            MinimumProtocolVersion = "TLSv1",
        },
    }, new CustomResourceOptions
    {
        Provider = aws_sentify_demo_global,
        ImportId = "E1RNY9HPGY8YPZ",
    });

    // S3 bucket policy allowing CloudFront to GetObject from the bucket
    var bucketPolicy = new Aws.S3.BucketPolicy("demo-9cc426a_2", new()
    {
        Bucket = bucket.Id, // was "demo-9cc426a"
        Policy = Output.Tuple(bucket.Arn, distribution.Arn).Apply(items =>
        {
            var (bArn, distArn) = items;
            // Construct policy JSON with referenced ARNs
            var policy = new Dictionary<string, object?>
            {
                ["Version"] = "2012-10-17",
                ["Statement"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["Sid"] = "PublicReadGetObject",
                        ["Effect"] = "Allow",
                        ["Principal"] = new Dictionary<string, object?>
                        {
                            ["Service"] = "cloudfront.amazonaws.com",
                        },
                        ["Action"] = "s3:GetObject",
                        ["Resource"] = $"{bArn}/*",
                        ["Condition"] = new Dictionary<string, object?>
                        {
                            ["StringEquals"] = new Dictionary<string, object?>
                            {
                                // Preserve original SourceArn semantics with the distribution ARN
                                ["AWS:SourceArn"] = distArn,
                            },
                        },
                    },
                },
            };
            return policy;
        }).Apply(p => System.Text.Json.JsonSerializer.Serialize(p)),
    }, new CustomResourceOptions
    {
        Provider = aws_sentify_demo_ap_southeast_2,
        ImportId = "demo-9cc426a",
    });

    // S3 ownership controls
    var bucketOwnershipControls = new Aws.S3.BucketOwnershipControls("demo-9cc426a_3", new()
    {
        Bucket = bucket.Id, // was "demo-9cc426a"
        Rule = new Aws.S3.Inputs.BucketOwnershipControlsRuleArgs
        {
            ObjectOwnership = "BucketOwnerEnforced",
        },
    }, new CustomResourceOptions
    {
        Provider = aws_sentify_demo_ap_southeast_2,
        ImportId = "demo-9cc426a",
    });
});
