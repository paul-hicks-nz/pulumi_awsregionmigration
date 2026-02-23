Here is a comprehensive approach to duplicating all resources from an unmanaged AWS region to another AWS region using Pulumi Insights, ESC, and IaC — without any AI tools.

# Strategy: Discover, Export, Import, Deploy

The workflow has four phases:
## Phase 1: Discover Unmanaged Resources with Pulumi Insights

Use Pulumi Insights resource search to find all unmanaged resources in the source region. You need an AWS account scan configured in Pulumi Cloud for this to work.

In the Pulumi Cloud UI or via API:

```
managed:Other package:aws .region:us-east-1
```

This returns all AWS resources in us-east-1 that are not managed by Pulumi IaC. You can also use the API:

```
# List all unmanaged resources in the source region
curl -s -H "Authorization: token $PULUMI_ACCESS_TOKEN" \
  "https://api.pulumi.com/api/orgs/puluminaries/search/resources?query=managed%3AOther+package%3Aaws+.region%3Aus-east-1" | jq '.resources[] | {type, id, name}'
  ```

Export the full list to a file for processing:

```
curl -s -H "Authorization: token $PULUMI_ACCESS_TOKEN" \
  "https://api.pulumi.com/api/orgs/puluminaries/search/resources?query=managed%3AOther+package%3Aaws+.region%3Aus-east-1&properties=true" \
  | jq '.resources' > /tmp/unmanaged-resources.json
```

## Phase 2: Import Resources into a Pulumi Stack

Create a new Pulumi project and stack to manage these resources, then bulk-import them so Pulumi captures their current state.

```
# Create a new project
mkdir region-migration && cd region-migration
pulumi new aws-python --generate-only --yes
pulumi stack init dev

# Configure the SOURCE region
pulumi config set aws:region us-east-1
```

Now use pulumi import to bring each discovered resource under Pulumi management. The Insights results give you the Pulumi type and the physical id for each resource. You can script this:

```
# Example: import each resource from the discovery output
# The format is: pulumi import <type> <logical-name> <physical-id> --yes
cat /tmp/unmanaged-resources.json | jq -r '.[] | "\(.type) \(.name) \(.id)"' | while read type name id; do
  pulumi import "$type" "$name" "$id" --yes --skip-preview 2>&1 | tee -a /tmp/import-log.txt
done
```

After import, Pulumi generates the corresponding IaC code in `__main__.py`. Review and clean up the generated code. The imported resources now have their full configuration captured in Pulumi state.

## Phase 3: Set Up ESC for Multi-Region Credentials

Use Pulumi ESC to manage AWS credentials and region configuration for both the source and target stacks. This avoids hardcoding credentials and makes the region switch clean.

```
# Create ESC environments for source and target regions
pulumi env init puluminaries/region-migration/source-region
pulumi env init puluminaries/region-migration/target-region
```
Source region environment (source-region):

```
values:
  pulumiConfig:
    aws:region: us-east-1
  environmentVariables:
    AWS_REGION: us-east-1
```

Target region environment (target-region):

```
values:
  pulumiConfig:
    aws:region: us-west-2
  environmentVariables:
    AWS_REGION: us-west-2
```

If you use OIDC for AWS credentials, add the aws-login provider to a shared base environment and import it from both:

```
# shared/aws-creds environment
imports: []
values:
  aws:
    login:
      fn::open::aws-login:
        oidc:
          roleArn: arn:aws:iam::123456789012:role/PulumiOIDC
          sessionName: pulumi-migration
  environmentVariables:
    AWS_ACCESS_KEY_ID: ${aws.login.accessKeyId}
    AWS_SECRET_ACCESS_KEY:
      fn::secret: ${aws.login.secretAccessKey}
    AWS_SESSION_TOKEN:
      fn::secret: ${aws.login.sessionToken}
```

Then both region environments import it:

```
# source-region
imports:
  - shared/aws-creds
values:
  pulumiConfig:
    aws:region: us-east-1
```

Link the environments to stacks:

```
# Source stack uses source-region config
pulumi stack select dev
pulumi config env add region-migration/source-region

# Create and configure target stack
pulumi stack init target
pulumi config env add region-migration/target-region
```

## Phase 4: Deploy to the Target Region

Now the key step. You have two approaches depending on whether you want to copy (keep source, create in target) or move (recreate in target, eventually decommission source):

### Approach A: Duplicate (copy to new region, source untouched)

    Take the generated IaC code from the import step.
    Remove all import resource options from the code (these were only needed to adopt existing resources).
    Remove any hardcoded region references or ARNs that are region-specific. Replace them with config lookups:

```
import pulumi
config = pulumi.Config("aws")
region = config.require("region")
```

    Remove physical names so Pulumi auto-names resources in the target region (avoids collisions).
    Handle region-specific resources that can't be directly duplicated (e.g., AMI IDs differ by region — use aws.ec2.get_ami lookups instead of hardcoded IDs).
    Deploy to the target stack:

```
pulumi stack select target
# Preview first to verify
# Then deploy via your CI/CD pipeline or Pulumi Deployments
```

### Approach B: Move (migrate state)

If you want to truly move resources, you'd deploy to the target region first (as above), verify everything works, then tear down the source stack.
Summary of the Full Workflow
Step 	Tool 	Action
1. 	Pulumi Insights 	Query managed:Other package:aws .region:<source> to discover all unmanaged resources
2. 	Pulumi IaC 	pulumi import each resource to capture its configuration as code
3. 	Pulumi ESC 	Create per-region environments with credentials and aws:region config
4. 	Pulumi IaC 	Clean up generated code (remove imports, hardcoded ARNs, physical names), parameterize region
5. 	Pulumi IaC 	Create a target stack linked to the target-region ESC environment
6. 	Pulumi IaC 	Preview and deploy to the target region

## Key Considerations

  * Region-specific resources: Some resources like AMI IDs, VPC endpoint service names, and AZ names differ by region. You'll need to replace hardcoded values with dynamic lookups.
  * Global resources: Some AWS resources (IAM roles, Route 53 hosted zones, CloudFront distributions) are global and don't need duplication.
  * Cross-region dependencies: If resources reference each other across regions (e.g., S3 replication, cross-region VPC peering), those relationships need to be re-established manually in code.
  * Data migration: Pulumi handles infrastructure configuration, not data. S3 object contents, RDS data, DynamoDB items, etc. need separate data migration tooling (e.g., aws s3 sync, DMS, snapshots).
  * Ordering: Import resources in dependency order (VPCs before subnets before instances) to avoid reference errors.
