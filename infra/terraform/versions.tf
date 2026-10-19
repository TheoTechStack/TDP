terraform {
  required_version = ">= 1.8"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }

  # Remote state in S3 (created by infra/bootstrap) so CI and laptops share one source of truth.
  # Bucket and region are supplied at init time by scripts/deploy.sh:
  #   -backend-config="bucket=tdp-tfstate-<account-id>" -backend-config="region=<region>"
  # use_lockfile: S3-native locking, so two applies can never run at once (no DynamoDB table).
  backend "s3" {
    key          = "dispute-portal/terraform.tfstate"
    encrypt      = true
    use_lockfile = true
  }
}

provider "aws" {
  region = var.region

  default_tags {
    tags = {
      Project   = var.name
      ManagedBy = "terraform"
    }
  }
}
