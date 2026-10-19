#!/usr/bin/env bash
# Build the image, push it to ECR and roll it out with Terraform.
# Prereqs: AWS credentials for the target account, Docker, Terraform >= 1.8.
#   ./scripts/deploy.sh              # deploy current commit
#   AWS_REGION=eu-west-1 ./scripts/deploy.sh
set -euo pipefail

cd "$(dirname "$0")/.."
REGION="${AWS_REGION:-af-south-1}"
ARCH="${CPU_ARCHITECTURE:-ARM64}"
PLATFORM=$([ "$ARCH" = "ARM64" ] && echo linux/arm64 || echo linux/amd64)

if [ -n "$(git status --porcelain)" ]; then
  echo "Working tree has uncommitted changes; commit first so the image tag identifies the code." >&2
  exit 1
fi
TAG="$(git rev-parse --short=12 HEAD)"

TF="terraform -chdir=infra/terraform"
TF_VARS=(-var "region=$REGION" -var "cpu_architecture=$ARCH")

echo "==> Terraform init"
$TF init -input=false

echo "==> Ensuring the ECR repository exists"
$TF apply -input=false -auto-approve "${TF_VARS[@]}" -var "image_tag=$TAG" -target=aws_ecr_repository.app
REPO="$($TF output -raw ecr_repository_url)"

echo "==> Building $REPO:$TAG ($PLATFORM)"
docker build --platform "$PLATFORM" -t "$REPO:$TAG" .

echo "==> Pushing"
aws ecr get-login-password --region "$REGION" | docker login --username AWS --password-stdin "${REPO%%/*}"
docker push "$REPO:$TAG"

echo "==> Applying infrastructure + new task definition"
$TF apply -input=false -auto-approve "${TF_VARS[@]}" -var "image_tag=$TAG"

echo "==> Waiting for the service to become stable"
aws ecs wait services-stable --region "$REGION" \
  --cluster "$($TF output -raw ecs_cluster)" --services "$($TF output -raw ecs_service)"

echo "Deployed $TAG -> $($TF output -raw app_url)"
