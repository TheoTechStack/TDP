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

output "demo_password_secret" {
  description = "Secrets Manager secret holding the demo accounts' password (the name, not the value)."
  value       = aws_secretsmanager_secret.demo_password.name
}
