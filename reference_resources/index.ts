import * as aws from "@pulumi/aws";
import * as microapp from "./Microapp";

const region = aws.Region.APSoutheast2;
const provider = new aws.Provider("origregion", {
  region: region,
  profile: "admin-cms",
  defaultTags: {
    tags: {
      moveto: region
    }
  }
});

const app = new microapp.Microapp("demo", { region }, { provider });
export const apiUrl = app.service.invokeUrl;
