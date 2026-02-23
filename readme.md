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
