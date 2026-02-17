using Pulumi;
using Aws = Pulumi.Aws;

return await Deployment.RunAsync(() => 
{
    var aws_sentify_demo_ap_southeast_6 = new Aws.Provider("aws_sentify_demo_ap-southeast-6", new()
    {
        Region = "ap-southeast-6",
        Profile = "admin-cms"
    });

    var mutual_poc_truststore = new Aws.S3.Bucket("mutual-poc-truststore", new()
    {
        BucketName = "mutual-poc-truststore-akl",
        RequestPayer = "BucketOwner",
        ServerSideEncryptionConfiguration = new Aws.S3.Inputs.BucketServerSideEncryptionConfigurationArgs
        {
            Rule = new Aws.S3.Inputs.BucketServerSideEncryptionConfigurationRuleArgs
            {
                ApplyServerSideEncryptionByDefault = new Aws.S3.Inputs.BucketServerSideEncryptionConfigurationRuleApplyServerSideEncryptionByDefaultArgs
                {
                    SseAlgorithm = "AES256",
                },
                BucketKeyEnabled = true,
            },
        },
    }, new CustomResourceOptions
    {
        Provider = aws_sentify_demo_ap_southeast_6,
    });

});

