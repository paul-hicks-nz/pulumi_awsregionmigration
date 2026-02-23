import * as pulumi from "@pulumi/pulumi";
import * as aws from "@pulumi/aws";
import { PolicyDocumentVersion, PolicyStatementEffect } from "@pulumi/aws/types/enums/iam";

interface MicroappArgs {
  region: aws.Region
}

export class Microapp extends pulumi.ComponentResource {
  public service: aws.apigateway.Stage;
  private region: aws.Region;
  private integrations: aws.apigateway.Integration[] = [];
  private opts: pulumi.ComponentResourceOptions;

  constructor(name: string, args: MicroappArgs, opts: pulumi.ComponentResourceOptions) {
    super("sentify:demo:apiapp", name, args, opts);
    this.opts = opts;
    this.region = args.region;

    const table = new aws.dynamodb.Table("Values", {
      attributes: [{ name: "id", type: "N" }],
      hashKey: "id",
      billingMode: "PAY_PER_REQUEST"
    }, { ...this.opts, parent: this });

    const role = this.allowApiGatewayToAccess(table);
    const api = this.createDemoApi(role);

    this.service = this.deployToApiGatewayService(api);
    this.registerOutputs();
  }

  // ============== Just implementation details from here down. ===============
  private allowApiGatewayToAccess(table: aws.dynamodb.Table): aws.iam.Role {
    const role = new aws.iam.Role("allowApiGatewayToAccessTable", {
      description: "Allow the API gateway read and write access to a DynamoDb table",
      assumeRolePolicy: {
        Version: PolicyDocumentVersion.PolicyDocumentVersion_2012_10_17,
        Statement: [{
          Effect: PolicyStatementEffect.ALLOW,
          Action: ["sts:AssumeRole"],
          Principal: {
            Service: "apigateway.amazonaws.com"
          }
        }]
      }
    }, { ...this.opts, parent: this });

    const policy = new aws.iam.Policy("allowApiGatewayToAccessTable", {
      description: "Allow the API gateway read and write access to a DynamoDb table",
      policy: {
        Id: "Demo_RegionMigration",
        Version: PolicyDocumentVersion.PolicyDocumentVersion_2012_10_17,
        Statement: [{
          Effect: PolicyStatementEffect.ALLOW,
          Action: ["dynamodb:PutItem", "dynamodb:GetItem"],
          Resource: table.arn
        }]
      }
    }, { ...this.opts, parent: role });

    new aws.iam.RolePolicyAttachment("allowApiGatewayToAccessTable", { role: role, policyArn: policy.arn }, { ...this.opts, parent: policy });
    return role;
  }

  private createDemoApi(role: aws.iam.Role): aws.apigateway.RestApi {
    const api = new aws.apigateway.RestApi("DemoApp", {
      description: "Add and read values from a DynamoDB table"
    }, { ...this.opts, parent: this });

    const app = new aws.apigateway.Resource("table", {
      restApi: api.id,
      parentId: api.rootResourceId,
      pathPart: "table"
    }, { ...this.opts, parent: this });

    const addValuePath = new aws.apigateway.Method("add", {
      restApi: api,
      resourceId: app.id,
      authorization: "NONE",
      apiKeyRequired: false,
      httpMethod: "POST"
    }, { ...this.opts, parent: app });
    this.integrations.push(new aws.apigateway.Integration("add", {
      restApi: api.id,
      resourceId: app.id,
      credentials: role.arn,
      httpMethod: addValuePath.httpMethod,
      type: "AWS",
      integrationHttpMethod: "POST",
      uri: `arn:aws:apigateway:${this.region}:dynamodb:action/PutItem`
    }, { ...this.opts, parent: addValuePath }));

    const getValuePath = new aws.apigateway.Method("get", {
      restApi: api,
      resourceId: app.id,
      authorization: "NONE",
      apiKeyRequired: false,
      httpMethod: "GET"
    }, { ...this.opts, parent: app });

    // This ensures that Pulumi waits until all the integrations are deployed before deploying the API gateway.
    // Without this, running `pulumi up` in a new stack will fail initially (though it will succeed on re-upping).
    this.integrations.push(new aws.apigateway.Integration("get", {
      restApi: api.id,
      resourceId: app.id,
      credentials: role.arn,
      httpMethod: getValuePath.httpMethod,
      type: "AWS",
      integrationHttpMethod: "POST",
      uri: `arn:aws:apigateway:${this.region}:dynamodb:action/PutItem`
    }, { ...this.opts, parent: getValuePath }));

    return api;
  }

  private deployToApiGatewayService(api: aws.apigateway.RestApi): aws.apigateway.Stage {
    const deployment = new aws.apigateway.Deployment("demoapp", { restApi: api }, {
      ...this.opts, parent: this, dependsOn: this.integrations
    });
    const stage = new aws.apigateway.Stage(`demoapp-${this.region}`, {
      restApi: api, stageName: "Demo", deployment: deployment
    }, { ...this.opts, parent: deployment });
    new aws.apigateway.UsagePlan("protection", {
      apiStages: [{ apiId: api.id, stage: stage.stageName }], throttleSettings: { burstLimit: 1, rateLimit: 1 }
    }, { ...this.opts, parent: stage });
    return stage;
  }
}
