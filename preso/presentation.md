---
marp: true
theme: default
class:
 - invert
 #- lead
backgroundColor: #512BD4
# This sets the deck's background
backgroundImage: linear-gradient(to left, #512BD4, #1B1F3B)
# This sets the slide's background
_backgroundImage: url("assets/background.png")
style: |
  # CSS goes here
  pre {
    class: !invert
  }

author: Paul Hicks
title:
description:
keywords: pulumi iac ai
---

# A Slide!
## A topic
### A subtopic

---

# Intro Slide

<!--
Goals for the project (ap-southeast-6)
Goals for the presentation (new Pulumi features, and where AI let me down.)
Show of hands: I can add in a few extra hints, tips and demos about the Pulumi features I use that aren't particularly AI-related. SHould I?
-->

---

# Technical Intro
- I have configured OIDC in Pulumi ESC for AWS
  - All connection from Pulumi to AWS use short-term credentials
  - No long-lived secrets are stored anywhere
- I have enabled the GitHub in my Pulumi organisation's settings
  - Enables Pulumi IaC to enrich PRs with previews and more
  - Enables Pulumi Neo to read and review code, and create PRs for changes it suggests (or makes)

---

# Discovering Unmanaged Resources
<!--
Pulumi Insights is a subscription-only feature that scans an "Account" to find cloud resources, both managed and unmanaged.
A Pulumi Insights account corresponds very closely to as AWS account, an Azure Subscription or a GCP Project.
This is an easy bit to demo but it's not really relevant to the AI-themed evening. Refer to show of hands above. If it's voted in, navigate to
https://app.pulumi.com/paulhicks_demo/insights/accounts/create
and follow the wizard.
-->
* Create an Insights "Account" using the AWS OIDC connection already created in Pulumi ESC
* View the created account, select "Actions" and "Scan"
* Wait a few minutes (about 4 for this Account)
* Optionally watch what the scan is doing by navigating through teh "Scans" tab
* Navigate to "Resources" in the left bar, and start playing with Pulumi AI Assist!

---


---
```ts
import * as aws from "@pulumi/pulumi-aws";
```

```csharp
using System;
```
---
