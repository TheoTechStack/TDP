#!/usr/bin/env bash
# Build the image, push it to ECR and roll it out with Terraform.
# The same script runs in GitHub Actions (on every push to main) and on a workstation (fallback).
#
# Prereqs: AWS credentials for the target account, Docker, Terraform >= 1.8, and the state bucket
# from infra/bootstrap.
#
#   ./scripts/deploy.sh                                   # deploy the current commit to eu-west-1
#   TF_VAR_db_backup_retention_days=1 ./scripts/deploy.sh # AWS free-plan accounts
#   AWS_REGION=af-south-1 ./scripts/deploy.sh             # another region
set -euo pipefail

cd "$(dirname "$0")/.."
REGION="${AWS_REGION:-eu-west-1}"
ARCH="${CPU_ARCHITECTURE:-ARM64}"
PLATFORM=$([ "$ARCH" = "ARM64" ] && echo linux/arm64 || echo linux/amd64)

if [ -n "$(git status --porcelain)" ]; then
  echo "Working tree has uncommitted changes; commit first so the image tag identifies the code." >&2
  exit 1
fi
TAG="$(git rev-parse --short=12 HEAD)"

ACCOUNT_ID="$(aws sts get-caller-identity --query Account --output text)"
STATE_BUCKET="${TF_STATE_BUCKET:-tdp-tfstate-$ACCOUNT_ID}"

TF="terraform -chdir=infra/terraform"
TF_VARS=(-var "region=$REGION" -var "cpu_architecture=$ARCH")

echo "==> Terraform init (state: s3://$STATE_BUCKET)"
$TF init -input=false \
  -backend-config="bucket=$STATE_BUCKET" \
  -backend-config="region=$REGION"

echo "==> Ensuring the ECR repository exists"
$TF apply -input=false -auto-approve "${TF_VARS[@]}" -var "image_tag=$TAG" -target=aws_ecr_repository.app
REPO="$($TF output -raw ecr_repository_url)"

if aws ecr describe-images --region "$REGION" --repository-name "${REPO#*/}" --image-ids "imageTag=$TAG" >/dev/null 2>&1; then
  echo "==> Image $TAG already in ECR; skipping build (tags are immutable)"
else
  echo "==> Building $REPO:$TAG ($PLATFORM)"
  docker build --platform "$PLATFORM" -t "$REPO:$TAG" .

  echo "==> Pushing"
  aws ecr get-login-password --region "$REGION" | docker login --username AWS --password-stdin "${REPO%%/*}"
  docker push "$REPO:$TAG"
fi

echo "==> Applying infrastructure + new task definition"
$TF apply -input=false -auto-approve "${TF_VARS[@]}" -var "image_tag=$TAG"

echo "==> Waiting for the service to become stable"
aws ecs wait services-stable --region "$REGION" \
  --cluster "$($TF output -raw ecs_cluster)" --services "$($TF output -raw ecs_service)"

URL="$($TF output -raw app_url)"
echo "Deployed $TAG -> $URL"

# In GitHub Actions, show the result on the run's summary page.
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  {
    echo "### Deployed \`$TAG\`"
    echo ""
    echo "| | |"
    echo "|---|---|"
    echo "| URL | $URL |"
    echo "| Region | $REGION |"
    echo "| Image | \`$REPO:$TAG\` |"
    echo "| Architecture | $ARCH |"
  } >> "$GITHUB_STEP_SUMMARY"
fi
