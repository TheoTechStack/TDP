output "app_url" {
  description = "Open this in a browser."
  value       = "${local.https ? "https" : "http"}://${aws_lb.main.dns_name}"
}

output "ecr_repository_url" {
  value = aws_ecr_repository.app.repository_url
}

output "ecs_cluster" {
  value = aws_ecs_cluster.main.name
}

output "ecs_service" {
  value = aws_ecs_service.app.name
}

output "log_group" {
  value = aws_cloudwatch_log_group.app.name
}
