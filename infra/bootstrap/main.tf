# One-time setup, run from a workstation with admin credentials (never from CI):
#
#   terraform -chdir=infra/bootstrap init
#   terraform -chdir=infra/bootstrap apply
#
# Creates what the CI/CD pipeline needs before it can run:
#   1. An S3 bucket for the main stack's Terraform state (versioned, encrypted, private).
#   2. GitHub's OIDC identity provider, so Actions can get short-lived AWS credentials (no stored keys).
#   3. A deploy role that only the configured repo's `production` environment can assume.
#
# Its own state stays local (infra/bootstrap/terraform.tfstate, git-ignored): it is tiny,
# changes rarely, and can be re-imported if lost.

terraform {
  required_version = ">= 1.8"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
}

variable "region" {
  description = "Region for the state bucket. Must match the region the app is deployed to."
  type        = string
  default     = "eu-west-1"
}

variable "github_repository" {
  description = "owner/repo allowed to deploy. Case-sensitive, exactly as GitHub shows it."
  type        = string
  default     = "TheoTechStack/TDP"
}

variable "github_environment" {
  description = "GitHub environment the deploy job runs in; only that environment can assume the role."
  type        = string
  default     = "production"
}

provider "aws" {
  region = var.region

  default_tags {
    tags = {
      Project   = "dispute-portal"
      ManagedBy = "terraform-bootstrap"
    }
  }
}

data "aws_caller_identity" "current" {}

locals {
  # Account ID makes the name globally unique and lets deploy.sh derive it without configuration.
  state_bucket = "tdp-tfstate-${data.aws_caller_identity.current.account_id}"
}

# ---- 1. Terraform state bucket --------------------------------------------------------------

resource "aws_s3_bucket" "state" {
  bucket = local.state_bucket

  lifecycle {
    prevent_destroy = true # losing state means Terraform forgets what it created
  }
}

resource "aws_s3_bucket_versioning" "state" {
  bucket = aws_s3_bucket.state.id
  versioning_configuration {
    status = "Enabled" # every state write is kept; a bad apply can be rolled back
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "state" {
  bucket = aws_s3_bucket.state.id
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_public_access_block" "state" {
  bucket                  = aws_s3_bucket.state.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_policy" "state_tls_only" {
  bucket = aws_s3_bucket.state.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Sid       = "DenyInsecureTransport"
      Effect    = "Deny"
      Principal = "*"
      Action    = "s3:*"
      Resource  = [aws_s3_bucket.state.arn, "${aws_s3_bucket.state.arn}/*"]
      Condition = { Bool = { "aws:SecureTransport" = "false" } }
    }]
  })
}

# ---- 2. GitHub Actions OIDC provider ---------------------------------------------------------

resource "aws_iam_openid_connect_provider" "github" {
  url            = "https://token.actions.githubusercontent.com"
  client_id_list = ["sts.amazonaws.com"]
  # AWS validates GitHub's certificate itself; these are GitHub's published thumbprints.
  thumbprint_list = [
    "6938fd4d98bab03faadb97b34396831e3780aea1",
    "1c58a3a8518e8759bf075b76b750d4f2df264fcd",
  ]
}

# ---- 3. Deploy role --------------------------------------------------------------------------

data "aws_iam_policy_document" "github_trust" {
  statement {
    actions = ["sts:AssumeRoleWithWebIdentity"]

    principals {
      type        = "Federated"
      identifiers = [aws_iam_openid_connect_provider.github.arn]
    }

    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:aud"
      values   = ["sts.amazonaws.com"]
    }

    # Only jobs running in this repo's production environment. Forks, other branches' jobs
    # without the environment, and other repos all fail this check.
    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:sub"
      values   = ["repo:${var.github_repository}:environment:${var.github_environment}"]
    }
  }
}

resource "aws_iam_role" "deploy" {
  name                 = "tdp-github-deploy"
  assume_role_policy   = data.aws_iam_policy_document.github_trust.json
  max_session_duration = 3600
}

# Terraform creates VPCs, IAM roles, RDS, ECS and more, so the deploy role needs broad rights.
# The control here is the trust policy above (who can assume it), not the permissions.
# Follow-up: replace with a least-privilege policy scoped to the resources in infra/terraform.
resource "aws_iam_role_policy_attachment" "deploy_admin" {
  role       = aws_iam_role.deploy.name
  policy_arn = "arn:aws:iam::aws:policy/AdministratorAccess"
}

# ---- Outputs ---------------------------------------------------------------------------------

output "state_bucket" {
  value = aws_s3_bucket.state.bucket
}

output "deploy_role_arn" {
  description = "Set as the GitHub repository variable AWS_DEPLOY_ROLE_ARN."
  value       = aws_iam_role.deploy.arn
}
