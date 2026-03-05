## Prerequisites / Preparation

Working in Linux (Ubunti 24.04).
Dotnet installed via `apt install dotnet-sdk-10.0`.
Installed extensions 'C#' and 'C# Dev Ket', both pushlished by Microsoft.

# What I Planned to Do

Asked Neo how to do this. [The answer](./neo_response.md) could be improved but does provide some helpful insights.

Now need to make some decisions:

## Where?

### AWS

I think an unused Sentify account would be most flexible, but the CMS demo account is earmarked for this sort of work. Let's start with "cms-demo.aws", and if there's already resources in there that means it's unsuitable for this demo, then we can switch to "Log Archive", which has never been used.

### Pulumi

It would be nice to use a free account. However, Insights isn't available on a free account, though it can be enabled temporarily via a trial request.
If necessary, I can use the Puluminaries org, which has Insights enabled, for discovery. It should be possible to use my own org or a new free org for the deployment.
Engin and Aurelien are happy to set up the account appropriately.

## How?

Following the getting started docs (https://www.pulumi.com/docs/insights/discovery/get-started/begin/) and the [Neo-generated response](./neo_response.md) should get me most of the way. I plan not to import the source resources, but instead to use the import feature with the options `--generate-code --preview-only`

# What I Did

The base stuff is described above.

## Set up Pulumi

I created a trial org, with 14 days enterprise access.
In the Pulumi app under "Environments", I clicked "Create Environment" and selected "Login Provider Setup" and used SSO. This was really easy, it just needed a start URL.

With credentials sorted, I went to Management > Accounts and clicked "Add new". I selected AWS, picked a name ("sentify_demo"), selected the credentials from the previous step, and selected the regions I wanted to scan. Then I clicked the account (top level, not a region), then Actions > Scan.

## Experiment 1

I went to Resources, clicked the robot face, entered "find all resources in sentify_demo in stack ap-southeast-2" (turns out "stack" is important), and saw results. Clicked the magnifying glass icon to see the real syntax.

Created a new directory and ran this to set up a Pulumi project and stack:

```
pulumi new aws-csharp
pulumi install
pulumi stack init one # Since this is experiment number one.

Select Stacks, click Import, put the query from above in, and then select which resources to import.
```
A challenge is to find and exclude "default" resources, like the default VPC, internet gateways, RDS parameter groups, and keys. The query can be quite complex, and I've got this far:

## Experiment 2

In the Pulumi app, go to Stacks, click Import, and build a suitable query. It should include _at least_ all the resources you want to import, but it may include more, because you'll be given checkboxes for each resource.

There's some problems with "Pulumi AI Assist".
* `category storage` works but `storage` doesn't.
* `module = cloudfront` works and translates to search syntax `module:cloudfront`. `module cloudfront` also works but translates to search syntax `type:"aws:cloudfront/distribution:Distribution"`. Exsqueeze me? Baking powder?
* Logic is very hard to get right. The search syntax `category:network or category:storage` is hard to produce from AI assist. Eventually I got this to work: `(category is network)  (category is storage)`. But check these out:
  * category network or category storage
  * network or storage category
  They both produce `category:(network or storage)` which is invalid. And I loved `anything in network or storage` which translated to search syntax `type:(network|storage)`. No idea why.

There's also some gotchas that make sense.
* It won't catch resources that aren't cloud resources. E.g. Pulumi can track objects in buckets as BucketObjects.. but AWS doesn't.
* Some Pulumi resources are just properties on other resources: this shouldn't affect things being found, but it does mean that the number of resources in code will often not match the number of resources that you select for importing (because one imported resource might result in 3 or 4 resources in code).
* A lot of cloud resources don't get created in code. Some are default always-present (e.g. default VPC), and some are created by AWS when another resource is created (e.g. a swathe of CloudFront origin request policies get created the first time you create a CloudFront Distribution).
* Some resources are global but AWS sorts this for you: you can create a resource with a provider in a specific region, and the resource is correctly created globally. However, Insights doesn't know this, so it'll create a global provider and use it for those resources. Often (or always?) you can remove the global provider.

Eventually, I settled on AI assist query `(modue:cloudfront) or (category is storage)`, which mapped to Pulumi query `module:cloudfront OR category:storage`. From these results (34 at time of testing), I selected all the resources that weren't created by AWS (just 6, though that shows as 16 initially.. no idea why). Note that this step required system knowledge, either of the resource creation process or of how AWS works (and given how wide AWS is, the creation process for your own resources is likely to be easier to grok).

### Code Generate, Enhance and Fix

The next step of the wizard generates the code. The initial code is basic, and frequently doesn't work. There is an Enhance button that refactors the code; this improves variables names, looping and code reuse, and a few other things, but doesn't make the code actually compile :) Hit it anyway. You can even hit it a few times, it tends to get slightly better with repeats. Determinism, rest in peace.

A particular bugbear for me is policy code. In this example, bucket policy is the only one we have to contend with, but in some cases there can be many policies, all built incorrectly (in my opinion). Presumably it always gets it wrong because the vast majority of example code out there on the internet are built using one of two patterns, but there are three, and the third is the most readable and most idiomatic, at least in the C# and JS/TS runtimes.

This is the provided code, that I consider wrong:
```csharp
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

    return policy.ToJson();
}),
```
Other than the one compilation error (`ToJson()` does not exist on the `policy` object: the code should have used `JsonSerializer.Serialize()`), this code is adequate, but not idiomatic. For extra type safety, IDE help and pure readability, this code is much better:
```csharp
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
    }
}
```

Other changes that had to be made:
* Pulumi resource names needed to be revisited. Often they're derived from the actual names of the resources; generally, it should be the other way around.
* Lots of hardcoding of cloud resources names that didn't make sense and needed to be removed. In this little project, the CloudFront distribution and S3 bucket names are both globally unique, so you can't re-use the original as the new one. Plus, that's Pulumi's job.
* The Insights-generated code tends to follow the idiom of six months ago. For AWS at least, this is quite different to the idiom of now, because of the change from v6 to v7. In particular, S3 buckets have greatly changed, with a lot of configuration extracted to smaller linked resources, instead of nested into one big resource. So some reworking needs to happen.
* Pulumi provides enums, constants etc. to make code more readable, but the generated code uses the literal values. With experience, you can spot these "sugary" refactorings.
* Some compiler warnings can be easily eliminated. Minor refactoring work here.
* No stack outputs are created, and you'll usually want at least one, so you know what you've created.

Refactor before running, and importantly, _remove all the importIds_! Because you'll be creating the resources in a new region :)
