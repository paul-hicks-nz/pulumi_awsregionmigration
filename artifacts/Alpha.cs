using System.Collections.Generic;
using System.Text.Json;
using Pulumi;
using Pulumi.Serialization;
using Aws = Pulumi.Aws;

/*
Pulumi program that provisions an S3 bucket (with ownership controls, policy, and PAB) and a CloudFront distribution using an Origin Access Control.
Refactor highlights: replaces cross-resource literals with typed outputs, clarifies variable/resource names, adds comments/spacing, and deduplicates literals.
*/

namespace demo_newregion
{
    class Alpha
    {
        public static IDictionary<string, object?> StackFunc()

        {    // Shared literals
            const string tagMigrateToKey = "MigrateTo";
            const string tagMigrateToValue = "ap-southeast-6";
            const string defaultRootObject = "index.html";
            const int cacheTtl = 600;

            // Providers
            var awsGlobal = new Aws.Provider("aws_sentify_demo_global", new()
            {
                Region = "global",
            });

            var awsApSoutheast2 = new Aws.Provider("aws_sentify_demo_ap-southeast-2", new()
            {
                Region = "ap-southeast-2",
            });

            // S3 bucket
            var s3Bucket = new Aws.S3.Bucket("demo-9cc426a", new()
            {
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
            { tagMigrateToKey, tagMigrateToValue },
        },
            }, new CustomResourceOptions
            {
                Provider = awsApSoutheast2,
            });

            // S3 bucket ownership controls
            var s3BucketOwnership = new Aws.S3.BucketOwnershipControls("demo-9cc426a_1", new()
            {
                Bucket = s3Bucket.Bucket,
                Rule = new Aws.S3.Inputs.BucketOwnershipControlsRuleArgs
                {
                    ObjectOwnership = "BucketOwnerEnforced",
                },
            }, new CustomResourceOptions
            {
                Provider = awsApSoutheast2,
            });

            // Origin Access Control for CloudFront -> S3
            var originAccessControl = new Aws.CloudFront.OriginAccessControl("E2OS23KWF95585", new()
            {
                OriginAccessControlOriginType = "s3",
                SigningBehavior = "always",
                SigningProtocol = "sigv4",
            }, new CustomResourceOptions
            {
                Provider = awsGlobal,
            });

            // Common S3 identifiers
            var s3BucketArn = s3Bucket.Arn;
            var s3BucketName = s3Bucket.Bucket;
            var s3BucketRegionalDomainName = s3Bucket.BucketRegionalDomainName;

            // CloudFront distribution
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

                    // Keep consistent with Origin. Use bucket ARN for a stable OriginId
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
                DomainName = s3BucketRegionalDomainName,
                OriginAccessControlId = originAccessControl.Id,
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
                Tags =
        {
            { tagMigrateToKey, tagMigrateToValue },
        },
                ViewerCertificate = new Aws.CloudFront.Inputs.DistributionViewerCertificateArgs
                {
                    CloudfrontDefaultCertificate = true,
                    MinimumProtocolVersion = "TLSv1",
                },
            }, new CustomResourceOptions
            {
                Provider = awsGlobal,
            });

            // S3 bucket policy allowing CloudFront to GetObject (scoped to distribution ARN)
            var s3BucketPolicy = new Aws.S3.BucketPolicy("demo-9cc426a_2", new()
            {
                Bucket = s3BucketName,
                Policy = Output.Tuple(s3BucketName, cfDistribution.Arn).Apply(items =>
                {
                    var bucket = items.Item1;
                    var distributionArn = items.Item2;

                    var policy = new
                    {
                        Statement = new[]
                        {
                    new Dictionary<string, object?>
                    {
                        { "Sid", "PublicReadGetObject" },
                        { "Effect", "Allow" },
                        { "Principal", new Dictionary<string, object?> { { "Service", "cloudfront.amazonaws.com" } } },
                        { "Action", "s3:GetObject" },
                        { "Resource", $"arn:aws:s3:::{bucket}/*" },
                        { "Condition", new Dictionary<string, object?>
                            {
                                { "StringEquals", new Dictionary<string, object?>
                                    {
                                        { "AWS:SourceArn", distributionArn }
                                    }
                                }
                            }
                        },
                    }
                        },
                        Version = "2012-10-17",
                    };
                    return JsonSerializer.Serialize(policy);
                }),
            }, new CustomResourceOptions
            {
                Provider = awsApSoutheast2,
            });

            // S3 bucket Public Access Block
            var s3BucketPublicAccessBlock = new Aws.S3.BucketPublicAccessBlock("demo-9cc426a_3", new()
            {
                Bucket = s3BucketName,
            }, new CustomResourceOptions
            {
                Provider = awsApSoutheast2,
            });

            return new Dictionary<string, object?>
            {
                ["cdnURL"] = Output.Format($"https://{cfDistribution.DomainName}")
            };
        }
    }
}
