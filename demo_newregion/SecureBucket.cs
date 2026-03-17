using System.Collections.Generic;
using Pulumi;
using Aws = Pulumi.Aws;

namespace demo_newregion;

/// <summary>
/// Arguments for the SecureBucket component.
/// </summary>
public sealed class SecureBucketArgs : ResourceArgs
{
    /// <summary>
    /// Optional explicit bucket name. If omitted, Pulumi auto-names the bucket.
    /// </summary>
    [Input("bucketName")]
    public Input<string>? BucketName { get; set; }

    /// <summary>
    /// Who pays for requests. Defaults to "BucketOwner".
    /// </summary>
    [Input("requestPayer")]
    public Input<string>? RequestPayer { get; set; }

    /// <summary>
    /// Server-side encryption algorithm. Defaults to "AES256".
    /// </summary>
    [Input("sseAlgorithm")]
    public Input<string>? SseAlgorithm { get; set; }

    /// <summary>
    /// Tags to apply to the bucket.
    /// </summary>
    [Input("tags")]
    public InputMap<string> Tags { get; set; } = new InputMap<string>();
}

/// <summary>
/// A component that creates an S3 bucket with public access blocked and
/// BucketOwnerEnforced ownership controls.
/// </summary>
public class SecureBucket : ComponentResource
{
    [Output("bucketName")]
    public Output<string> BucketName { get; private set; } = null!;

    [Output("arn")]
    public Output<string> Arn { get; private set; } = null!;

    [Output("bucketRegionalDomainName")]
    public Output<string> BucketRegionalDomainName { get; private set; } = null!;

    public SecureBucket(string name, SecureBucketArgs args, ComponentResourceOptions? opts = null)
        : base("demo:index:SecureBucket", name, opts)
    {
        var sseAlgorithm = args.SseAlgorithm ?? "AES256";

        // S3 Bucket
        var bucket = new Aws.S3.Bucket($"{name}-bucket", new()
        {
            BucketName = args.BucketName,
            RequestPayer = args.RequestPayer,
            ServerSideEncryptionConfiguration = new Aws.S3.Inputs.BucketServerSideEncryptionConfigurationArgs
            {
                Rule = new Aws.S3.Inputs.BucketServerSideEncryptionConfigurationRuleArgs
                {
                    ApplyServerSideEncryptionByDefault = new Aws.S3.Inputs.BucketServerSideEncryptionConfigurationRuleApplyServerSideEncryptionByDefaultArgs
                    {
                        SseAlgorithm = sseAlgorithm,
                    },
                },
            },
            Tags = args.Tags,
        }, new CustomResourceOptions { Parent = this });

        // Block all public access
        var publicAccessBlock = new Aws.S3.BucketPublicAccessBlock($"{name}-pab", new()
        {
            Bucket = bucket.BucketName,
        }, new CustomResourceOptions { Parent = this });

        // Enforce bucket-owner ownership
        var ownershipControls = new Aws.S3.BucketOwnershipControls($"{name}-ownership", new()
        {
            Bucket = bucket.BucketName,
            Rule = new Aws.S3.Inputs.BucketOwnershipControlsRuleArgs
            {
                ObjectOwnership = "BucketOwnerEnforced",
            },
        }, new CustomResourceOptions { Parent = this });

        // Expose outputs
        this.BucketName = bucket.BucketName;
        this.Arn = bucket.Arn;
        this.BucketRegionalDomainName = bucket.BucketRegionalDomainName;

        this.RegisterOutputs(new Dictionary<string, object?>
        {
            ["bucketName"] = this.BucketName,
            ["arn"] = this.Arn,
            ["bucketRegionalDomainName"] = this.BucketRegionalDomainName,
        });
    }
}
