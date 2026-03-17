using System.Collections.Generic;
using System.Linq;
using Pulumi;
using Aws = Pulumi.Aws;

// This program provisions an S3 bucket with access controls and a CloudFront distribution.
// Improvements: replaced hardcoded resource identifiers with typed references, clarified names, added comments, and minor formatting.

return await Deployment.RunAsync(() =>
{
    // Providers
    var awsApSoutheast2 = new Aws.Provider("aws_sentify_demo_ap-southeast-2", new()
    {
        Region = "ap-southeast-2",
    });

    // Shared tags
    var migrateToTag = "ap-southeast-6";

    // S3 Bucket (imported)
    var s3Bucket = new Aws.S3.Bucket("demo-9cc426a_1", new()
    {
        BucketName = "demo-9cc426a",
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
            { "MigrateTo", migrateToTag },
        },
    }, new CustomResourceOptions
    {
        Provider = awsApSoutheast2,
        ImportId = "demo-9cc426a",
    });

    // Public Access Block for S3 Bucket (imported)
    var s3BucketPublicAccessBlock = new Aws.S3.BucketPublicAccessBlock("demo-9cc426a", new()
    {
        // Use the bucket's name output instead of a literal
        Bucket = s3Bucket.BucketName,
    }, new CustomResourceOptions
    {
        Provider = awsApSoutheast2,
        ImportId = "demo-9cc426a",
    });

    // Ownership Controls for S3 Bucket (imported)
    var s3BucketOwnershipControls = new Aws.S3.BucketOwnershipControls("demo-9cc426a_3", new()
    {
        Bucket = s3Bucket.BucketName,
        Rule = new Aws.S3.Inputs.BucketOwnershipControlsRuleArgs
        {
            ObjectOwnership = "BucketOwnerEnforced",
        },
    }, new CustomResourceOptions
    {
        Provider = awsApSoutheast2,
        ImportId = "demo-9cc426a",
    });

    // CloudFront Origin Access Control (imported)
    var cfOriginAccessControl = new Aws.CloudFront.OriginAccessControl("E2OS23KWF95585", new()
    {
        Name = "demo-e10cc0e",
        OriginAccessControlOriginType = "s3",
        SigningBehavior = "always",
        SigningProtocol = "sigv4",
    }, new CustomResourceOptions
    {
        Provider = awsApSoutheast2,
        ImportId = "E2OS23KWF95585",
    });

    // Derived/common outputs for use in CloudFront and policy
    var s3BucketArn = s3Bucket.Arn;
    var s3BucketName = s3Bucket.BucketName;
    var s3OriginId = s3BucketArn; // TargetOriginId/OriginId is an arbitrary string; preserve original ARN-like string
    var s3RegionalDomainName = s3Bucket.BucketRegionalDomainName;

    // CloudFront Distribution (imported)
    var cfDistribution = new Aws.CloudFront.Distribution("E1RNY9HPGY8YPZ", new()
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
            // Use the same string value pattern as original: an ARN-like string for OriginId/TargetOriginId
            TargetOriginId = s3OriginId,
            ViewerProtocolPolicy = "redirect-to-https",
        },
        DefaultRootObject = "index.html",
        Enabled = true,
        HttpVersion = "http2",
        Origins = new[]
        {
            new Aws.CloudFront.Inputs.DistributionOriginArgs
            {
                // Use the bucket's regional domain name instead of hard-coded domain
                DomainName = s3RegionalDomainName,
                // Reference the created Origin Access Control's ID
                OriginAccessControlId = cfOriginAccessControl.Id,
                // Keep the same origin ID string (matches TargetOriginId)
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
        Provider = awsApSoutheast2,
        ImportId = "E1RNY9HPGY8YPZ",
    });

    // S3 Bucket Policy (imported), referencing the CloudFront distribution and S3 bucket via outputs
    var s3BucketPolicy = new Aws.S3.BucketPolicy("demo-9cc426a_2", new()
    {
        Bucket = s3BucketName,
        Policy = Output.Tuple(cfDistribution.Arn, s3BucketArn).Apply(items =>
        {
            var distArn = items.Item1;
            var bucketArn = items.Item2;
            // Maintain exact semantics while replacing literals with references
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
        Provider = awsApSoutheast2,
        ImportId = "demo-9cc426a",
    });

    // Stack exports
    return new Dictionary<string, object?>
    {
        ["websiteUrl"] = Output.Format($"https://{cfDistribution.DomainName}"),
        ["bucketName"] = s3BucketName,
    };
});