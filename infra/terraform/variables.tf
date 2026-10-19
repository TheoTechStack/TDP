variable "name" {
  description = "Prefix for every resource name."
  type        = string
  default     = "dispute-portal"
}

variable "region" {
  description = "AWS region. Defaults to Ireland; af-south-1 (Cape Town) works once enabled on the account."
  type        = string
  default     = "eu-west-1"
}

variable "image_tag" {
  description = "Tag of the image in ECR to deploy (the deploy script uses the git commit SHA)."
  type        = string
}

variable "cpu_architecture" {
  description = "ARM64 (Graviton, cheaper; matches Apple-silicon builds) or X86_64."
  type        = string
  default     = "ARM64"

  validation {
    condition     = contains(["ARM64", "X86_64"], var.cpu_architecture)
    error_message = "cpu_architecture must be ARM64 or X86_64."
  }
}

variable "desired_count" {
  description = "Number of app tasks. Two spreads the service across both availability zones."
  type        = number
  default     = 2
}

variable "seed_demo_data" {
  description = "Seed demo users and transactions on first start. Turn off for a real environment."
  type        = bool
  default     = true
}

variable "db_instance_class" {
  type    = string
  default = "db.t4g.micro"
}

variable "db_multi_az" {
  description = "Standby replica in a second AZ. Off by default to keep a demo cheap; on for production."
  type        = bool
  default     = false
}

variable "db_backup_retention_days" {
  description = "Days of automated RDS backups. 7 for real use; AWS free-plan accounts only allow 1."
  type        = number
  default     = 7
}

variable "deletion_protection" {
  description = "Protect the database from `terraform destroy`. Turn on for anything that matters."
  type        = bool
  default     = false
}

variable "certificate_arn" {
  description = "Optional ACM certificate ARN. When set, the ALB serves HTTPS and redirects HTTP to it."
  type        = string
  default     = ""
}
